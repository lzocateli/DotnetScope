using System.Text.Json;
using DotnetScope.Cli;
using DotnetScope.Cli.Commands;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Resolution;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.UnitTests;

public sealed class InstallCommandTests
{
    private static readonly string Home = OperatingSystem.IsWindows() ? @"C:\Users\teste" : "/home/teste";

    // Raiz absoluta no SO de execução para os testes do fluxo Windows (o RID é simulado).
    private static readonly string WindowsRoot = OperatingSystem.IsWindows() ? @"C:\Users\teste\dotnet" : "/home/teste/dotnet";

    [Theory]
    [InlineData(ExitCodes.Usage, "install")]
    [InlineData(ExitCodes.Usage, "install", "sdk")]
    [InlineData(ExitCodes.Usage, "install", "10.0.402")]
    [InlineData(ExitCodes.Usage, "install", "dotnet", "10.0.402")]
    [InlineData(ExitCodes.Usage, "install", "sdk", "latest")]
    [InlineData(ExitCodes.Usage, "install", "runtime", "10.0.4xx")]
    [InlineData(ExitCodes.Privilege, "install", "sdk", "10.0.402", "--scope", "global")]
    [InlineData(ExitCodes.Usage, "install", "sdk", "10.0.402", "--scope", "system")]
    [InlineData(ExitCodes.Usage, "install", "sdk", "11.0.100-rc.2.26504.105")]
    [InlineData(ExitCodes.Usage, "install", "sdk", "10.0.402", "--install-dir", "relativo/dotnet")]
    [InlineData(ExitCodes.Usage, "install", "sdk", "10.0.402", "--sha512", "abc")]
    [InlineData(ExitCodes.Usage, "install", "sdk", "10.0", "--archive", "/tmp/x.tar.gz", "--sha512", "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData(ExitCodes.Usage, "install", "sdk", "10.0.402", "--archive", "/tmp/x.tar.gz", "--sha512", "xyz")]
    [InlineData(ExitCodes.Usage, "install", "sdk", "10.0.402", "--archive", "/tmp/x.tar.gz", "--from-global-json")]
    [InlineData(ExitCodes.Usage, "install", "runtime", "--from-global-json")]
    [InlineData(ExitCodes.Usage, "install", "sdk", "10.0.402", "--from-global-json")]
    [InlineData(ExitCodes.NotFound, "install", "sdk", "10.0.402", "--archive", "/tmp/dotnetscope-inexistente.tar.gz", "--sha512", "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData(ExitCodes.NotFound, "install", "sdk", "12.0.100")]
    [InlineData(ExitCodes.NotFound, "install", "WINDOWSDESKTOP-10.0")]
    [InlineData(ExitCodes.Integrity, "install", "SDK-9.0")]
    public async Task Install_EntradasInvalidas_RetornamCodigoDocumentado(int expected, params string[] args)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Cenário Linux simulado (home e host Unix).");
        var rid = args.Contains("SDK-9.0") ? "linux-arm64" : "linux-x64";
        var result = await RunAsync(args, rid: rid);

        Assert.Equal(expected, result.ExitCode);
        Assert.NotEmpty(result.Error);
        Assert.Equal(0, result.Installer.Calls);
    }

    [Fact]
    public async Task Install_ComoRootSemAllowRoot_ErroDePrivilegio()
    {
        var result = await RunAsync(["install", "sdk", "10.0.402"], isRoot: true);

        Assert.Equal(ExitCodes.Privilege, result.ExitCode);
        Assert.Contains("--allow-root", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_ComoRootComAllowRoot_Prossegue()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Cenário Linux simulado (home e host Unix).");
        var result = await RunAsync(["install", "sdk", "10.0.402", "--allow-root"], isRoot: true);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Equal(1, result.Installer.Calls);
    }

    [Fact]
    public async Task Install_ComoRootComAllowRoot_UsaRaizNoHomeDoRoot()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Regras Unix.");
        var result = await RunAsync(["install", "sdk", "10.0.402", "--allow-root"], isRoot: true, home: "/root");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Equal("/root/.dotnet", result.Installer.Root);
    }

    [Fact]
    public async Task Install_InstallDirPersonalizado_MantemFerramentasGlobaisNoHome()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Regras Unix.");
        var result = await RunAsync(["install", "sdk", "10.0.402", "--install-dir", "/data/dotnet", "--output", "json"]);

        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(
            ["/data/dotnet", "/home/teste/.dotnet/tools"],
            json.RootElement.GetProperty("environment").GetProperty("pathEntries").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Install_Windows_UsaZipEConfiguraORegistro()
    {
        var store = new InMemoryEnvironmentStore();

        var result = await RunAsync(
            ["install", "sdk", "10.0.402", "--install-dir", WindowsRoot, "--output", "json"], rid: "win-x64", windowsStore: store);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.EndsWith(".zip", json.RootElement.GetProperty("artifact").GetProperty("fileName").GetString(), StringComparison.Ordinal);
        var environment = json.RootElement.GetProperty("environment");
        Assert.Equal("applied", environment.GetProperty("status").GetString());
        Assert.Contains(@"HKCU\Environment\Path", environment.GetProperty("changedFiles").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(WindowsRoot, store.User["DOTNET_ROOT"].Value);
        Assert.StartsWith(WindowsRoot + ";", store.User["Path"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_Windows_TextoOrientaTerminalNovoEPowerShell()
    {
        var result = await RunAsync(["install", "sdk", "10.0.402", "--install-dir", WindowsRoot], rid: "win-x64", windowsStore: new());

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains(@"Ambiente configurado: HKCU\Environment\DOTNET_ROOT, HKCU\Environment\Path.", result.Output, StringComparison.Ordinal);
        Assert.Contains("dotnetscope env show | Out-String | Invoke-Expression", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_Windows_ConfigureEnvFalse_ImprimeInstrucoesPowerShell()
    {
        var store = new InMemoryEnvironmentStore();

        var result = await RunAsync(
            ["install", "sdk", "10.0.402", "--install-dir", WindowsRoot, "--configure-env", "false"], rid: "win-x64", windowsStore: store);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains($"$env:DOTNET_ROOT = '{WindowsRoot}'", result.Output, StringComparison.Ordinal);
        Assert.Empty(store.User);
    }

    [Fact]
    public async Task Install_Windows_ConflitoNoRegistro_FalhaAntesDeInstalar()
    {
        var store = new InMemoryEnvironmentStore();
        store.User["DOTNET_ROOT"] = new EnvironmentVariableValue(@"D:\dotnet", Expandable: false);

        var result = await RunAsync(["install", "sdk", "10.0.402", "--install-dir", WindowsRoot], rid: "win-x64", windowsStore: store);

        Assert.Equal(ExitCodes.Conflict, result.ExitCode);
        Assert.Equal(0, result.Installer.Calls);
    }

    [Fact]
    public async Task EnvShow_Windows_PadraoPowerShell()
    {
        var result = await RunAsync(["env", "show", "--install-dir", WindowsRoot], rid: "win-x64", windowsStore: new());

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.StartsWith("# Gerado por dotnetscope", result.Output, StringComparison.Ordinal);
        Assert.Contains($"$env:DOTNET_ROOT = '{WindowsRoot}'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallArchive_Windows_ExigeZip()
    {
        var archive = Path.GetTempFileName();
        try
        {
            var result = await RunAsync(
                ["install", "sdk", "10.0.402", "--install-dir", WindowsRoot, "--archive", archive, "--sha512", new string('a', 128)],
                rid: "win-x64",
                windowsStore: new());

            Assert.Equal(ExitCodes.Usage, result.ExitCode);
            Assert.Contains(".zip", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(archive);
        }
    }

    [Fact]
    public async Task Install_PadraoUsaDotnetNoHomeEScopoUser()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Cenário Linux simulado (home e host Unix).");
        var result = await RunAsync(["install", "sdk", "10.0.402", "--output", "json"]);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Equal(Path.Combine(Home, ".dotnet"), result.Installer.Root);
        Assert.Equal(InstallScope.User, result.Installer.Scope);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("installed", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("10.0.402", json.RootElement.GetProperty("request").GetString());
        Assert.Equal("dotnet-sdk-linux-x64.tar.gz", json.RootElement.GetProperty("artifact").GetProperty("fileName").GetString());
        Assert.Equal(Path.Combine(Home, ".dotnet"), json.RootElement.GetProperty("environment").GetProperty("dotnetRoot").GetString());
    }

    [Fact]
    public async Task Install_DependenciasAusentes_EmiteAvisos()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Cenário Linux simulado (home e host Unix).");
        var result = await RunAsync(["install", "sdk", "10.0.402"], missing: ["ICU (libicu)"]);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("aviso: dependência nativa não encontrada: ICU (libicu).", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_CanalEol_EmiteAviso()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Cenário Linux simulado (home e host Unix).");
        var result = await RunAsync(["install", "runtime", "7.0.20"]);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("fora de suporte", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_TextoInformaComoUsarNaSessao()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Cenário Linux simulado (home e host Unix).");
        var result = await RunAsync(["install", "sdk", "10.0.402"]);

        Assert.Contains("Instalado: sdk 10.0.402", result.Output, StringComparison.Ordinal);
        Assert.Contains("Ambiente configurado:", result.Output, StringComparison.Ordinal);
        Assert.Contains("Para usar nesta sessão: . '", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_ConfigureEnvFalse_SomenteImprimeInstrucoes()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Cenário Linux simulado (home e host Unix).");
        var result = await RunAsync(["install", "sdk", "10.0.402", "--configure-env", "false"]);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("--configure-env false", result.Output, StringComparison.Ordinal);
        Assert.Contains("export PATH='/home/teste/.dotnet':'/home/teste/.dotnet/tools':\"$PATH\"", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/home/teste/.dotnet", "'/home/teste/.dotnet'")]
    [InlineData("/home/o'brien/dotnet", "'/home/o'\\''brien/dotnet'")]
    [InlineData("/tmp/$(rm -rf ~)", "'/tmp/$(rm -rf ~)'")]
    public void ShellQuote_EscapaParaShellPosix(string value, string expected) =>
        Assert.Equal(expected, ShellQuoting.Posix(value));

    private static async Task<(int ExitCode, string Output, string Error, FakeInstaller Installer)> RunAsync(
        string[] args,
        string rid = "linux-x64",
        bool isRoot = false,
        IReadOnlyList<string>? missing = null,
        string? home = null,
        InMemoryEnvironmentStore? windowsStore = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var installer = new FakeInstaller();
        var profileHome = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
        var services = new CliServices(output, error, (_, _) => new FixtureMetadataSource(), () => rid)
        {
            CreateInstaller = (_, _) => installer,
            IsRoot = () => isRoot,
            HomeDirectory = () => home ?? Home,
            FindMissingDependencies = () => missing ?? [],
            Prompt = new NonInteractivePrompt(),
            CreateEnvironmentLayout = _ => new EnvironmentLayout(
                profileHome,
                Path.Combine(profileHome, ".config"),
                Path.Combine(profileHome, ".local", "state"),
                IsMacOS: false,
                FishAvailable: false,
                SystemFiles: []),
            ProbeLoginShell = (_, _, _) => Task.FromResult<LoginShellResult?>(null),
            CreateWindowsEnvironment = h => new WindowsEnvironmentConfigurator(
                windowsStore ?? throw new InvalidOperationException("Informe windowsStore para RIDs Windows."),
                Path.Combine(profileHome, "dotnetscope"),
                h),
        };

        try
        {
            var exitCode = await CliApplication.RunAsync(args, services, TestContext.Current.CancellationToken);
            return (exitCode, output.ToString(), error.ToString(), installer);
        }
        finally
        {
            if (Directory.Exists(profileHome))
            {
                Directory.Delete(profileHome, recursive: true);
            }
        }
    }

    private sealed class NonInteractivePrompt : IUserPrompt
    {
        public bool IsInteractive => false;

        public bool Confirm(string question) => throw new InvalidOperationException("Não deve perguntar.");
    }

    internal sealed class FakeInstaller : IArtifactInstaller
    {
        public int Calls { get; private set; }

        public string? Root { get; private set; }

        public InstallScope? Scope { get; private set; }

        public Task<InstallResult> InstallAsync(ResolvedArtifact artifact, string root, InstallScope scope, CancellationToken cancellationToken)
        {
            Calls++;
            Root = root;
            Scope = scope;
            return Task.FromResult(new InstallResult(
                InstallStatus.Installed,
                artifact,
                root,
                [$"sdk/{artifact.Version}"],
                [],
                false,
                new HostValidation(true, "ok")));
        }

        public void Dispose()
        {
        }
    }
}
