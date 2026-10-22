using DotnetScope.Core.Releases;
using DotnetScope.Core.Resolution;

namespace DotnetScope.Core.Installation;

public enum InstallScope
{
    User,
    Global,
}

public enum InstallStatus
{
    Installed,
    AlreadyInstalled,
}

/// <summary>
/// Instalação gerenciada pelo DotnetScope. <see cref="Paths"/> são as folhas contidas no arquivo; <see cref="AddedPaths"/>
/// são as que não existiam e foram criadas por esta instalação. A remoção usa ambas para não apagar folhas ainda
/// usadas por outra instalação.
/// </summary>
public sealed record InstallationRecord(
    ComponentKind Component,
    string Version,
    string Rid,
    InstallScope Scope,
    string Root,
    string FileName,
    Uri Url,
    string Sha512,
    DateTimeOffset InstalledAt,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> AddedPaths,
    bool HostReplaced,
    string? DeclaredInvoker = null);

/// <summary>Resultado da validação do host após a instalação.</summary>
public sealed record HostValidation(bool Succeeded, string Detail);

public sealed record InstallResult(
    InstallStatus Status,
    ResolvedArtifact Artifact,
    string Root,
    IReadOnlyList<string> AddedPaths,
    IReadOnlyList<string> SharedPaths,
    bool HostReplaced,
    HostValidation Validation);

/// <summary>Instala um artefato resolvido em uma raiz .NET.</summary>
public interface IArtifactInstaller : IDisposable
{
    Task<InstallResult> InstallAsync(ResolvedArtifact artifact, string root, InstallScope scope, CancellationToken cancellationToken);
}

/// <summary>Progresso de download em bytes.</summary>
public readonly record struct DownloadProgress(string FileName, long Received, long? Total);
