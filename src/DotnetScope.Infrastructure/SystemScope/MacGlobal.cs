using System.Runtime.Versioning;
using System.Security.Cryptography;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Resolution;
using DotnetScope.Infrastructure.Http;
using DotnetScope.Infrastructure.Installation;

namespace DotnetScope.Infrastructure.SystemScope;

/// <summary>
/// Interpreta <c>pkgutil --check-signature</c> (D-F7-1, R02): o pacote precisa estar assinado com Developer ID e o
/// primeiro certificado da cadeia precisa ser exatamente o instalador da Microsoft (Team ID <c>UBF8T346G9</c>).
/// </summary>
public static class PkgSignature
{
    public const string MicrosoftInstaller = "1. Developer ID Installer: Microsoft Corporation (UBF8T346G9)";

    private const string DeveloperIdStatus = "Status: signed by a developer certificate issued by Apple for distribution";

    public static bool IsMicrosoft(int exitCode, string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (exitCode != 0)
        {
            return false;
        }

        var lines = output.Split('\n').Select(line => line.Trim()).ToList();
        var firstCertificate = lines.FirstOrDefault(line => line.StartsWith("1. ", StringComparison.Ordinal));
        return lines.Contains(DeveloperIdStatus, StringComparer.Ordinal)
            && string.Equals(firstCertificate, MicrosoftInstaller, StringComparison.Ordinal);
    }
}

/// <summary>
/// Executa o instalador oficial <c>.pkg</c> no escopo global do macOS (R01 a R03, R16): download na allowlist com
/// SHA-512 para um staging do root com modo 0700 dentro do estado protegido, nova conferência do hash, verificação
/// da assinatura com <c>pkgutil</c> e <c>installer -pkg ... -target /</c> com ambiente fixo.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacPkgInstaller : IArtifactInstaller
{
    private const string Pkgutil = "/usr/sbin/pkgutil";
    private const string Installer = "/usr/sbin/installer";
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromMinutes(30);

    private readonly TrustedHttpFetcher fetcher;
    private readonly InstallerGlobalLayout layout;
    private readonly ISystemCommandRunner runner;
    private readonly InstallationManifestStore manifest;
    private readonly IDiagnosticSink diagnostics;
    private readonly IProgress<DownloadProgress>? progress;

    internal MacPkgInstaller(
        TrustedHttpFetcher fetcher,
        InstallerGlobalLayout layout,
        ISystemCommandRunner runner,
        InstallationManifestStore manifest,
        IDiagnosticSink diagnostics,
        IProgress<DownloadProgress>? progress)
    {
        this.fetcher = fetcher;
        this.layout = layout;
        this.runner = runner;
        this.manifest = manifest;
        this.diagnostics = diagnostics;
        this.progress = progress;
    }

    public static MacPkgInstaller CreateOfficial(
        InstallerGlobalLayout layout, ISystemCommandRunner runner, IDiagnosticSink diagnostics, IProgress<DownloadProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new(TrustedHttpFetcher.CreateOfficial(diagnostics), layout, runner, new InstallationManifestStore(layout.ManifestFile), diagnostics, progress);
    }

    public async Task<InstallResult> InstallAsync(ResolvedArtifact artifact, string root, InstallScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (InstallLocations.IsInstalled(root, artifact.Component, artifact.Version))
        {
            var existing = await DotnetHostValidator.ValidateAsync(root, artifact, cancellationToken).ConfigureAwait(false);
            return new InstallResult(InstallStatus.AlreadyInstalled, artifact, root, [], [], false, existing);
        }

        GlobalLocations.CreateDirectory(layout.StagingDirectory);
        var staging = Path.Combine(layout.StagingDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var package = Path.Combine(staging, Path.GetFileName(artifact.File.Name));
            diagnostics.Verbose($"baixando {artifact.File.Url} para {package}");
            await fetcher.DownloadToFileAsync(artifact.File.Url, package, artifact.Sha512, progress, cancellationToken).ConfigureAwait(false);
            VerifySha512(package, artifact.Sha512, artifact.File.Name);

            var signature = await runner.RunAsync(Pkgutil, ["--check-signature", package], TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            if (!PkgSignature.IsMicrosoft(signature.ExitCode, signature.Output))
            {
                throw new DotnetScopeException(
                    ExitCodes.Integrity,
                    $"{artifact.File.Name} não está assinado com \"{PkgSignature.MicrosoftInstaller[3..]}\"; nada foi executado.");
            }

            diagnostics.Verbose($"SHA-512 e assinatura do pacote verificados: {artifact.File.Name}");

            // Interromper o installer no meio deixaria a máquina fora do controle do dotnetscope: o Ctrl+C só é informado.
            using var notice = cancellationToken.Register(
                () => diagnostics.Warning($"aguardando {artifact.File.Name} terminar; interrompê-lo deixaria a instalação inconsistente."));
            var result = await runner.RunAsync(Installer, ["-pkg", package, "-target", "/"], SetupTimeout, CancellationToken.None).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                var detail = result.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
                throw new DotnetScopeException(ExitCodes.UnexpectedError, $"installer -pkg {artifact.File.Name} terminou com código {result.ExitCode}: {detail}");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Warning($"não foi possível remover o staging {staging}: {exception.Message}");
            }
        }

        await manifest.AddAsync(
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
                [],
                [],
                false),
            diagnostics,
            CancellationToken.None).ConfigureAwait(false);
        var validation = await DotnetHostValidator.ValidateAsync(root, artifact, CancellationToken.None).ConfigureAwait(false);
        return new InstallResult(InstallStatus.Installed, artifact, root, [], [], false, validation);
    }

    public void Dispose() => fetcher.Dispose();

    private static void VerifySha512(string path, string expected, string fileName)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexStringLower(SHA512.HashData(stream));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new DotnetScopeException(ExitCodes.Integrity, $"SHA-512 de {fileName} mudou após o download; nada foi executado.");
        }
    }
}
