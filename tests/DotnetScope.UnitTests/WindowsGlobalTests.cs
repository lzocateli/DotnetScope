using System.Text.Json;
using DotnetScope.Cli;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Resolution;
using DotnetScope.Core.Versions;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.SystemScope;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.UnitTests;

public sealed class WindowsGlobalTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_base))
        {
            Directory.Delete(_base, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(3010, true, true)]
    [InlineData(1641, true, true)]
    [InlineData(1618, false, false)]
    [InlineData(1602, false, false)]
    [InlineData(1603, false, false)]
    public void SetupExitCodes_MapeiaCodigosDoInstalador(int code, bool succeeded, bool reboot)
    {
        var (ok, rebootRequired, error) = SetupExitCodes.Interpret(code);

        Assert.Equal(succeeded, ok);
        Assert.Equal(reboot, rebootRequired);
        Assert.Equal(succeeded, error is null);
    }

    [Theory]
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", true)]
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation Fans Ltd, C=US", false)]
    [InlineData("CN=\"X, O=Microsoft Corporation\", O=Contoso, C=US", false)]
    [InlineData("CN=Contoso, O=Microsoft Corporation, C=US", false)]
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, O=Contoso", false)]
    [InlineData("CN=Microsoft Corporation, C=US", false)]
    public void SignerPolicy_ExigeOECnExatos(string subject, bool expected) =>
        Assert.Equal(expected, SignerPolicy.IsMicrosoft(new System.Security.Cryptography.X509Certificates.X500DistinguishedName(subject)));

    [Fact]
    public async Task Install_SourceInstallerNoEscopoUser_EhErroDeUso()
    {
        var result = await RunAsync(["install", "runtime", "10.0", "--source", "installer"]);

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
    }

    [Fact]
    public async Task Resolver_ComInstaller_EscolheOExeComSha512()
    {
        var resolver = new VersionResolver(new FixtureMetadataSource());

        var artifact = await resolver.ResolveAsync(
            ComponentRequest.Parse(ComponentKind.Runtime, "10.0"), new ResolveOptions("win-x64", false, Installer: true), TestContext.Current.CancellationToken);

        Assert.EndsWith("-win-x64.exe", artifact.File.Url.AbsolutePath, StringComparison.Ordinal);
        Assert.NotEmpty(artifact.Sha512);
    }

    [Fact]
    public async Task Install_Global_SemElevacao_RetornaPrivilegio()
    {
        var result = await RunAsync(["install", "runtime", "10.0", "--scope", "global", "--yes"], elevated: false);

        Assert.Equal(ExitCodes.Privilege, result.ExitCode);
        Assert.Contains("Executar como administrador", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, result.Installer.Calls);
    }

    [Theory]
    [InlineData("--source", "archive")]
    [InlineData("--source", "apt")]
    [InlineData("--install-dir", @"D:\dotnet")]
    [InlineData("--install-deps", null)]
    public async Task Install_Global_OpcoesQueNaoSeAplicamAoInstalador_SaoErroDeUso(string option, string? value)
    {
        string[] args = value is null
            ? ["install", "runtime", "10.0", "--scope", "global", option]
            : ["install", "runtime", "10.0", "--scope", "global", option, value];

        var result = await RunAsync(args);

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
    }

    [Fact]
    public async Task Install_Global_UsaInstaladorOficialSemConfigurarAmbiente()
    {
        var result = await RunAsync(["install", "runtime", "10.0", "--scope", "global", "--yes", "--output", "json"]);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Equal(1, result.Installer.Calls);
        Assert.Equal(result.Layout.Root, result.Installer.Root);
        using var json = JsonDocument.Parse(result.Output);
        Assert.EndsWith(".exe", json.RootElement.GetProperty("artifact").GetProperty("fileName").GetString(), StringComparison.Ordinal);
        Assert.Equal("package", json.RootElement.GetProperty("environment").GetProperty("status").GetString());
        Assert.Equal([result.Layout.StateDirectory], result.Protected);
    }

    [Fact]
    public async Task Install_SourceInstallerForaDoWindows_EhErroDeUso()
    {
        var result = await RunAsync(["install", "runtime", "10.0", "--scope", "global", "--source", "installer"], rid: "linux-x64");

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
    }

    [Fact]
    public async Task Uninstall_Global_SemRegistro_RetornaNaoEncontradoSemExecutar()
    {
        var result = await RunAsync(["uninstall", "runtime", "10.0.13", "--scope", "global"]);

        Assert.Equal(ExitCodes.NotFound, result.ExitCode);
        Assert.Empty(result.Uninstalled);
    }

    [Fact]
    public async Task Uninstall_Global_ComRegistro_ExecutaOInstaladorRegistrado()
    {
        var result = await RunAsync(
            ["uninstall", "runtime", "10.0.13", "--scope", "global", "--output", "json"],
            prepare: layout => new InstallationManifestStore(layout.ManifestFile).AddAsync(
                new InstallationRecord(
                    ComponentKind.Runtime, "10.0.13", "win-x64", InstallScope.Global, layout.Root, "dotnet-runtime-win-x64.exe",
                    new Uri("https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.13/dotnet-runtime-10.0.13-win-x64.exe"),
                    new string('a', 128), DateTimeOffset.UtcNow, [], [], false),
                new NullDiagnostics(),
                CancellationToken.None).GetAwaiter().GetResult());

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Equal(["dotnet-runtime-win-x64.exe"], result.Uninstalled);
        Assert.Contains("\"origin\": \"installer\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Env_Global_NoWindows_EhErroDeUso()
    {
        var result = await RunAsync(["env", "apply", "--scope", "global"]);

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
    }

    private async Task<(int ExitCode, string Output, string Error, InstallCommandTests.FakeInstaller Installer, InstallerGlobalLayout Layout, List<string> Protected, List<string> Uninstalled)> RunAsync(
        string[] args,
        string rid = "win-x64",
        bool elevated = true,
        Action<InstallerGlobalLayout>? prepare = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var installer = new InstallCommandTests.FakeInstaller();
        var layout = new InstallerGlobalLayout(Path.Combine(_base, "Program Files", "dotnet"), Path.Combine(_base, "ProgramData", "dotnetscope"));
        prepare?.Invoke(layout);
        var protectedPaths = new List<string>();
        var uninstalled = new List<string>();
        var services = new CliServices(output, error, (_, _) => new FixtureMetadataSource(), () => rid)
        {
            CreateInstaller = (_, _) => installer,
            CreateGlobalInstaller = (_, _, _) => installer,
            CreateOfficialInstaller = (_, _, _) => installer,
            IsElevated = () => elevated,
            IsRoot = () => false,
            HomeDirectory = () => Path.Combine(_base, "home"),
            FindMissingDependencies = () => [],
            CreateInstallerLayout = _ => layout,
            EnsureProtectedDirectory = protectedPaths.Add,
            UninstallWindowsSetup = (_, record, _, _) =>
            {
                uninstalled.Add(record.FileName);
                return Task.CompletedTask;
            },
            ProbeLoginShell = (_, _, _) => Task.FromResult<LoginShellResult?>(null),
        };

        var exitCode = await CliApplication.RunAsync(args, services, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString(), installer, layout, protectedPaths, uninstalled);
    }

    private sealed class NullDiagnostics : IDiagnosticSink
    {
        public void Warning(string message)
        {
        }

        public void Verbose(string message)
        {
        }
    }
}
