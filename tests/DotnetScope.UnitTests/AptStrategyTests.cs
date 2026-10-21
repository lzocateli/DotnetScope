using System.Text.Json;
using DotnetScope.Cli;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.Platform;
using DotnetScope.Infrastructure.SystemScope;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.UnitTests;

public sealed class AptStrategyTests : IDisposable
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
    [InlineData("10.0.13-0ubuntu1~24.04.1", "10.0.13")]
    [InlineData("1:8.0.120-1", "8.0.120")]
    [InlineData("10.0.113", "10.0.113")]
    public void UpstreamVersion_RemoveEpocaERevisao(string packageVersion, string expected) =>
        Assert.Equal(expected, AptPackages.UpstreamVersion(packageVersion));

    [Fact]
    public void PackageName_PorComponente()
    {
        var channel = new ChannelVersion(10, 0);

        Assert.Equal("dotnet-sdk-10.0", AptPackages.PackageName(ComponentKind.Sdk, channel));
        Assert.Equal("dotnet-runtime-10.0", AptPackages.PackageName(ComponentKind.Runtime, channel));
        Assert.Equal("aspnetcore-runtime-10.0", AptPackages.PackageName(ComponentKind.AspNetCore, channel));
        Assert.Null(AptPackages.PackageName(ComponentKind.WindowsDesktop, channel));
    }

    [Theory]
    [InlineData("10.0.13", "10.0", "10.0.13", false, true)]
    [InlineData("10.0.12", "10.0", "10.0.13", false, false)]
    [InlineData("10.0.113", "10.0", "10.0.402", false, false)]
    [InlineData("10.0.113", "10.0", "10.0.402", true, true)]
    [InlineData("10.0.113", "10.0.1xx", "10.0.113", true, true)]
    [InlineData("10.0.113", "10.0.4xx", "10.0.402", true, false)]
    [InlineData("10.0.113", "10.0.402", "10.0.402", true, false)]
    [InlineData("10.0.113", "10.0.113", "10.0.113", true, true)]
    public void Accepts_AutoSomenteVersaoResolvidaExplicitoRespeitaPedido(string candidate, string request, string resolved, bool explicitApt, bool expected)
    {
        var apt = new AptCandidate("dotnet-sdk-10.0", candidate + "-0ubuntu1", DotnetVersion.Parse(candidate));

        Assert.Equal(expected, AptPackages.Accepts(apt, VersionSpec.Parse(request), DotnetVersion.Parse(resolved), explicitApt));
    }

    [Fact]
    public async Task FindCandidate_LePolicyComArgumentosSeguros()
    {
        var runner = new FakeCommandRunner { Policies = { ["dotnet-runtime-10.0"] = "10.0.13-0ubuntu1~24.04.1" } };

        var candidate = await AptPackages.FindCandidateAsync(runner, "dotnet-runtime-10.0", TestContext.Current.CancellationToken);

        Assert.Equal(new AptCandidate("dotnet-runtime-10.0", "10.0.13-0ubuntu1~24.04.1", DotnetVersion.Parse("10.0.13")), candidate);
        Assert.Equal(AptPackages.AptCache, runner.Calls[0].Executable);
        Assert.Equal(["policy", "--", "dotnet-runtime-10.0"], runner.Calls[0].Arguments);
    }

    [Theory]
    [InlineData("(none)")]
    [InlineData("10.0.13; rm -rf /")]
    [InlineData("-o Dpkg::Options=x")]
    [InlineData("")]
    public async Task FindCandidate_CandidatoAusenteOuInvalido_EhNulo(string policy)
    {
        var runner = new FakeCommandRunner { Policies = { ["dotnet-sdk-10.0"] = policy } };

        Assert.Null(await AptPackages.FindCandidateAsync(runner, "dotnet-sdk-10.0", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("-o")]
    [InlineData("dotnet sdk")]
    [InlineData("dotnet-sdk;id")]
    [InlineData("DOTNET")]
    public async Task NomeDePacoteInvalido_EhRecusadoAntesDeExecutar(string package)
    {
        var runner = new FakeCommandRunner();

        await Assert.ThrowsAsync<DotnetScopeException>(() => AptPackages.FindCandidateAsync(runner, package, TestContext.Current.CancellationToken));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task InstallDeps_Apt_TabelaFixaComPrimeiraAlternativaDisponivel()
    {
        var runner = new FakeCommandRunner { Policies = { ["libicu74"] = "74.2-1ubuntu3.1", ["libicu72"] = "72.1-3", ["libssl3t64"] = "3.0.13-0ubuntu3" } };

        var (manager, packages) = await NativeDependencyInstaller.InstallAsync(runner, TestContext.Current.CancellationToken);

        Assert.Equal("apt", manager);
        Assert.Contains("libicu74", packages);
        Assert.DoesNotContain("libicu72", packages);
        Assert.Contains("libssl3t64", packages);
        var install = runner.Calls.Single(c => c.Executable == AptPackages.AptGet);
        Assert.Equal(["install", "-y", "--no-install-recommends", "--"], install.Arguments.Take(4));
    }

    [Fact]
    public async Task InstallDeps_SemGerenciadorSuportado_RetornaDependenciaAusente()
    {
        var runner = new FakeCommandRunner { Existing = [] };

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => NativeDependencyInstaller.InstallAsync(runner, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.MissingDependency, exception.ExitCode);
    }

    [Theory]
    [InlineData("--source", "apt")]
    [InlineData("--install-deps", null)]
    public async Task Install_OpcoesGlobais_NoEscopoUser_SaoErroDeUso(string option, string? value)
    {
        string[] args = value is null ? ["install", "sdk", "10.0.402", option] : ["install", "sdk", "10.0.402", option, value];

        var result = await RunAsync(args, new FakeCommandRunner());

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
    }

    [Fact]
    public async Task Install_Auto_CandidatoIgualAoResolvido_UsaAptSemConfigurarAmbiente()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");
        var runner = new FakeCommandRunner { Policies = { ["dotnet-runtime-10.0"] = "10.0.13-0ubuntu1~24.04.1" } };

        var result = await RunAsync(["install", "runtime", "10.0", "--scope", "global", "--yes", "--dry-run", "--output", "json"], runner);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Contains(json.RootElement.GetProperty("installDir").GetString(), AptPackages.PackageRoots);
        Assert.Equal("apt:dotnet-runtime-10.0", json.RootElement.GetProperty("artifact").GetProperty("url").GetString());
        Assert.Equal("package", json.RootElement.GetProperty("environment").GetProperty("status").GetString());
        Assert.DoesNotContain(runner.Calls, c => c.Executable == AptPackages.AptGet);
    }

    [Fact]
    public async Task Install_Auto_CandidatoDeOutraFaixa_UsaArchive()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");
        var runner = new FakeCommandRunner { Policies = { ["dotnet-sdk-10.0"] = "10.0.113-0ubuntu1~24.04.1" } };

        var result = await RunAsync(["install", "sdk", "10.0", "--scope", "global", "--yes", "--dry-run", "--output", "json"], runner);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("10.0.402", json.RootElement.GetProperty("version").GetString());
        Assert.StartsWith("https://", json.RootElement.GetProperty("artifact").GetProperty("url").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_SourceApt_SemCandidato_RetornaNaoEncontrado()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");

        var result = await RunAsync(["install", "sdk", "10.0", "--scope", "global", "--source", "apt", "--yes"], new FakeCommandRunner());

        Assert.Equal(ExitCodes.NotFound, result.ExitCode);
        Assert.Contains("não adiciona repositórios", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_Apt_ComInstalacaoGlobalPorArquivoConfigurada_EhConflito()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");
        var runner = new FakeCommandRunner { Policies = { ["dotnet-runtime-10.0"] = "10.0.13-0ubuntu1" } };

        var result = await RunAsync(
            ["install", "runtime", "10.0", "--scope", "global", "--source", "apt", "--yes"],
            runner,
            prepare: layout =>
            {
                Directory.CreateDirectory(layout.StateDirectory);
                File.WriteAllText(Path.Combine(layout.StateDirectory, "env-state.json"), $$"""{"schemaVersion":1,"dotnetRoot":"{{layout.Root}}","files":[]}""");
            });

        Assert.Equal(ExitCodes.Conflict, result.ExitCode);
        Assert.DoesNotContain(runner.Calls, c => c.Executable == AptPackages.AptGet);
    }

    [Fact]
    public async Task Uninstall_RegistroApt_RemovePacoteERegistro()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");
        var runner = new FakeCommandRunner { Installed = { ["dotnet-runtime-10.0"] = "10.0.13-0ubuntu1" } };
        GlobalLayout? used = null;

        var result = await RunAsync(
            ["uninstall", "runtime", "10.0.13", "--scope", "global", "--output", "json"],
            runner,
            prepare: layout =>
            {
                used = layout;
                new InstallationManifestStore(layout.ManifestFile).AddAsync(
                    new InstallationRecord(
                        ComponentKind.Runtime, "10.0.13", "linux-x64", InstallScope.Global, "/usr/lib/dotnet",
                        "dotnet-runtime-10.0=10.0.13-0ubuntu1", new Uri("apt:dotnet-runtime-10.0"), string.Empty, DateTimeOffset.UtcNow, [], [], false),
                    new NullDiagnostics(),
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains(runner.Calls, c => c.Executable == AptPackages.AptGet && c.Arguments.SequenceEqual(["remove", "-y", "--", "dotnet-runtime-10.0"]));
        Assert.Empty(new InstallationManifestStore(used!.ManifestFile).Read());
        Assert.Contains("\"origin\": \"apt\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_ComDependentes_NaoRemoveNada()
    {
        var runner = new FakeCommandRunner { Dependents = { "dotnet-sdk-10.0", "aspnetcore-runtime-10.0" } };

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(
            () => AptPackages.RemoveAsync(runner, "dotnet-runtime-10.0", TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
        Assert.Contains("dotnet-sdk-10.0", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Calls, c => c.Arguments.Contains("-y"));
    }

    [Fact]
    public async Task AptInstaller_PacoteJaInstaladoNaMesmaVersao_NaoRegistraNemReinstala()
    {
        var runner = new FakeCommandRunner { Installed = { ["dotnet-runtime-10.0"] = "10.0.13-0ubuntu1" } };
        var candidate = new AptCandidate("dotnet-runtime-10.0", "10.0.13-0ubuntu1", DotnetVersion.Parse("10.0.13"));
        var manifest = new InstallationManifestStore(Path.Combine(_base, "manifest.json"));
        using var installer = new AptInstaller(runner, candidate, manifest, new NullDiagnostics());

        var result = await installer.InstallAsync(
            AptPackages.ToArtifact(candidate, ComponentKind.Runtime, "linux-x64"), "/usr/lib/dotnet", InstallScope.Global, TestContext.Current.CancellationToken);

        Assert.Equal(InstallStatus.AlreadyInstalled, result.Status);
        Assert.Empty(manifest.Read());
        Assert.DoesNotContain(runner.Calls, c => c.Executable == AptPackages.AptGet);
    }

    [Fact]
    public async Task AptInstaller_PacoteJaInstaladoEmOutraVersao_EhConflito()
    {
        var runner = new FakeCommandRunner { Installed = { ["dotnet-runtime-10.0"] = "10.0.12-0ubuntu1" } };
        var candidate = new AptCandidate("dotnet-runtime-10.0", "10.0.13-0ubuntu1", DotnetVersion.Parse("10.0.13"));
        using var installer = new AptInstaller(runner, candidate, new InstallationManifestStore(Path.Combine(_base, "manifest.json")), new NullDiagnostics());

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => installer.InstallAsync(
            AptPackages.ToArtifact(candidate, ComponentKind.Runtime, "linux-x64"), "/usr/lib/dotnet", InstallScope.Global, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
        Assert.DoesNotContain(runner.Calls, c => c.Executable == AptPackages.AptGet);
    }

    [Fact]
    public async Task Install_SourceAptComInstallDir_EhErroDeUso()
    {
        var result = await RunAsync(["install", "runtime", "10.0", "--scope", "global", "--source", "apt", "--install-dir", "/opt/dotnet"], new FakeCommandRunner());

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
    }

    [Fact]
    public async Task Install_AutoComInstallDir_UsaArchive()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");
        var runner = new FakeCommandRunner { Policies = { ["dotnet-runtime-10.0"] = "10.0.13-0ubuntu1" } };

        var result = await RunAsync(["install", "runtime", "10.0", "--scope", "global", "--install-dir", "/opt/dotnet", "--yes", "--dry-run", "--output", "json"], runner);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("/opt/dotnet", json.RootElement.GetProperty("installDir").GetString());
        Assert.DoesNotContain(runner.Calls, c => c.Executable == AptPackages.AptCache);
    }

    [Fact]
    public async Task Install_AutoComInstalacaoPorArquivoConfigurada_SegueComArchive()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");
        var runner = new FakeCommandRunner { Policies = { ["dotnet-runtime-10.0"] = "10.0.13-0ubuntu1" } };
        GlobalLayout? used = null;

        var result = await RunAsync(
            ["install", "runtime", "10.0", "--scope", "global", "--yes", "--dry-run", "--output", "json"],
            runner,
            prepare: layout =>
            {
                used = layout;
                Directory.CreateDirectory(layout.StateDirectory);
                File.WriteAllText(Path.Combine(layout.StateDirectory, "env-state.json"), $$"""{"schemaVersion":1,"dotnetRoot":"{{layout.Root}}","files":[]}""");
            });

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(used!.Root, json.RootElement.GetProperty("installDir").GetString());
    }

    [Fact]
    public async Task Install_DryRunEmTexto_ComApt_DescrevePacote()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");
        var runner = new FakeCommandRunner { Policies = { ["dotnet-runtime-10.0"] = "10.0.13-0ubuntu1" } };

        var result = await RunAsync(["install", "runtime", "10.0", "--scope", "global", "--yes", "--dry-run"], runner);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("pelo pacote dotnet-runtime-10.0=10.0.13-0ubuntu1 (apt", result.Output, StringComparison.Ordinal);
        Assert.Contains("gerenciado pelo pacote", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uninstall_PacoteAtualizadoPeloApt_CasaPelaVersaoInstalada()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Escopo global do Linux.");
        var runner = new FakeCommandRunner { Installed = { ["dotnet-runtime-10.0"] = "10.0.13-0ubuntu1" } };

        var result = await RunAsync(
            ["uninstall", "runtime", "10.0.13", "--scope", "global"],
            runner,
            prepare: layout => new InstallationManifestStore(layout.ManifestFile).AddAsync(
                new InstallationRecord(
                    ComponentKind.Runtime, "10.0.12", "linux-x64", InstallScope.Global, "/usr/lib/dotnet",
                    "dotnet-runtime-10.0=10.0.12-0ubuntu1", new Uri("apt:dotnet-runtime-10.0"), string.Empty, DateTimeOffset.UtcNow, [], [], false),
                new NullDiagnostics(),
                CancellationToken.None).GetAwaiter().GetResult());

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains(runner.Calls, c => c.Executable == AptPackages.AptGet && c.Arguments.SequenceEqual(["remove", "-y", "--", "dotnet-runtime-10.0"]));
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(string[] args, FakeCommandRunner runner, Action<GlobalLayout>? prepare = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var layout = new GlobalLayout(
            Path.Combine(_base, "share", "dotnet"),
            Path.Combine(_base, "var", "lib", "dotnetscope"),
            Path.Combine(_base, "var", "cache", "dotnetscope"),
            Path.Combine(_base, "etc", "profile.d", "dotnetscope.sh"),
            Path.Combine(_base, "etc", "dotnet", "install_location_x64"),
            [],
            []);
        prepare?.Invoke(layout);
        var installer = new InstallCommandTests.FakeInstaller();
        var services = new CliServices(output, error, (_, _) => new FixtureMetadataSource(), () => "linux-x64")
        {
            CreateInstaller = (_, _) => installer,
            CreateGlobalInstaller = (_, _, _) => installer,
            IsRoot = () => true,
            HomeDirectory = () => Path.Combine(_base, "home"),
            FindMissingDependencies = () => [],
            CreateGlobalLayout = _ => layout,
            StatPath = path => Path.Exists(path) ? Trusted : null,
            ProbeLoginShell = (_, _, _) => Task.FromResult<LoginShellResult?>(null),
            GetEnvironmentVariable = _ => null,
            UnsetEnvironmentVariable = _ => { },
            CommandRunner = runner,
        };

        var exitCode = await CliApplication.RunAsync(args, services, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
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

    internal sealed class FakeCommandRunner : ISystemCommandRunner
    {
        public HashSet<string> Existing { get; init; } = [AptPackages.AptGet, AptPackages.AptCache, AptPackages.DpkgQuery];

        public Dictionary<string, string> Policies { get; } = [];

        /// <summary>Pacotes instalados e sua versão Debian (dpkg-query).</summary>
        public Dictionary<string, string> Installed { get; } = [];

        /// <summary>Pacotes adicionais que a simulação de remoção informaria (dependentes).</summary>
        public List<string> Dependents { get; } = [];

        public List<(string Executable, string[] Arguments)> Calls { get; } = [];

        public bool Exists(string executable) => Existing.Contains(executable);

        public Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add((executable, arguments.ToArray()));
            if (executable == AptPackages.DpkgQuery)
            {
                return Task.FromResult(Installed.TryGetValue(arguments[^1], out var installed)
                    ? new CommandResult(0, $"install ok installed|{installed}", string.Empty)
                    : new CommandResult(1, string.Empty, "dpkg-query: no packages found"));
            }

            if (executable == AptPackages.AptGet && arguments[0] == "-s")
            {
                var removed = Dependents.Prepend(arguments[^1]).Select(p => $"Remv {p} [1.0]");
                return Task.FromResult(new CommandResult(0, string.Join('\n', removed) + "\n", string.Empty));
            }

            if (executable == AptPackages.AptCache)
            {
                var package = arguments[^1];
                var candidate = Policies.TryGetValue(package, out var value) ? value : "(none)";
                return Task.FromResult(new CommandResult(0, $"{package}:\n  Installed: (none)\n  Candidate: {candidate}\n", string.Empty));
            }

            return Task.FromResult(new CommandResult(0, string.Empty, string.Empty));
        }
    }
}
