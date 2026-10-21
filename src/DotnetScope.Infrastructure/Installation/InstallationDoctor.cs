using System.Text.Json;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Resolution;
using DotnetScope.Core.Versions;

namespace DotnetScope.Infrastructure.Installation;

public enum FindingSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Problema encontrado pelo doctor; <see cref="Repaired"/> só é verdadeiro com <c>--repair</c>.</summary>
public sealed record DoctorFinding(string Code, FindingSeverity Severity, string Message, bool Repairable, bool Repaired);

/// <summary>
/// Verifica raízes .NET e o manifesto: staging remanescente, operações interrompidas (pelo journal), registros sem
/// arquivos e falhas do host. Com reparo, desfaz instalações e remoções interrompidas e remove registros órfãos.
/// </summary>
public sealed class InstallationDoctor(InstallationManifestStore manifest, IDiagnosticSink diagnostics)
{
    internal TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<DoctorFinding>> CheckAsync(IEnumerable<string> roots, bool repair, CancellationToken cancellationToken)
    {
        var findings = new List<DoctorFinding>();
        foreach (var root in roots.Distinct(StringComparer.Ordinal).Where(Directory.Exists))
        {
            InstallLock installLock;
            try
            {
                installLock = await InstallLock.AcquireAsync(
                    Path.Combine(root, ArchiveInstaller.LockFileName), LockTimeout, NullDiagnosticSink.Instance, cancellationToken).ConfigureAwait(false);
            }
            catch (DotnetScopeException exception) when (exception.ExitCode == ExitCodes.Conflict)
            {
                findings.Add(new DoctorFinding("root-busy", FindingSeverity.Warning, $"{root}: outra operação em andamento; não verificada.", false, false));
                continue;
            }

            using (installLock)
            {
                foreach (var staging in Directory.EnumerateDirectories(root, ArchiveInstaller.StagingPrefix + "*"))
                {
                    findings.Add(CheckStaging(root, staging, repair));
                }

                findings.AddRange(await CheckRecordsAsync(root, repair, cancellationToken).ConfigureAwait(false));
            }
        }

        return findings;
    }

    private DoctorFinding CheckStaging(string root, string staging, bool repair)
    {
        var journalPath = Path.Combine(staging, "journal.json");
        var trash = Path.Combine(staging, "trash");
        var host = DotnetLayout.HostExecutable(OperatingSystem.IsWindows());
        var hostBackup = Path.Combine(staging, "backup", host);
        MergeJournal? journal = null;
        try
        {
            journal = File.Exists(journalPath)
                ? JsonSerializer.Deserialize(File.ReadAllBytes(journalPath), InstallationJsonContext.Default.MergeJournal)
                : null;
        }
        catch (JsonException)
        {
        }

        if (journal is null)
        {
            // Sem journal, só é seguro apagar staging que não guarda conteúdo da raiz (lixeira ou backup do host).
            var holdsRootContent = (Directory.Exists(trash) && Directory.EnumerateFileSystemEntries(trash).Any()) || File.Exists(hostBackup);
            if (holdsRootContent)
            {
                return new DoctorFinding("unknown-staging", FindingSeverity.Error, $"{staging} contém arquivos da raiz sem journal; verifique manualmente.", false, false);
            }

            if (repair)
            {
                Directory.Delete(staging, recursive: true);
            }

            return new DoctorFinding("orphan-staging", FindingSeverity.Warning, $"Diretório temporário remanescente: {staging}.", true, repair);
        }

        if (!journal.Added.All(DotnetLayout.IsValidLeaf))
        {
            return new DoctorFinding("invalid-journal", FindingSeverity.Error, $"{journalPath} contém caminhos inválidos; nada foi alterado.", false, false);
        }

        var records = manifest.Read().Where(r => InstallLocations.SameRoot(r.Root, root)).ToList();
        var recorded = journal.Component is not null && records.Any(r => r.Component.ToName() == journal.Component && r.Version == journal.Version);

        if (journal.Operation == "uninstall")
        {
            // Registro ainda presente (ou remoção forçada de componente externo): a remoção não terminou e é desfeita.
            var restore = recorded || !records.Any(r => r.Component.ToName() == journal.Component);
            if (repair)
            {
                if (restore)
                {
                    ArchiveUninstaller.Restore(root, trash, journal.Added);
                }

                Directory.Delete(staging, recursive: true);
                ArchiveUninstaller.RemoveEmptyContainers(root, journal.Added);
            }

            return new DoctorFinding(
                "interrupted-uninstall",
                FindingSeverity.Error,
                restore
                    ? $"Remoção interrompida de {journal.Component} {journal.Version} em {root}; as pastas movidas serão restauradas."
                    : $"Remoção de {journal.Component} {journal.Version} concluída com diretório temporário remanescente: {staging}.",
                true,
                repair);
        }

        var hostMissing = !File.Exists(Path.Combine(root, host));
        if (recorded)
        {
            if (repair)
            {
                if (hostMissing && File.Exists(hostBackup))
                {
                    FileSystemRetry.Run(() => File.Move(hostBackup, Path.Combine(root, host)));
                }

                Directory.Delete(staging, recursive: true);
            }

            return new DoctorFinding("orphan-staging", FindingSeverity.Warning, $"Diretório temporário de instalação concluída: {staging}.", true, repair);
        }

        if (repair)
        {
            var referenced = records.SelectMany(r => r.Paths).ToHashSet(StringComparer.Ordinal);
            var orphaned = journal.Added.Where(leaf => !referenced.Contains(leaf)).ToList();
            foreach (var leaf in Enumerable.Reverse(orphaned))
            {
                var path = DotnetLayout.Resolve(root, leaf);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            // O backup do host é sempre o caminho derivado do staging, nunca um caminho lido do journal.
            if (File.Exists(hostBackup))
            {
                FileSystemRetry.Run(() => File.Move(hostBackup, Path.Combine(root, host), overwrite: true));
            }

            ArchiveUninstaller.RemoveEmptyContainers(root, orphaned);
            Directory.Delete(staging, recursive: true);
        }

        return new DoctorFinding(
            "interrupted-install",
            FindingSeverity.Error,
            $"Instalação interrompida{(journal.Component is null ? string.Empty : $" de {journal.Component} {journal.Version}")} em {root}: {journal.Added.Count} pastas movidas sem registro no manifesto.",
            true,
            repair);
    }

    /// <summary>
    /// Remove registros sem arquivos transferindo a posse das folhas que ainda existem para quem as usa; folhas que
    /// ninguém mais usa pertencem à instalação quebrada e são apagadas.
    /// </summary>
    private static List<InstallationRecord> RemoveStale(IReadOnlyList<InstallationRecord> all, List<InstallationRecord> stale, string root)
    {
        static bool Same(InstallationRecord a, InstallationRecord b) => InstallLocations.SameRoot(a.Root, b.Root) && a.Component == b.Component && a.Version == b.Version;
        var remaining = all.Where(r => !stale.Any(x => Same(x, r))).ToList();
        foreach (var record in stale.Where(r => InstallLocations.SameRoot(r.Root, root)))
        {
            foreach (var leaf in record.AddedPaths)
            {
                var path = DotnetLayout.Resolve(root, leaf);
                if (!LayoutMerger.EntryExists(path))
                {
                    continue;
                }

                var index = remaining.FindIndex(r => InstallLocations.SameRoot(r.Root, root) && r.Paths.Contains(leaf, StringComparer.Ordinal));
                if (index >= 0)
                {
                    if (!remaining[index].AddedPaths.Contains(leaf, StringComparer.Ordinal))
                    {
                        remaining[index] = remaining[index] with { AddedPaths = [.. remaining[index].AddedPaths, leaf] };
                    }
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else
                {
                    File.Delete(path);
                }
            }

            ArchiveUninstaller.RemoveEmptyContainers(root, record.AddedPaths);
        }

        return remaining;
    }

    private async Task<IEnumerable<DoctorFinding>> CheckRecordsAsync(string root, bool repair, CancellationToken cancellationToken)
    {
        var findings = new List<DoctorFinding>();
        var records = manifest.Read().Where(r => InstallLocations.SameRoot(r.Root, root)).ToList();
        var stale = new List<InstallationRecord>();
        foreach (var record in records)
        {
            var name = $"{record.Component.ToName()} {record.Version}";
            if (!DotnetVersion.TryParse(record.Version, out var version)
                || !LayoutMerger.EntryExists(Path.Combine(root, DotnetLayout.PrimaryPath(record.Component, version))))
            {
                stale.Add(record);
                findings.Add(new DoctorFinding("stale-record", FindingSeverity.Error, $"{name} está no manifesto, mas não existe em {root}.", true, repair));
                continue;
            }

            var missing = record.AddedPaths.Where(leaf => !LayoutMerger.EntryExists(Path.Combine(root, leaf))).ToList();
            if (missing.Count > 0)
            {
                findings.Add(new DoctorFinding(
                    "missing-files",
                    FindingSeverity.Warning,
                    $"{name}: {missing.Count} pastas registradas não existem mais ({string.Join(", ", missing.Take(3))}).",
                    false,
                    false));
            }

            var artifact = new ResolvedArtifact(
                record.Component, version, null, record.Version, record.Rid,
                new ReleaseFile(record.FileName, record.Rid, record.Url, record.Sha512, true));
            var validation = await DotnetHostValidator.ValidateAsync(root, artifact, cancellationToken).ConfigureAwait(false);
            if (!validation.Succeeded)
            {
                findings.Add(new DoctorFinding("host-validation", FindingSeverity.Error, $"{name}: {validation.Detail}.", false, false));
            }
        }

        if (repair && stale.Count > 0)
        {
            await manifest.UpdateAsync(all => RemoveStale(all, stale, root), diagnostics, cancellationToken).ConfigureAwait(false);
        }

        return findings;
    }
}
