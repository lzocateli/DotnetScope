using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Resolution;
using DotnetScope.Infrastructure.Http;

namespace DotnetScope.Infrastructure.Installation;

/// <summary>
/// Instalação por arquivo: lock da raiz, download verificado em staging privado, extração segura, mesclagem
/// lado a lado com rollback, registro no manifesto e validação do host.
/// </summary>
public sealed class ArchiveInstaller : IArtifactInstaller
{
    internal const string LockFileName = ".dotnetscope.lock";
    internal const string StagingPrefix = ".dotnetscope-staging-";

    private readonly TrustedHttpFetcher _fetcher;
    private readonly InstallationManifestStore _manifest;
    private readonly IDiagnosticSink _diagnostics;
    private readonly IProgress<DownloadProgress>? _progress;
    private readonly SafeTarExtractor _extractor;

    internal ArchiveInstaller(
        TrustedHttpFetcher fetcher,
        InstallationManifestStore manifest,
        IDiagnosticSink diagnostics,
        IProgress<DownloadProgress>? progress,
        SafeTarExtractor? extractor = null)
    {
        _fetcher = fetcher;
        _manifest = manifest;
        _diagnostics = diagnostics;
        _progress = progress;
        _extractor = extractor ?? new SafeTarExtractor();
    }

    public static ArchiveInstaller CreateOfficial(IDiagnosticSink diagnostics, IProgress<DownloadProgress>? progress) =>
        new(TrustedHttpFetcher.CreateOfficial(diagnostics), InstallationManifestStore.CreateDefault(), diagnostics, progress);

    internal TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Teste: permite injetar falhas na mesclagem.</summary>
    internal Action<LayoutMerger>? ConfigureMerger { get; set; }

    public async Task<InstallResult> InstallAsync(
        ResolvedArtifact artifact,
        string root,
        InstallScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (!artifact.File.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            throw new DotnetScopeException(
                ExitCodes.NotFound,
                $"A instalação de {artifact.File.Name} ainda não é suportada nesta plataforma.");
        }

        Directory.CreateDirectory(root);
        using var installLock = await InstallLock.AcquireAsync(
            Path.Combine(root, LockFileName),
            LockTimeout,
            _diagnostics,
            cancellationToken).ConfigureAwait(false);

        var primary = DotnetLayout.PrimaryPath(artifact.Component, artifact.Version);
        if (LayoutMerger.EntryExists(Path.Combine(root, primary)))
        {
            var existing = await DotnetHostValidator.ValidateAsync(root, artifact, cancellationToken).ConfigureAwait(false);
            return new InstallResult(InstallStatus.AlreadyInstalled, artifact, root, [], [primary], false, existing);
        }

        var staging = Path.Combine(root, StagingPrefix + Guid.NewGuid().ToString("N"));
        CreatePrivateDirectory(staging);
        MergeResult merge;
        try
        {
            var archive = Path.Combine(staging, artifact.File.Name);
            _diagnostics.Verbose($"baixando {artifact.File.Url} para {archive}");
            await _fetcher.DownloadToFileAsync(artifact.File.Url, archive, artifact.Sha512, _progress, cancellationToken)
                .ConfigureAwait(false);
            _diagnostics.Verbose("SHA-512 verificado");

            var extracted = Path.Combine(staging, "extract");
            await _extractor.ExtractAsync(archive, extracted, cancellationToken).ConfigureAwait(false);
            File.Delete(archive);
            if (!LayoutMerger.EntryExists(Path.Combine(extracted, primary)))
            {
                throw new DotnetScopeException(
                    ExitCodes.Integrity,
                    $"{artifact.File.Name} não contém {primary}; a instalação foi cancelada.");
            }

            var merger = new LayoutMerger(extracted, root, staging, OperatingSystem.IsWindows());
            ConfigureMerger?.Invoke(merger);
            // A partir daqui a operação não é cancelável: os movimentos levam milissegundos e interromper
            // um rollback recursivo deixaria a raiz inconsistente.
            merge = merger.Merge(CancellationToken.None);
            try
            {
                await _manifest.AddAsync(
                    new InstallationRecord(
                        artifact.Component,
                        artifact.Version.ToString(),
                        artifact.Rid,
                        scope,
                        root,
                        artifact.File.Name,
                        artifact.File.Url,
                        artifact.Sha512,
                        DateTimeOffset.UtcNow,
                        merge.Leaves,
                        merge.Added,
                        merge.HostReplaced),
                    _diagnostics,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                merger.Rollback();
                throw;
            }
        }
        finally
        {
            TryDelete(staging);
        }

        var validation = await DotnetHostValidator.ValidateAsync(root, artifact, CancellationToken.None).ConfigureAwait(false);
        return new InstallResult(InstallStatus.Installed, artifact, root, merge.Added, merge.Shared, merge.HostReplaced, validation);
    }

    public void Dispose() => _fetcher.Dispose();

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
            _diagnostics.Warning($"não foi possível remover o diretório temporário {staging}: {exception.Message}");
        }
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
