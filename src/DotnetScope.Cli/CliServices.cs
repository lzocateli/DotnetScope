using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.Metadata;
using DotnetScope.Infrastructure.Platform;
using DotnetScope.Infrastructure.SystemScope;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.Cli;

/// <summary>Dependências da CLI, substituíveis em testes.</summary>
internal sealed record CliServices(
    TextWriter Output,
    TextWriter Error,
    Func<MetadataOptions, IDiagnosticSink, IReleaseMetadataSource> CreateMetadataSource,
    Func<string> DetectRid)
{
    public Func<IDiagnosticSink, IProgress<DownloadProgress>?, IArtifactInstaller> CreateInstaller { get; init; } =
        ArchiveInstaller.CreateOfficial;

    public Func<bool> IsRoot { get; init; } = UnixUser.IsRoot;

    public Func<string, string?> GetEnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;

    public Action<string> UnsetEnvironmentVariable { get; init; } = UnixUser.UnsetEnvironmentVariable;

    public Func<string> HomeDirectory { get; init; } = () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public Func<IReadOnlyList<string>> FindMissingDependencies { get; init; } = NativeDependencyChecker.FindMissing;

    public IUserPrompt Prompt { get; init; } = new ConsolePrompt();

    public Func<string, EnvironmentLayout> CreateEnvironmentLayout { get; init; } = EnvironmentLayout.CreateDefault;

    /// <summary>Configurador do registro do Windows (substituído por um store em memória nos testes).</summary>
    public Func<string, WindowsEnvironmentConfigurator> CreateWindowsEnvironment { get; init; } = WindowsEnvironmentConfigurator.CreateDefault;

    /// <summary>Configurador dos perfis do PowerShell no Windows, sem registro (substituído nos testes).</summary>
    public Func<string, WindowsProfileEnvironmentConfigurator> CreateWindowsProfileEnvironment { get; init; } =
        WindowsProfileEnvironmentConfigurator.CreateDefault;

    public Func<InstallationManifestStore> CreateManifestStore { get; init; } = InstallationManifestStore.CreateDefault;

    public Func<IReadOnlyList<string>> SystemRoots { get; init; } = InstalledInventory.SystemRoots;

    public Func<string, bool> IsPackageManaged { get; init; } = InstalledInventory.IsPackageManaged;

    public Func<string> CurrentDirectory { get; init; } = Directory.GetCurrentDirectory;

    public Func<string?> LoginShell { get; init; } = LoginShellProbe.DefaultShell;

    public Func<string?, string, CancellationToken, Task<LoginShellResult?>> ProbeLoginShell { get; init; } = LoginShellProbe.ProbeAsync;

    /// <summary>Caminhos fixos do escopo global para o RID (substituídos por diretórios temporários nos testes).</summary>
    public Func<string, GlobalLayout> CreateGlobalLayout { get; init; } = GlobalLayout.CreateDefault;

    /// <summary>Dono e permissões de um caminho, usados para validar diretórios do escopo global.</summary>
    public Func<string, UnixFileStatus?> StatPath { get; init; } = UnixFileOwnership.Stat;

    public Func<GlobalLayout, IDiagnosticSink, IProgress<DownloadProgress>?, IArtifactInstaller> CreateGlobalInstaller { get; init; } =
        (layout, diagnostics, progress) => ArchiveInstaller.CreateOfficial(new InstallationManifestStore(layout.ManifestFile), diagnostics, progress);

    public SystemEnvironmentConfigurator CreateSystemEnvironment(GlobalLayout layout) => new(layout, StatPath);

    /// <summary>Processo elevado: root no Linux e macOS, token elevado (UAC) no Windows.</summary>
    public Func<bool> IsElevated { get; init; } = () => OperatingSystem.IsWindows() ? WindowsSecurity.IsElevated() : UnixUser.IsRoot();

    /// <summary>Raiz e estado do escopo global pelo instalador oficial (Windows e macOS) para o RID.</summary>
    public Func<string, InstallerGlobalLayout> CreateInstallerLayout { get; init; } = InstallerGlobalLayout.CreateDefault;

    /// <summary>
    /// Cria ou valida o estado global protegido: ACL só de Administradores e SYSTEM no Windows; dono root e sem escrita
    /// de grupo ou outros, inclusive nos ancestrais, no macOS.
    /// </summary>
    public Action<string> EnsureProtectedDirectory { get; init; } = path =>
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsSecurity.EnsureProtectedDirectory(path);
            return;
        }

        var parent = Path.GetDirectoryName(path);
        if (parent is not null)
        {
            GlobalLocations.EnsureTrusted(parent, UnixFileOwnership.Stat, "o estado do dotnetscope");
        }

        GlobalLocations.CreateDirectory(path);
        GlobalLocations.EnsureTrusted(path, UnixFileOwnership.Stat, "o estado do dotnetscope");
    };

    /// <summary>Instalador oficial verificado: <c>.exe</c> no Windows, <c>.pkg</c> no macOS.</summary>
    public Func<InstallerGlobalLayout, IDiagnosticSink, IProgress<DownloadProgress>?, IArtifactInstaller> CreateOfficialInstaller { get; init; } =
        (layout, diagnostics, progress) => OperatingSystem.IsWindows() ? WindowsSetupInstaller.CreateOfficial(layout, diagnostics, progress)
            : OperatingSystem.IsMacOS() ? MacPkgInstaller.CreateOfficial(layout, new SystemCommandRunner(), diagnostics, progress)
            : throw new PlatformNotSupportedException("O instalador oficial só executa no Windows e no macOS.");

    public Func<InstallerGlobalLayout, InstallationRecord, IDiagnosticSink, CancellationToken, Task> UninstallWindowsSetup { get; init; } =
        async (layout, record, diagnostics, cancellationToken) =>
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("O instalador oficial só executa no Windows.");
            }

            using var installer = WindowsSetupInstaller.CreateOfficial(layout, diagnostics, null);
            await installer.UninstallAsync(record, cancellationToken).ConfigureAwait(false);
        };

    /// <summary>Ferramentas do sistema (apt-get, apt-cache, dnf, apk), substituídas nos testes.</summary>
    public ISystemCommandRunner CommandRunner { get; init; } = new SystemCommandRunner();

    public IArtifactInstaller CreateAptInstaller(GlobalLayout layout, AptCandidate candidate, IDiagnosticSink diagnostics) =>
        new AptInstaller(CommandRunner, candidate, new InstallationManifestStore(layout.ManifestFile), diagnostics);

    /// <summary>Indica se o RID alvo é Windows (registro, zip e <c>dotnet.exe</c>).</summary>
    public bool IsWindowsTarget => DetectRid().StartsWith("win-", StringComparison.Ordinal);

    public IUserEnvironmentConfigurator CreateEnvironmentConfigurator(
        string home, WindowsEnvironmentTarget windowsTarget = WindowsEnvironmentTarget.Registry) =>
        !IsWindowsTarget ? new UnixEnvironmentConfigurator(CreateEnvironmentLayout(home))
            : windowsTarget == WindowsEnvironmentTarget.Profile ? CreateWindowsProfileEnvironment(home)
            : CreateWindowsEnvironment(home);

    /// <summary>Todos os destinos que podem guardar o ambiente do usuário (remoção, desinstalação e diagnóstico).</summary>
    public IReadOnlyList<IUserEnvironmentConfigurator> CreateEnvironmentConfigurators(string home) =>
        IsWindowsTarget
            ? [CreateWindowsEnvironment(home), CreateWindowsProfileEnvironment(home)]
            : [new UnixEnvironmentConfigurator(CreateEnvironmentLayout(home))];

    public static CliServices CreateDefault(TextWriter output, TextWriter error) =>
        new(output, error, ReleaseMetadataClient.CreateOfficial, PlatformDetector.DetectRid);
}

/// <summary>Interação com o usuário; em ambiente não interativo nenhuma pergunta é feita.</summary>
internal interface IUserPrompt
{
    bool IsInteractive { get; }

    bool Confirm(string question);
}

internal sealed class ConsolePrompt : IUserPrompt
{
    public bool IsInteractive => !Console.IsInputRedirected && !Console.IsErrorRedirected;

    public bool Confirm(string question)
    {
        Console.Error.Write($"{question} [s/N] ");
        var answer = Console.ReadLine()?.Trim().ToUpperInvariant();
        return answer is "S" or "SIM" or "Y" or "YES";
    }
}

internal sealed class ConsoleDiagnostics(TextWriter error, bool verbose) : IDiagnosticSink
{
    public void Warning(string message) => error.WriteLine($"aviso: {message}");

    public void Verbose(string message)
    {
        if (verbose)
        {
            error.WriteLine($"detalhe: {message}");
        }
    }
}

/// <summary>Progresso de download em stderr, somente em terminal interativo.</summary>
internal sealed class ConsoleDownloadProgress(TextWriter error) : IProgress<DownloadProgress>
{
    private int _lastPercent = -1;
    private long _lastMebibytes = -1;

    public void Report(DownloadProgress value)
    {
        var mebibytes = value.Received / (1024 * 1024);
        if (value.Total is > 0)
        {
            var percent = (int)(value.Received * 100 / value.Total.Value);
            if (percent == _lastPercent)
            {
                return;
            }

            _lastPercent = percent;
            error.Write($"\rBaixando {value.FileName}: {percent,3}% ({mebibytes}/{value.Total.Value / (1024 * 1024)} MiB)");
            if (percent == 100)
            {
                error.WriteLine();
            }
        }
        else if (mebibytes != _lastMebibytes)
        {
            _lastMebibytes = mebibytes;
            error.Write($"\rBaixando {value.FileName}: {mebibytes} MiB");
        }
    }
}
