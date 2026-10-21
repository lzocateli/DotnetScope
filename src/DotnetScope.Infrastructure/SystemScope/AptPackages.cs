using System.Text.RegularExpressions;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Resolution;
using DotnetScope.Core.Versions;
using DotnetScope.Infrastructure.Installation;

namespace DotnetScope.Infrastructure.SystemScope;

/// <summary>Pacote .NET disponível nas fontes apt já configuradas na máquina.</summary>
/// <param name="Package">Nome do pacote, por exemplo <c>dotnet-sdk-10.0</c>.</param>
/// <param name="PackageVersion">Versão Debian completa, por exemplo <c>10.0.13-0ubuntu1~24.04.1</c>.</param>
/// <param name="Version">Versão .NET correspondente (versão upstream).</param>
public sealed record AptCandidate(string Package, string PackageVersion, DotnetVersion Version);

/// <summary>
/// Estratégia apt do escopo global (modelo de ameaças, R10 a R12): usa somente as fontes já configuradas, sem adicionar
/// repositório nem chave; nomes e versões de pacote são validados por expressão regular antes de qualquer uso.
/// </summary>
public static partial class AptPackages
{
    public const string AptGet = "/usr/bin/apt-get";
    public const string AptCache = "/usr/bin/apt-cache";
    public const string DpkgQuery = "/usr/bin/dpkg-query";
    public const string Scheme = "apt";

    /// <summary>Raízes dos pacotes .NET: Ubuntu (<c>/usr/lib/dotnet</c>) e packages.microsoft.com (<c>/usr/share/dotnet</c>).</summary>
    public static readonly IReadOnlyList<string> PackageRoots = ["/usr/lib/dotnet", "/usr/share/dotnet"];

    private const string SystemDotnet = "/usr/bin/dotnet";

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(20);

    /// <summary>Nome do pacote do componente para o canal; nulo para componentes sem pacote (windowsdesktop).</summary>
    public static string? PackageName(ComponentKind component, ChannelVersion channel) => component switch
    {
        ComponentKind.Sdk => $"dotnet-sdk-{channel}",
        ComponentKind.Runtime => $"dotnet-runtime-{channel}",
        ComponentKind.AspNetCore => $"aspnetcore-runtime-{channel}",
        _ => null,
    };

    public static bool IsAvailable(ISystemCommandRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        return runner.Exists(AptGet) && runner.Exists(AptCache);
    }

    /// <summary>Candidato .NET do <c>apt-cache policy</c>, ou nulo se o pacote não existe nas fontes configuradas.</summary>
    public static async Task<AptCandidate?> FindCandidateAsync(ISystemCommandRunner runner, string package, CancellationToken cancellationToken)
    {
        var candidate = await FindPackageVersionAsync(runner, package, cancellationToken).ConfigureAwait(false);
        return candidate is not null && DotnetVersion.TryParse(UpstreamVersion(candidate), out var version)
            ? new AptCandidate(package, candidate, version)
            : null;
    }

    /// <summary>Versão candidata de qualquer pacote nas fontes configuradas, ou nula; validada antes de ser usada.</summary>
    public static async Task<string?> FindPackageVersionAsync(ISystemCommandRunner runner, string package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);
        EnsurePackageName(package);
        var result = await runner.RunAsync(AptCache, ["policy", "--", package], QueryTimeout, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var candidate = result.Output.Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("Candidate:", StringComparison.Ordinal))?["Candidate:".Length..].Trim();
        return candidate is null or "" or "(none)" || !PackageVersionPattern().IsMatch(candidate) ? null : candidate;
    }

    /// <summary>
    /// Escolhe o candidato apt para o pedido. Com <c>--source auto</c>, somente quando ele é exatamente a versão que o
    /// metadata resolveu (assim o resultado nunca difere do da estratégia archive). Com <c>--source apt</c>, aceita o
    /// candidato do canal, respeitando versão exata e feature band quando pedidas.
    /// </summary>
    public static bool Accepts(AptCandidate candidate, VersionSpec request, DotnetVersion resolved, bool explicitApt)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resolved);
        if (!explicitApt)
        {
            return candidate.Version == resolved;
        }

        return request switch
        {
            ExactVersionSpec exact => candidate.Version == exact.Version,
            FeatureBandSpec band => candidate.Version.Channel == band.Channel && candidate.Version.FeatureBand == band.Band,
            _ => candidate.Version.Channel == resolved.Channel,
        };
    }

    public static async Task InstallAsync(ISystemCommandRunner runner, AptCandidate candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(candidate);
        EnsurePackageName(candidate.Package);
        EnsurePackageVersion(candidate.PackageVersion);
        var result = await runner.RunAsync(
            AptGet,
            ["install", "-y", "--no-install-recommends", "--", $"{candidate.Package}={candidate.PackageVersion}"],
            InstallTimeout,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new DotnetScopeException(
                ExitCodes.UnexpectedError,
                $"apt-get install {candidate.Package} terminou com código {result.ExitCode}: {LastLine(result.Error)}");
        }
    }

    /// <summary>
    /// Remove somente o pacote informado. A remoção é simulada antes (<c>apt-get -s</c>): se o apt também removeria
    /// pacotes que dependem dele (por exemplo, o SDK ao remover o runtime), nada é feito e retorna código 6.
    /// </summary>
    public static async Task RemoveAsync(ISystemCommandRunner runner, string package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);
        EnsurePackageName(package);
        var simulation = await runner.RunAsync(AptGet, ["-s", "remove", "--", package], QueryTimeout, cancellationToken).ConfigureAwait(false);
        if (simulation.ExitCode != 0)
        {
            throw new DotnetScopeException(ExitCodes.UnexpectedError, $"apt-get -s remove {package} terminou com código {simulation.ExitCode}: {LastLine(simulation.Error)}");
        }

        var others = simulation.Output.Split('\n')
            .Where(line => line.StartsWith("Remv ", StringComparison.Ordinal))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1])
            .Where(name => name != package && !name.StartsWith(package + ":", StringComparison.Ordinal))
            .ToList();
        if (others.Count > 0)
        {
            throw new DotnetScopeException(
                ExitCodes.Conflict,
                $"Remover {package} também removeria {string.Join(", ", others)}, que dependem dele; nada foi removido. Remova esses componentes antes.");
        }

        var result = await runner.RunAsync(AptGet, ["remove", "-y", "--", package], InstallTimeout, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new DotnetScopeException(ExitCodes.UnexpectedError, $"apt-get remove {package} terminou com código {result.ExitCode}: {LastLine(result.Error)}");
        }
    }

    /// <summary>Versão Debian instalada do pacote, ou nula se ele não está instalado.</summary>
    public static async Task<string?> InstalledVersionAsync(ISystemCommandRunner runner, string package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);
        EnsurePackageName(package);
        if (!runner.Exists(DpkgQuery))
        {
            return null;
        }

        var result = await runner.RunAsync(DpkgQuery, ["-W", "-f=${Status}|${Version}", "--", package], QueryTimeout, cancellationToken).ConfigureAwait(false);
        var parts = result.Output.Trim().Split('|');
        return result.ExitCode == 0 && parts.Length == 2 && parts[0].EndsWith(" installed", StringComparison.Ordinal)
            && PackageVersionPattern().IsMatch(parts[1])
                ? parts[1]
                : null;
    }

    /// <summary>
    /// Raiz do .NET dos pacotes: o diretório real de <c>/usr/bin/dotnet</c> quando ele existe; senão, a raiz provável pela
    /// origem do candidato (Ubuntu ou packages.microsoft.com).
    /// </summary>
    public static string ResolveRoot(AptCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (File.Exists(SystemDotnet))
        {
            var target = new FileInfo(SystemDotnet).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? SystemDotnet;
            var directory = Path.GetDirectoryName(target);
            if (directory is not null && PackageRoots.Contains(directory, StringComparer.Ordinal))
            {
                return directory;
            }
        }

        return candidate.PackageVersion.Contains("ubuntu", StringComparison.Ordinal) ? PackageRoots[0] : PackageRoots[1];
    }

    /// <summary>Versão upstream de uma versão Debian: sem época (<c>N:</c>) e sem revisão (<c>-...</c>).</summary>
    public static string UpstreamVersion(string packageVersion)
    {
        ArgumentNullException.ThrowIfNull(packageVersion);
        var value = packageVersion;
        var colon = value.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            value = value[(colon + 1)..];
        }

        var dash = value.LastIndexOf('-');
        return dash > 0 ? value[..dash] : value;
    }

    /// <summary>Artefato sintético que representa o pacote, registrado no manifesto com a URL <c>apt:&lt;pacote&gt;</c>.</summary>
    public static ResolvedArtifact ToArtifact(AptCandidate candidate, ComponentKind component, string rid) =>
        new(
            component,
            candidate.Version,
            null,
            candidate.Version.ToString(),
            rid,
            new ReleaseFile($"{candidate.Package}={candidate.PackageVersion}", rid, new Uri($"{Scheme}:{candidate.Package}"), string.Empty, IsTrusted: true));

    /// <summary>Pacote de um registro do manifesto instalado pela estratégia apt, ou nulo.</summary>
    public static string? PackageOf(InstallationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.Url.Scheme == Scheme && PackageNamePattern().IsMatch(record.Url.AbsolutePath) ? record.Url.AbsolutePath : null;
    }

    internal static void EnsurePackageName(string package)
    {
        if (!PackageNamePattern().IsMatch(package))
        {
            throw new DotnetScopeException(ExitCodes.Usage, $"Nome de pacote inválido: {package}.");
        }
    }

    internal static void EnsurePackageVersion(string version)
    {
        if (!PackageVersionPattern().IsMatch(version))
        {
            throw new DotnetScopeException(ExitCodes.Usage, $"Versão de pacote inválida: {version}.");
        }
    }

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;

    // Debian: minúsculas, dígitos e + - . (sem começar com '-', o que viraria opção).
    [GeneratedRegex("^[a-z0-9][a-z0-9.+-]{1,62}$")]
    private static partial Regex PackageNamePattern();

    [GeneratedRegex("^([0-9]+:)?[0-9][A-Za-z0-9.+~-]{0,127}$")]
    private static partial Regex PackageVersionPattern();
}

/// <summary>
/// Instala o pacote apt escolhido e o registra no manifesto global; a validação usa o host do pacote. Um pacote já
/// instalado não é tocado nem registrado: ele pertence a quem o instalou (modelo de ameaças, R18).
/// </summary>
public sealed class AptInstaller(ISystemCommandRunner runner, AptCandidate candidate, InstallationManifestStore manifest, IDiagnosticSink diagnostics)
    : IArtifactInstaller
{
    public async Task<InstallResult> InstallAsync(ResolvedArtifact artifact, string root, InstallScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var current = await AptPackages.InstalledVersionAsync(runner, candidate.Package, cancellationToken).ConfigureAwait(false);
        if (current is not null)
        {
            if (current != candidate.PackageVersion)
            {
                throw new DotnetScopeException(
                    ExitCodes.Conflict,
                    $"{candidate.Package} já está instalado na versão {current}; ele não será alterado. Use o apt diretamente ou --source archive.");
            }

            var existingRoot = AptPackages.ResolveRoot(candidate);
            var existing = await DotnetHostValidator.ValidateAsync(existingRoot, artifact, cancellationToken).ConfigureAwait(false);
            return new InstallResult(InstallStatus.AlreadyInstalled, artifact, existingRoot, [], [], false, existing);
        }

        await AptPackages.InstallAsync(runner, candidate, cancellationToken).ConfigureAwait(false);
        var actualRoot = AptPackages.ResolveRoot(candidate);
        await manifest.AddAsync(
            new InstallationRecord(
                artifact.Component,
                artifact.Version.ToString(),
                artifact.Rid,
                scope,
                actualRoot,
                artifact.File.Name,
                artifact.File.Url,
                string.Empty,
                DateTimeOffset.UtcNow,
                [],
                [],
                false),
            diagnostics,
            CancellationToken.None).ConfigureAwait(false);
        var validation = await DotnetHostValidator.ValidateAsync(actualRoot, artifact, CancellationToken.None).ConfigureAwait(false);
        return new InstallResult(InstallStatus.Installed, artifact, actualRoot, [], [], false, validation);
    }

    public void Dispose()
    {
    }
}
