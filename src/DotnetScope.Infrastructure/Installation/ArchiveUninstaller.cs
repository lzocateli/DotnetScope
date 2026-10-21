using System.Text.Json;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;

namespace DotnetScope.Infrastructure.Installation;

/// <summary>Plano de remoção: folhas a apagar e folhas mantidas por ainda pertencerem a outra instalação.</summary>
/// <param name="Record">Registro do manifesto; nulo quando o componente foi instalado por outra ferramenta.</param>
public sealed record UninstallPlan(
    string Root,
    ComponentKind Component,
    DotnetVersion Version,
    InstallationRecord? Record,
    IReadOnlyList<string> Remove,
    IReadOnlyList<KeptPath> Keep)
{
    public bool External => Record is null;
}

/// <summary>Folha mantida e a instalação que passa a ser dona dela.</summary>
public sealed record KeptPath(string Path, string UsedBy);

/// <summary>
/// Remove instalações registradas no manifesto: apaga só folhas criadas pelo DotnetScope que nenhuma outra instalação
/// registrada contém, transfere a posse das folhas compartilhadas e desfaz tudo se a atualização do manifesto falhar.
/// </summary>
public sealed class ArchiveUninstaller(InstallationManifestStore manifest, IDiagnosticSink diagnostics)
{
    internal TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public async Task<UninstallPlan> UninstallAsync(
        string root,
        ComponentKind component,
        DotnetVersion version,
        bool force,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            throw NotInstalled(component, version, root);
        }

        using var installLock = await InstallLock.AcquireAsync(
            Path.Combine(root, ArchiveInstaller.LockFileName), LockTimeout, diagnostics, cancellationToken).ConfigureAwait(false);
        var plan = Plan(manifest.Read(), root, component, version, force);
        if (dryRun)
        {
            return plan;
        }

        var staging = Path.Combine(root, ArchiveInstaller.StagingPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var trash = Path.Combine(staging, "trash");
        var moved = new List<string>();

        // O journal lista o plano completo antes do primeiro movimento: o doctor restaura tudo o que estiver na lixeira.
        WriteJournal(staging, root, plan.Remove, component, version);
        try
        {
            foreach (var leaf in plan.Remove)
            {
                var source = DotnetLayout.Resolve(root, leaf);
                var target = Path.Combine(trash, leaf);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (Directory.Exists(source))
                {
                    Directory.Move(source, target);
                }
                else
                {
                    File.Move(source, target);
                }

                moved.Add(leaf);
            }

            if (plan.Record is not null)
            {
                await manifest.UpdateAsync(records => Transfer(records, plan), diagnostics, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            Restore(root, trash, moved);
            TryDelete(staging);
            throw;
        }

        TryDelete(staging);
        RemoveEmptyContainers(root, plan.Remove);
        return plan;
    }

    /// <summary>Calcula o plano a partir do manifesto; exposto para testes e para o doctor.</summary>
    internal static UninstallPlan Plan(
        IReadOnlyList<InstallationRecord> records,
        string root,
        ComponentKind component,
        DotnetVersion version,
        bool force)
    {
        var primary = DotnetLayout.PrimaryPath(component, version);
        var sameRoot = records.Where(r => string.Equals(r.Root, root, StringComparison.Ordinal)).ToList();
        var record = sameRoot.FirstOrDefault(r => r.Component == component && r.Version == version.ToString());
        if (record is null)
        {
            if (!LayoutMerger.EntryExists(Path.Combine(root, primary)))
            {
                throw NotInstalled(component, version, root);
            }

            // Nem com --force: a pasta pertence a uma instalação gerenciada (por exemplo, o runtime trazido por um SDK).
            if (sameRoot.FirstOrDefault(r => r.Paths.Contains(primary, StringComparer.Ordinal)) is { } owner)
            {
                throw new DotnetScopeException(
                    ExitCodes.Conflict,
                    $"{component.ToName()} {version} faz parte de {owner.Component.ToName()} {owner.Version} em {root}; remova essa instalação.");
            }

            return force
                ? new UninstallPlan(root, component, version, null, [primary], [])
                : throw new DotnetScopeException(
                    ExitCodes.Conflict,
                    $"{component.ToName()} {version} em {root} não foi instalado pelo dotnetscope; use --force para remover somente {primary}.");
        }

        var others = sameRoot.Where(r => !ReferenceEquals(r, record)).ToList();
        var owned = sameRoot.SelectMany(r => r.AddedPaths).ToHashSet(StringComparer.Ordinal);
        var remove = new List<string>();
        var keep = new List<KeptPath>();
        foreach (var leaf in record.Paths)
        {
            var user = others.FirstOrDefault(r => r.Paths.Contains(leaf, StringComparer.Ordinal));
            if (user is not null)
            {
                if (record.AddedPaths.Contains(leaf, StringComparer.Ordinal))
                {
                    keep.Add(new KeptPath(leaf, $"{user.Component.ToName()} {user.Version}"));
                }
            }
            else if (owned.Contains(leaf) && LayoutMerger.EntryExists(Path.Combine(root, leaf)))
            {
                remove.Add(leaf);
            }
        }

        return new UninstallPlan(root, component, version, record, remove, keep);
    }

    /// <summary>Remove o registro e transfere a posse das folhas mantidas para quem ainda as usa.</summary>
    private static List<InstallationRecord> Transfer(IReadOnlyList<InstallationRecord> records, UninstallPlan plan)
    {
        var result = new List<InstallationRecord>();
        foreach (var record in records)
        {
            if (string.Equals(record.Root, plan.Root, StringComparison.Ordinal)
                && record.Component == plan.Component
                && record.Version == plan.Version.ToString())
            {
                continue;
            }

            var inherited = plan.Keep
                .Where(k => string.Equals(record.Root, plan.Root, StringComparison.Ordinal)
                    && record.Paths.Contains(k.Path, StringComparer.Ordinal)
                    && !record.AddedPaths.Contains(k.Path, StringComparer.Ordinal)
                    && k.UsedBy == $"{record.Component.ToName()} {record.Version}")
                .Select(k => k.Path)
                .ToList();
            result.Add(inherited.Count == 0 ? record : record with { AddedPaths = [.. record.AddedPaths, .. inherited] });
        }

        return result;
    }

    internal static void Restore(string root, string trash, IEnumerable<string> moved)
    {
        foreach (var leaf in moved.Reverse())
        {
            var source = Path.Combine(trash, leaf);
            var target = DotnetLayout.Resolve(root, leaf);
            if (LayoutMerger.EntryExists(target))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (Directory.Exists(source))
            {
                Directory.Move(source, target);
            }
            else if (File.Exists(source))
            {
                File.Move(source, target);
            }
        }
    }

    /// <summary>Apaga diretórios contêineres (por exemplo, shared/Microsoft.NETCore.App) que ficaram vazios.</summary>
    internal static void RemoveEmptyContainers(string root, IEnumerable<string> leaves)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        foreach (var leaf in leaves)
        {
            for (var directory = Path.GetDirectoryName(Path.Combine(fullRoot, leaf));
                 directory is not null && directory.Length > fullRoot.Length && directory.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
                 directory = Path.GetDirectoryName(directory))
            {
                if (!Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    break;
                }

                Directory.Delete(directory);
            }
        }
    }

    private static void WriteJournal(string staging, string root, IReadOnlyList<string> planned, ComponentKind component, DotnetVersion version)
    {
        var path = Path.Combine(staging, "journal.json");
        var journal = new MergeJournal("uninstall", root, planned.ToList(), null, component.ToName(), version.ToString());
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(journal, InstallationJsonContext.Default.MergeJournal));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    private void TryDelete(string staging)
    {
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Warning($"não foi possível remover o diretório temporário {staging}: {exception.Message}");
        }
    }

    private static DotnetScopeException NotInstalled(ComponentKind component, DotnetVersion version, string root) =>
        new(ExitCodes.NotFound, $"{component.ToName()} {version} não está instalado em {root}.");
}
