using System.Text.Json;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;

namespace DotnetScope.Infrastructure.Installation;

/// <summary>Manifesto das instalações gerenciadas, com gravação atômica sob lock entre processos.</summary>
public sealed class InstallationManifestStore
{
    private const int SchemaVersion = 1;

    public InstallationManifestStore(string path) => Path = System.IO.Path.GetFullPath(path);

    public string Path { get; }

    public static InstallationManifestStore CreateDefault() =>
        new(System.IO.Path.Combine(DotnetScopePaths.GetDataDirectory(), "manifest.json"));

    public IReadOnlyList<InstallationRecord> Read() => Load().Installations.Select(ToRecord).ToList();

    public Task AddAsync(InstallationRecord record, IDiagnosticSink diagnostics, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(diagnostics);
        var isUnix = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
        var sudoUser = record.DeclaredInvoker ?? (isUnix && record.Scope == InstallScope.Global
            ? Environment.GetEnvironmentVariable("SUDO_USER")
            : null);
        var declaredInvoker = ResolveDeclaredInvoker(record.Scope, isUnix, sudoUser);
        if (isUnix &&
            record.Scope == InstallScope.Global &&
            !string.IsNullOrWhiteSpace(sudoUser) &&
            declaredInvoker is null)
        {
            diagnostics.Warning("SUDO_USER não foi registrado no manifesto por formato inválido.");
        }

        record = record with { DeclaredInvoker = declaredInvoker };
        return UpdateAsync(
            records => records
                .Where(r => !(InstallLocations.SameRoot(r.Root, record.Root) && r.Component == record.Component && r.Version == record.Version))
                .Append(record)
                .ToList(),
            diagnostics,
            cancellationToken);
    }

    /// <summary>Lê, transforma e grava o manifesto atomicamente sob o lock entre processos.</summary>
    public async Task UpdateAsync(
        Func<IReadOnlyList<InstallationRecord>, IReadOnlyList<InstallationRecord>> change,
        IDiagnosticSink diagnostics,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        CreatePrivateDirectory(directory);
        using var _ = await InstallLock.AcquireAsync(
            System.IO.Path.Combine(directory, "manifest.lock"),
            TimeSpan.FromSeconds(30),
            diagnostics,
            cancellationToken).ConfigureAwait(false);

        var current = Load().Installations.Select(ToRecord).ToList();
        var document = new ManifestDocument(SchemaVersion, change(current).Select(ToEntry).ToList());

        var temporary = System.IO.Path.Combine(directory, $".manifest.{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(temporary, options))
            {
                JsonSerializer.Serialize(stream, document, InstallationJsonContext.Default.ManifestDocument);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private ManifestDocument Load()
    {
        if (!File.Exists(Path))
        {
            return new ManifestDocument(SchemaVersion, []);
        }

        try
        {
            var document = JsonSerializer.Deserialize(File.ReadAllBytes(Path), InstallationJsonContext.Default.ManifestDocument);
            if (document is null || document.SchemaVersion != SchemaVersion)
            {
                throw new DotnetScopeException(ExitCodes.UnexpectedError, $"Manifesto com versão de esquema não suportada: {Path}.");
            }

            return document with { Installations = document.Installations ?? [] };
        }
        catch (JsonException exception)
        {
            throw new DotnetScopeException(
                ExitCodes.UnexpectedError,
                $"Manifesto corrompido em {Path}; ele não será sobrescrito. Corrija ou mova o arquivo.",
                exception);
        }
    }

    private static void CreatePrivateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static ManifestEntry ToEntry(InstallationRecord record) => new(
        record.Component.ToName(),
        record.Version,
        record.Rid,
        record.Scope == InstallScope.Global ? "global" : "user",
        record.Root,
        record.FileName,
        record.Url.ToString(),
        record.Sha512,
        record.InstalledAt,
        [.. record.Paths],
        [.. record.AddedPaths],
        record.HostReplaced,
        record.Scope == InstallScope.Global ? record.DeclaredInvoker : null);

    private static InstallationRecord ToRecord(ManifestEntry entry)
    {
        if (!(entry.Paths ?? []).Concat(entry.AddedPaths ?? []).All(DotnetLayout.IsValidLeaf) ||
            !System.IO.Path.IsPathFullyQualified(entry.Root))
        {
            throw new DotnetScopeException(
                ExitCodes.UnexpectedError,
                $"Manifesto com caminho inválido em {entry.Component} {entry.Version}; ele não será usado nem sobrescrito.");
        }

        var declaredInvoker = ValidateDeclaredInvoker(entry);
        return CreateRecord(entry, declaredInvoker);
    }

    private static InstallationRecord CreateRecord(ManifestEntry entry, string? declaredInvoker) => new(
        ComponentKindNames.TryParse(entry.Component, out var component)
            ? component
            : throw new DotnetScopeException(ExitCodes.UnexpectedError, $"Componente desconhecido no manifesto: {entry.Component}."),
        entry.Version,
        entry.Rid,
        entry.Scope == "global" ? InstallScope.Global : InstallScope.User,
        entry.Root,
        entry.FileName,
        new Uri(entry.Url),
        entry.Sha512,
        entry.InstalledAt,
        entry.Paths,
        entry.AddedPaths,
        entry.HostReplaced,
        declaredInvoker);

    internal static string? ResolveDeclaredInvoker(InstallScope scope, bool isUnix, string? value) =>
        scope == InstallScope.Global && isUnix ? NormalizeDeclaredInvoker(value) : null;

    private static string? NormalizeDeclaredInvoker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= 256 && !normalized.Any(char.IsControl) ? normalized : null;
    }

    private static string? ValidateDeclaredInvoker(ManifestEntry entry)
    {
        if (entry.DeclaredInvoker is null)
        {
            return null;
        }

        var normalized = NormalizeDeclaredInvoker(entry.DeclaredInvoker);
        if (entry.Scope != "global" || normalized is null || normalized != entry.DeclaredInvoker)
        {
            throw new DotnetScopeException(
                ExitCodes.UnexpectedError,
                "Manifesto contém um campo de auditoria inválido; ele não será usado nem sobrescrito.");
        }

        return normalized;
    }
}
