using System.Text.Json;
using DotnetScope.Cli;
using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Infrastructure.Platform;
using DotnetScope.Infrastructure.SystemScope;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.UnitTests;

public sealed class GlobalScopeTests : IDisposable
{
    private static readonly UnixFileStatus Trusted = new(0, Convert.ToInt32("755", 8));

    private readonly string _base = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_base))
        {
            Directory.Delete(_base, recursive: true);
        }
    }

    [Theory]
    [InlineData("0 755", 0, "755")]
    [InlineData("1000 2775\n", 1000, "2775")]
    [InlineData("0 1777", 0, "1777")]
    public void Stat_InterpretaSaida(string output, int uid, string mode) =>
        Assert.Equal(new UnixFileStatus(uid, Convert.ToInt32(mode, 8)), UnixFileOwnership.Parse(output));

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("x 755")]
    [InlineData("0 789")]
    [InlineData("0 755 extra")]
    public void Stat_SaidaInvalida_EhNula(string output) => Assert.Null(UnixFileOwnership.Parse(output));

    [Fact]
    public void EnsureTrusted_CadeiaDoRoot_Passa()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Regras Unix.");
        GlobalLocations.EnsureTrusted("/fake/a/b", path => path == "/fake/a/b" ? null : Trusted, "teste");
    }

    [Theory]
    [InlineData("/fake", 1000, "755", "UID 1000")]
    [InlineData("/fake/a", 0, "775", "escrita de grupo ou outros")]
    [InlineData("/fake/a", 0, "757", "escrita de grupo ou outros")]
    [InlineData("/fake", 0, "1777", "escrita de grupo ou outros")]
    public void EnsureTrusted_AncestralInseguro_RetornaPrivilegio(string unsafePath, int uid, string mode, string reason)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Regras Unix.");

        var exception = Assert.Throws<DotnetScopeException>(() => GlobalLocations.EnsureTrusted(
            "/fake/a/b",
            path => path == unsafePath ? new UnixFileStatus(uid, Convert.ToInt32(mode, 8)) : Trusted,
            "teste"));

        Assert.Equal(ExitCodes.Privilege, exception.ExitCode);
        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/", ExitCodes.Privilege)]
    [InlineData("/usr", ExitCodes.Privilege)]
    [InlineData("/usr/local/", ExitCodes.Privilege)]
    [InlineData("/etc/dotnet", ExitCodes.Privilege)]
    [InlineData("/proc/1", ExitCodes.Privilege)]
    [InlineData("/tmp", ExitCodes.Privilege)]
    [InlineData("/usr/lib/dotnet", ExitCodes.Conflict)]
    [InlineData("/usr/share/dotnet/", ExitCodes.Conflict)]
    [InlineData("relativo/dotnet", ExitCodes.Usage)]
    public void ValidateRoot_RecusaLocaisInadequados(string path, int code)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Regras Unix.");

        Assert.Equal(code, Assert.Throws<DotnetScopeException>(() => GlobalLocations.ValidateRoot(path)).ExitCode);
    }

    [Theory]
    [InlineData("/usr/local/share/dotnet")]
    [InlineData("/opt/dotnet/")]
    public void ValidateRoot_AceitaDiretoriosDedicados(string path)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Regras Unix.");

        Assert.Equal(path.TrimEnd('/'), GlobalLocations.ValidateRoot(path));
    }

    [Fact]
    public void RenderSystemProfile_SomenteRaizNoPathComGuarda()
    {
        var script = EnvironmentScripts.RenderSystemProfile("/usr/local/share/dotnet", telemetryOptOut: true);

        Assert.Contains("export DOTNET_ROOT='/usr/local/share/dotnet'", script, StringComparison.Ordinal);
        Assert.Contains("  *) export PATH='/usr/local/share/dotnet':\"${PATH}\" ;;", script, StringComparison.Ordinal);
        Assert.DoesNotContain("tools", script, StringComparison.Ordinal);
        Assert.Contains("export DOTNET_CLI_TELEMETRY_OPTOUT=1", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_Global_SemRoot_RetornaPrivilegio()
    {
        var result = await RunAsync(["install", "sdk", "10.0.402", "--scope", "global"], isRoot: false);

        Assert.Equal(ExitCodes.Privilege, result.ExitCode);
        Assert.Contains("sudo", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, result.Installer.Calls);
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("osx-arm64")]
    public async Task Install_Global_ForaDoLinux_AindaNaoDisponivel(string rid)
    {
        var result = await RunAsync(["install", "sdk", "10.0.402", "--scope", "global"], rid: rid);

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
    }

    [Fact]
    public async Task Install_Global_ConfiguraProfileDEInstallLocation()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");

        var result = await RunAsync(["install", "sdk", "10.0.402", "--scope", "global", "--output", "json"]);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Equal(result.Layout.Root, result.Installer.Root);
        Assert.Equal(Core.Installation.InstallScope.Global, result.Installer.Scope);
        Assert.Equal(result.Layout.Root + "\n", await File.ReadAllTextAsync(result.Layout.InstallLocationFile, TestContext.Current.CancellationToken));
        Assert.Contains($"export DOTNET_ROOT='{result.Layout.Root}'", await File.ReadAllTextAsync(result.Layout.ProfileScript, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        using var json = JsonDocument.Parse(result.Output);
        var environment = json.RootElement.GetProperty("environment");
        Assert.Equal([result.Layout.Root], environment.GetProperty("pathEntries").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(result.Layout.ProfileScript, environment.GetProperty("script").GetString());
        Assert.Equal("global", json.RootElement.GetProperty("scope").GetString());
    }

    [Fact]
    public async Task Install_Global_DotnetRootDeTerceiroNoSistema_FalhaAntesDeInstalar()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");

        var result = await RunAsync(
            ["install", "sdk", "10.0.402", "--scope", "global"],
            prepare: layout => File.WriteAllText(layout.SystemFiles[0], "DOTNET_ROOT=/opt/outro\n"));

        Assert.Equal(ExitCodes.Conflict, result.ExitCode);
        Assert.Equal(0, result.Installer.Calls);
        Assert.False(File.Exists(result.Layout.ProfileScript));
    }

    [Fact]
    public async Task Install_Global_DotnetDoSistemaNoPath_EhConflito()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");

        var result = await RunAsync(
            ["install", "sdk", "10.0.402", "--scope", "global"],
            prepare: layout =>
            {
                var package = Path.Combine(Path.GetDirectoryName(layout.Root)!, "pacote");
                Directory.CreateDirectory(package);
                File.WriteAllText(Path.Combine(package, "dotnet"), "host");
                File.CreateSymbolicLink(layout.SystemDotnetLinks[0], Path.Combine(package, "dotnet"));
            });

        Assert.Equal(ExitCodes.Conflict, result.ExitCode);
        Assert.Contains("--env-conflict", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_Global_InstallLocationDeTerceiro_OverrideERemoveRestauram()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");
        const string previous = "/opt/dotnet-pacote\n";

        var result = await RunAsync(
            ["install", "sdk", "10.0.402", "--scope", "global", "--env-conflict", "override"],
            prepare: layout =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(layout.InstallLocationFile)!);
                File.WriteAllText(layout.InstallLocationFile, previous);
            },
            after: async (services, layout) =>
            {
                Assert.Equal(layout.Root + "\n", await File.ReadAllTextAsync(layout.InstallLocationFile, TestContext.Current.CancellationToken));
                Directory.CreateDirectory(layout.Root);
                await File.WriteAllTextAsync(Path.Combine(layout.Root, "dotnet"), "host", TestContext.Current.CancellationToken);
                Assert.Equal(ExitCodes.Success, await CliApplication.RunAsync(["env", "remove", "--scope", "global"], services, TestContext.Current.CancellationToken));
                Assert.Equal(previous, await File.ReadAllTextAsync(layout.InstallLocationFile, TestContext.Current.CancellationToken));
                Assert.False(File.Exists(layout.ProfileScript));
            });

        Assert.Equal(ExitCodes.Success, result.ExitCode);
    }

    [Fact]
    public async Task Install_Global_RaizNaoConfiavel_RetornaPrivilegio()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");

        var result = await RunAsync(["install", "sdk", "10.0.402", "--scope", "global"], stat: _ => new UnixFileStatus(1000, Convert.ToInt32("755", 8)));

        Assert.Equal(ExitCodes.Privilege, result.ExitCode);
        Assert.Equal(0, result.Installer.Calls);
    }

    [Fact]
    public async Task Install_Global_IgnoraCertificadosIndicadosPeloAmbiente()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");
        var unset = new List<string>();

        var result = await RunAsync(
            ["install", "sdk", "10.0.402", "--scope", "global"],
            environment: name => name == "SSL_CERT_FILE" ? "/home/usuario/ca.pem" : null,
            unset: unset.Add);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Equal(["SSL_CERT_FILE"], unset);
        Assert.Contains("SSL_CERT_FILE é ignorada no escopo global", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_Global_ManifestoComDonoInesperado_EhRecusado()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");
        string? manifest = null;

        var result = await RunAsync(
            ["install", "sdk", "10.0.402", "--scope", "global"],
            prepare: layout =>
            {
                Directory.CreateDirectory(layout.StateDirectory);
                manifest = layout.ManifestFile;
                File.WriteAllText(manifest, "{}");
            },
            stat: path => path == manifest ? new UnixFileStatus(1000, Convert.ToInt32("600", 8)) : Path.Exists(path) ? Trusted : null);

        Assert.Equal(ExitCodes.Conflict, result.ExitCode);
        Assert.Equal(0, result.Installer.Calls);
    }

    [Fact]
    public async Task Uninstall_Global_DiretorioDeEstadoGravavelPorOutros_EhRecusado()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");
        string? state = null;

        var result = await RunAsync(
            ["uninstall", "sdk", "10.0.402", "--scope", "global"],
            prepare: layout =>
            {
                Directory.CreateDirectory(layout.StateDirectory);
                state = layout.StateDirectory;
            },
            stat: path => path == state ? new UnixFileStatus(0, Convert.ToInt32("2775", 8)) : Path.Exists(path) ? Trusted : null);

        Assert.Equal(ExitCodes.Privilege, result.ExitCode);
    }

    [Fact]
    public async Task EnvShow_Global_ImprimeScriptDoSistemaSemExigirRoot()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");

        var result = await RunAsync(["env", "show", "--scope", "global", "--install-dir", "/opt/dotnet"], isRoot: false);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("export DOTNET_ROOT='/opt/dotnet'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnvApply_Global_SemRoot_RetornaPrivilegio()
    {
        var result = await RunAsync(["env", "apply", "--scope", "global"], isRoot: false);

        Assert.Equal(ExitCodes.Privilege, result.ExitCode);
    }

    private async Task<(int ExitCode, string Output, string Error, InstallCommandTests.FakeInstaller Installer, GlobalLayout Layout)> RunAsync(
        string[] args,
        string rid = "linux-x64",
        bool isRoot = true,
        Func<string, UnixFileStatus?>? stat = null,
        Action<GlobalLayout>? prepare = null,
        Func<CliServices, GlobalLayout, Task>? after = null,
        Func<string, string?>? environment = null,
        Action<string>? unset = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var installer = new InstallCommandTests.FakeInstaller();
        var etc = Path.Combine(_base, "etc");
        var layout = new GlobalLayout(
            Path.Combine(_base, "share", "dotnet"),
            Path.Combine(_base, "var", "lib", "dotnetscope"),
            Path.Combine(_base, "var", "cache", "dotnetscope"),
            Path.Combine(etc, "profile.d", "dotnetscope.sh"),
            Path.Combine(etc, "dotnet", "install_location_x64"),
            [Path.Combine(etc, "environment")],
            [Path.Combine(_base, "usr-bin-dotnet")]);
        Directory.CreateDirectory(etc);
        prepare?.Invoke(layout);
        var services = new CliServices(output, error, (_, _) => new FixtureMetadataSource(), () => rid)
        {
            CreateGlobalInstaller = (_, _, _) => installer,
            CreateInstaller = (_, _) => installer,
            IsRoot = () => isRoot,
            HomeDirectory = () => Path.Combine(_base, "root"),
            FindMissingDependencies = () => [],
            CreateGlobalLayout = _ => layout,
            StatPath = stat ?? (path => Path.Exists(path) ? Trusted : null),
            ProbeLoginShell = (_, _, _) => Task.FromResult<LoginShellResult?>(null),
            GetEnvironmentVariable = environment ?? (_ => null),
            UnsetEnvironmentVariable = unset ?? (_ => { }),
        };

        var exitCode = await CliApplication.RunAsync(args, services, TestContext.Current.CancellationToken);
        if (after is not null)
        {
            await after(services, layout);
        }

        return (exitCode, output.ToString(), error.ToString(), installer, layout);
    }
}
