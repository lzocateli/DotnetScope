using System.Text.Json;
using DotnetScope.Cli;
using DotnetScope.Cli.Commands;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Resolution;

namespace DotnetScope.UnitTests;

public sealed class InstallCommandTests
{
    private static readonly string Home = OperatingSystem.IsWindows() ? @"C:\Users\teste" : "/home/teste";

    [Theory]
    [InlineData(ExitCodes.Usage, "install")]
    [InlineData(ExitCodes.Usage, "install", "sdk")]
    [InlineData(ExitCodes.Usage, "install", "10.0.402")]
    [InlineData(ExitCodes.Usage, "install", "dotnet", "10.0.402")]
    [InlineData(ExitCodes.Usage, "install", "sdk", "latest")]
    [InlineData(ExitCodes.Usage, "install", "runtime", "10.0.4xx")]
    [InlineData(ExitCodes.Usage, "install", "sdk", "10.0.402", "--scope", "global")]
    [InlineData(ExitCodes.Usage, "install", "sdk", "10.0.402", "--scope", "system")]
    [InlineData(ExitCodes.Usage, "install", "sdk", "11.0.100-rc.2.26504.105")]
    [InlineData(ExitCodes.Usage, "install", "sdk", "10.0.402", "--install-dir", "relativo/dotnet")]
    [InlineData(ExitCodes.NotFound, "install", "sdk", "12.0.100")]
    [InlineData(ExitCodes.NotFound, "install", "WINDOWSDESKTOP-10.0")]
    [InlineData(ExitCodes.Integrity, "install", "SDK-9.0")]
    public async Task Install_EntradasInvalidas_RetornamCodigoDocumentado(int expected, params string[] args)
    {
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
    public async Task Install_Windows_AindaNaoSuportado()
    {
        var result = await RunAsync(["install", "sdk", "10.0.402"], rid: "win-x64");

        Assert.Equal(ExitCodes.NotFound, result.ExitCode);
    }

    [Fact]
    public async Task Install_PadraoUsaDotnetNoHomeEScopoUser()
    {
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
        var result = await RunAsync(["install", "sdk", "10.0.402"], missing: ["ICU (libicu)"]);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("aviso: dependência nativa não encontrada: ICU (libicu).", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_CanalEol_EmiteAviso()
    {
        var result = await RunAsync(["install", "runtime", "7.0.20"]);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("fora de suporte", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_TextoInformaComoUsarNaSessao()
    {
        var result = await RunAsync(["install", "sdk", "10.0.402"]);

        Assert.Contains("Instalado: sdk 10.0.402", result.Output, StringComparison.Ordinal);
        Assert.Contains("export PATH='/home/teste/.dotnet':'/home/teste/.dotnet/tools':\"$PATH\"", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/home/teste/.dotnet", "'/home/teste/.dotnet'")]
    [InlineData("/home/o'brien/dotnet", "'/home/o'\\''brien/dotnet'")]
    [InlineData("/tmp/$(rm -rf ~)", "'/tmp/$(rm -rf ~)'")]
    public void ShellQuote_EscapaParaShellPosix(string value, string expected) =>
        Assert.Equal(expected, InstallCommand.ShellQuote(value));

    private static async Task<(int ExitCode, string Output, string Error, FakeInstaller Installer)> RunAsync(
        string[] args,
        string rid = "linux-x64",
        bool isRoot = false,
        IReadOnlyList<string>? missing = null,
        string? home = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var installer = new FakeInstaller();
        var services = new CliServices(output, error, (_, _) => new FixtureMetadataSource(), () => rid)
        {
            CreateInstaller = (_, _) => installer,
            IsRoot = () => isRoot,
            HomeDirectory = () => home ?? Home,
            FindMissingDependencies = () => missing ?? [],
            Prompt = new NonInteractivePrompt(),
        };

        var exitCode = await CliApplication.RunAsync(args, services, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString(), installer);
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
