using System.Runtime.Versioning;
using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Infrastructure.Platform;
using DotnetScope.Infrastructure.SystemScope;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.IntegrationTests;

// Arquivos reais em diretório temporário; o stat é simulado como confiável, exceto nos testes do stat real.
[UnsupportedOSPlatform("windows")]
[Trait("Platform", "Unix")]
public sealed class GlobalEnvironmentTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
    private readonly GlobalLayout _layout;
    private readonly NullDiagnostics _diagnostics = new();

    public GlobalEnvironmentTests()
    {
        _layout = new GlobalLayout(
            Path.Combine(_base, "share", "dotnet"),
            Path.Combine(_base, "var", "lib", "dotnetscope"),
            Path.Combine(_base, "var", "cache", "dotnetscope"),
            Path.Combine(_base, "etc", "profile.d", "dotnetscope.sh"),
            Path.Combine(_base, "etc", "dotnet", "install_location_x64"),
            [Path.Combine(_base, "etc", "environment")],
            []);
        Directory.CreateDirectory(Path.Combine(_base, "etc"));
    }

    private SystemEnvironmentConfigurator Configurator =>
        new(_layout, path => Path.Exists(path) ? new UnixFileStatus(0, Convert.ToInt32("755", 8)) : null);

    private EnvironmentTarget Target => new(_layout.Root, string.Empty, TelemetryOptOut: false);

    public void Dispose()
    {
        if (Directory.Exists(_base))
        {
            Directory.Delete(_base, recursive: true);
        }
    }

    [Fact]
    public void StatReal_LeDonoEModo()
    {
        var root = UnixFileOwnership.Stat("/");
        var temp = UnixFileOwnership.Stat("/tmp");

        Assert.Equal(0, root!.Uid);
        Assert.NotNull(temp);
        Assert.Null(UnixFileOwnership.Stat(Path.Combine(_base, "inexistente")));
    }

    [Fact]
    public void StatReal_DiretorioTemporarioGravavelPorTodos_NaoEhConfiavel()
    {
        Assert.SkipUnless(((UnixFileOwnership.Stat("/tmp")?.Mode ?? 0) & 0b010) != 0, "/tmp não é gravável por outros neste ambiente.");

        var exception = Assert.Throws<DotnetScopeException>(() => GlobalLocations.EnsureTrusted("/tmp/dotnetscope-global", UnixFileOwnership.Stat, "teste"));

        Assert.Equal(ExitCodes.Privilege, exception.ExitCode);
    }

    [Fact]
    public async Task Apply_GravaArquivosDoRootComModo0644EBackup()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_layout.InstallLocationFile)!);
        await File.WriteAllTextAsync(_layout.InstallLocationFile, _layout.Root + "\n", TestContext.Current.CancellationToken);

        var result = await ApplyAsync();

        Assert.Equal([_layout.ProfileScript], result.ChangedFiles);
        Assert.Empty(result.Backups);
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
            File.GetUnixFileMode(_layout.ProfileScript));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(_layout.StateDirectory, "env-state.json")));
        Assert.False(Configurator.PlanApply(Target, EnvConflictPolicy.Fail).HasChanges);
    }

    [Fact]
    public async Task Remove_RestauraConteudoAnteriorExato()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_layout.InstallLocationFile)!);
        const string previous = "/opt/anterior\n";
        await File.WriteAllTextAsync(_layout.InstallLocationFile, previous, TestContext.Current.CancellationToken);

        var applied = await ApplyAsync(EnvConflictPolicy.Override);
        Assert.Single(applied.Backups);
        var removed = await Configurator.ExecuteAsync(Configurator.PlanRemove(), _diagnostics, TestContext.Current.CancellationToken);

        Assert.Equal(previous, await File.ReadAllTextAsync(_layout.InstallLocationFile, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(_layout.ProfileScript));
        Assert.Contains(Path.Combine(_layout.StateDirectory, "env-state.json"), removed.ChangedFiles);
        Assert.Null(Configurator.ConfiguredRoot());
    }

    [Fact]
    public async Task Remove_ArquivoAlteradoDepoisPorTerceiro_NaoEhTocado()
    {
        await ApplyAsync();
        await File.WriteAllTextAsync(_layout.InstallLocationFile, "/opt/terceiro\n", TestContext.Current.CancellationToken);

        await Configurator.ExecuteAsync(Configurator.PlanRemove(), _diagnostics, TestContext.Current.CancellationToken);

        Assert.Equal("/opt/terceiro\n", await File.ReadAllTextAsync(_layout.InstallLocationFile, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(_layout.ProfileScript));
    }

    [Fact]
    public async Task PlanoDesatualizado_NadaEhGravado()
    {
        var plan = Configurator.PlanApply(Target, EnvConflictPolicy.Fail);
        Directory.CreateDirectory(Path.GetDirectoryName(_layout.ProfileScript)!);
        await File.WriteAllTextAsync(_layout.ProfileScript, "# concorrente\n", TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(
            () => Configurator.ExecuteAsync(plan, _diagnostics, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
        Assert.False(File.Exists(_layout.InstallLocationFile));
    }

    [Fact]
    public void LinkSimbolicoNoArquivoDoSistema_EhRecusado()
    {
        var victim = Path.Combine(_base, "vitima");
        File.WriteAllText(victim, "original");
        Directory.CreateDirectory(Path.GetDirectoryName(_layout.InstallLocationFile)!);
        File.CreateSymbolicLink(_layout.InstallLocationFile, victim);

        var exception = Assert.Throws<DotnetScopeException>(() => Configurator.PlanApply(Target, EnvConflictPolicy.Override));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
        Assert.Equal("original", File.ReadAllText(victim));
    }

    [Fact]
    public async Task DiretorioNaoConfiavel_NadaEhGravado()
    {
        var configurator = new SystemEnvironmentConfigurator(
            _layout,
            path => path.EndsWith("profile.d", StringComparison.Ordinal) ? new UnixFileStatus(1000, Convert.ToInt32("755", 8))
                : Path.Exists(path) ? new UnixFileStatus(0, Convert.ToInt32("755", 8)) : null);
        Directory.CreateDirectory(Path.GetDirectoryName(_layout.ProfileScript)!);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(
            () => configurator.ExecuteAsync(configurator.PlanApply(Target, EnvConflictPolicy.Fail), _diagnostics, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Privilege, exception.ExitCode);
        Assert.False(File.Exists(_layout.ProfileScript));
    }

    [Fact]
    public async Task Verificacao_UsaSomenteOScriptDoSistemaComAmbienteFixo()
    {
        Directory.CreateDirectory(_layout.Root);
        await File.WriteAllTextAsync(Path.Combine(_layout.Root, "dotnet"), "#!/bin/sh\n", TestContext.Current.CancellationToken);
        File.SetUnixFileMode(Path.Combine(_layout.Root, "dotnet"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await ApplyAsync();

        var result = await Configurator.VerifyAsync(_layout.Root, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Matches);
        Assert.Equal(Path.Combine(_layout.Root, "dotnet"), result.ResolvedDotnet);
        Assert.Equal(_layout.Root, result.DotnetRoot);
    }

    [Fact]
    public void ArquivoDeEstadoComoLink_EhRecusado()
    {
        Directory.CreateDirectory(_layout.StateDirectory);
        var link = Path.Combine(_layout.StateDirectory, "manifest.json");
        File.CreateSymbolicLink(link, "/etc/passwd");

        var exception = Assert.Throws<DotnetScopeException>(() => GlobalLocations.EnsureTrustedFile(link, UnixFileOwnership.Stat));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
    }

    private async Task<EnvironmentApplyResult> ApplyAsync(EnvConflictPolicy policy = EnvConflictPolicy.Fail)
    {
        var configurator = Configurator;
        return await configurator.ExecuteAsync(configurator.PlanApply(Target, policy), _diagnostics, TestContext.Current.CancellationToken);
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
