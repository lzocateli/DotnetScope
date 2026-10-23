using System.Text;
using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.UnitTests;

public sealed class WindowsProfileEnvironmentTests : IDisposable
{
    private const string Root = @"C:\Users\teste\AppData\Local\Microsoft\dotnet";
    private const string Tools = @"C:\Users\teste\.dotnet\tools";
    private static readonly EnvironmentTarget Target = new(Root, Tools, TelemetryOptOut: false);

    private readonly string _base = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
    private readonly InMemoryEnvironmentStore _store = new();
    private readonly RecordingDiagnostics _diagnostics = new();

    public WindowsProfileEnvironmentTests() => Probe = FakePowerShellProbe.Create(Documents);

    private FakePowerShellProbe Probe { get; set; }

    private string State => Path.Combine(_base, "LocalAppData", "dotnetscope");

    private string Documents => Path.Combine(_base, "Documents");

    private string WindowsPowerShellProfile => Path.Combine(Documents, "WindowsPowerShell", "profile.ps1");

    private string PwshProfile => Path.Combine(Documents, "PowerShell", "profile.ps1");

    private WindowsProfileEnvironmentConfigurator Configurator => new(State, Probe, _store, @"C:\Users\teste");

    public void Dispose()
    {
        if (Directory.Exists(_base))
        {
            Directory.Delete(_base, recursive: true);
        }
    }

    [Fact]
    public async Task Apply_SemPerfis_CriaEnvPs1ComBomEBlocoNosDoisHostsSemTocarNoRegistro()
    {
        var result = await ApplyAsync(Target);

        var script = await File.ReadAllBytesAsync(Path.Combine(State, "env.ps1"), TestContext.Current.CancellationToken);
        Assert.Equal([0xEF, 0xBB, 0xBF], script[..3]);
        Assert.Contains($"$env:DOTNET_ROOT = '{Root}'", Encoding.UTF8.GetString(script), StringComparison.Ordinal);
        foreach (var profile in new[] { WindowsPowerShellProfile, PwshProfile })
        {
            var content = await File.ReadAllTextAsync(profile, TestContext.Current.CancellationToken);
            Assert.Equal(string.Join('\n', EnvironmentScripts.PowerShellProfileBlock()) + "\n", content);
        }

        Assert.Equal([Path.Combine(State, "env.ps1"), WindowsPowerShellProfile, PwshProfile], result.ChangedFiles);
        Assert.Empty(result.Backups);
        Assert.Empty(_store.User);
        Assert.Equal(0, _store.Broadcasts);
        Assert.Equal(Root, Configurator.ConfiguredRoot());
    }

    [Fact]
    public async Task Apply_DuasVezes_EhIdempotente()
    {
        await ApplyAsync(Target);

        Assert.False(Configurator.PlanApply(Target, EnvConflictPolicy.Fail).HasChanges);
    }

    [Fact]
    public void PlanApply_DuasVezesNaMesmaInstancia_DetectaHostsUmaVez()
    {
        var configurator = Configurator;

        configurator.PlanApply(Target, EnvConflictPolicy.Fail);
        configurator.PlanApply(Target, EnvConflictPolicy.Fail);

        Assert.Equal(1, Probe.DetectCalls);
    }

    [Theory]
    [InlineData("Restricted")]
    [InlineData("AllSigned")]
    [InlineData(null)]
    public async Task Apply_PoliticaDoWindowsPowerShellBloqueia_ConfiguraSoOPwshEAvisa(string? policy)
    {
        Probe = FakePowerShellProbe.Create(Documents, windowsPowerShellPolicy: policy);

        var plan = Configurator.PlanApply(Target, EnvConflictPolicy.Fail);
        await Configurator.ExecuteAsync(plan, _diagnostics, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(WindowsPowerShellProfile));
        Assert.True(File.Exists(PwshProfile));
        var warning = Assert.Single(plan.Warnings);
        Assert.Contains(policy ?? "desconhecida", warning, StringComparison.Ordinal);
        Assert.Contains(WindowsPowerShellProfile, warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_NenhumHostCarregaPerfis_FalhaSemGravar()
    {
        Probe = FakePowerShellProbe.Create(Documents, windowsPowerShellPolicy: "Restricted", pwshPolicy: "AllSigned");

        var exception = Assert.Throws<DotnetScopeException>(() => Configurator.PlanApply(Target, EnvConflictPolicy.Override));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
        Assert.Contains("--windows-env-target registry", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_base));
    }

    [Fact]
    public void Apply_SemPowerShell_FalhaSemGravar()
    {
        Probe = new FakePowerShellProbe(Documents);

        var exception = Assert.Throws<DotnetScopeException>(() => Configurator.PlanApply(Target, EnvConflictPolicy.Fail));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
        Assert.Contains("nenhum PowerShell foi encontrado", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_DotnetRootDiferenteNoRegistro_FalhaComConflito()
    {
        _store.User["DOTNET_ROOT"] = new EnvironmentVariableValue(@"D:\dotnet", Expandable: false);

        var exception = Assert.Throws<DotnetScopeException>(() => Configurator.PlanApply(Target, EnvConflictPolicy.Fail));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
        Assert.Contains(@"HKCU\Environment\DOTNET_ROOT", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_DotnetRootIgualNoRegistroComOutraCaixaEBarraFinal_NaoEhConflito()
    {
        _store.User["DOTNET_ROOT"] = new EnvironmentVariableValue(Root.ToUpperInvariant() + @"\", Expandable: false);

        var plan = Configurator.PlanApply(Target, EnvConflictPolicy.Fail);

        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void Apply_ConflitoComSkip_NaoAlteraNada()
    {
        _store.User["DOTNET_ROOT"] = new EnvironmentVariableValue(@"D:\dotnet", Expandable: false);

        var plan = Configurator.PlanApply(Target, EnvConflictPolicy.Skip);

        Assert.True(plan.Skipped);
        Assert.False(plan.HasChanges);
    }

    [Fact]
    public async Task Apply_DotnetRootEmPerfilDeTerceiro_ConflitoComLinhaEOverrideMoveOBlocoParaOFim()
    {
        var hostProfile = Path.Combine(Documents, "PowerShell", "Microsoft.PowerShell_profile.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(hostProfile)!);
        await File.WriteAllTextAsync(hostProfile, "# meu perfil\n$env:DOTNET_ROOT = 'D:\\dotnet'\n", TestContext.Current.CancellationToken);

        var exception = Assert.Throws<DotnetScopeException>(() => Configurator.PlanApply(Target, EnvConflictPolicy.Fail));
        var plan = Configurator.PlanApply(Target, EnvConflictPolicy.Override);

        Assert.Contains($"{hostProfile}:2", exception.Message, StringComparison.Ordinal);
        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(@"D:\dotnet", conflict.ResolvedValue);
        Assert.Contains(plan.Profiles, p => p.Path == PwshProfile);
    }

    [Fact]
    public async Task Remove_RestauraPerfisByteAByteEApagaOsCriados()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PwshProfile)!);
        byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Set-Alias ll Get-ChildItem\r\nImport-Module posh-git")];
        await File.WriteAllBytesAsync(PwshProfile, original, TestContext.Current.CancellationToken);
        await ApplyAsync(Target);

        var result = await Configurator.ExecuteAsync(Configurator.PlanRemove(), _diagnostics, TestContext.Current.CancellationToken);

        Assert.Equal(original, await File.ReadAllBytesAsync(PwshProfile, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(WindowsPowerShellProfile));
        Assert.False(File.Exists(Path.Combine(State, "env.ps1")));
        Assert.Null(Configurator.ConfiguredRoot());
        Assert.Contains(result.Backups, b => Path.GetFileName(b).StartsWith("PowerShell.profile.ps1.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_PerfilEmUtf16_FalhaComConflitoSemGravar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PwshProfile)!);
        await File.WriteAllBytesAsync(PwshProfile, Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Set-Alias ll ls\r\n")).ToArray(), TestContext.Current.CancellationToken);

        var exception = Assert.Throws<DotnetScopeException>(() => Configurator.PlanApply(Target, EnvConflictPolicy.Fail));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
        Assert.False(File.Exists(Path.Combine(State, "env.ps1")));
    }

    [Fact]
    public async Task Remove_PerfisDeTerceiroEmUtf16OuAnsi_SaoIgnoradosSemErro()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PwshProfile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(WindowsPowerShellProfile)!);
        await File.WriteAllBytesAsync(PwshProfile, [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("Set-Alias ll ls\r\n")], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(WindowsPowerShellProfile, [0x23, 0x20, 0xE3, 0x0D, 0x0A], TestContext.Current.CancellationToken);

        var plan = Configurator.PlanRemove();
        var verification = await Configurator.VerifyAsync(Root, TestContext.Current.CancellationToken);

        Assert.False(plan.HasChanges);
        Assert.Null(verification);
    }

    [Fact]
    public async Task Apply_DotnetRootEmPerfilUtf16DeTerceiro_EhDetectado()
    {
        var hostProfile = Path.Combine(Documents, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(hostProfile)!);
        await File.WriteAllTextAsync(hostProfile, "$env:DOTNET_ROOT = 'D:\\dotnet'\r\n", Encoding.Unicode, TestContext.Current.CancellationToken);

        var exception = Assert.Throws<DotnetScopeException>(() => Configurator.PlanApply(Target, EnvConflictPolicy.Fail));

        Assert.Contains($"{hostProfile}:1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_PrefereOPwshComBloco()
    {
        Probe.Verification = new LoginShellResult("pwsh", Root + @"\dotnet.exe", Root, Matches: true);
        await ApplyAsync(Target);

        var result = await Configurator.VerifyAsync(Root, TestContext.Current.CancellationToken);

        Assert.Equal("pwsh", Probe.Verified?.Name);
        Assert.True(result?.Matches);
    }

    [Fact]
    public async Task VerifyAsync_SemPerfilConfigurado_RetornaNulo()
    {
        var result = await Configurator.VerifyAsync(Root, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Null(Probe.Verified);
    }

    [Fact]
    public void SemPastaDocumentos_RemocaoFuncionaEApplyFalhaComOrientacao()
    {
        var configurator = new WindowsProfileEnvironmentConfigurator(State, new PowerShellHostProbe(null, _store), _store, @"C:\Users\teste");

        Assert.False(configurator.PlanRemove().HasChanges);
        Assert.Null(configurator.ConfiguredRoot());
        var exception = Assert.Throws<DotnetScopeException>(() => configurator.PlanApply(Target, EnvConflictPolicy.Fail));
        Assert.Contains("--windows-env-target registry", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerShellHost_SomentePoliticasQueCarregamPerfisLocais()
    {
        Assert.True(new PowerShellHost("pwsh", "x", "y", "remotesigned").RunsProfile);
        Assert.True(new PowerShellHost("pwsh", "x", "y", "Unrestricted").RunsProfile);
        Assert.True(new PowerShellHost("pwsh", "x", "y", "Bypass").RunsProfile);
        Assert.False(new PowerShellHost("pwsh", "x", "y", "Restricted").RunsProfile);
        Assert.False(new PowerShellHost("pwsh", "x", "y", "AllSigned").RunsProfile);
        Assert.False(new PowerShellHost("pwsh", "x", "y", "Undefined").RunsProfile);
        Assert.False(new PowerShellHost("pwsh", "x", "y", null).RunsProfile);
    }

    private async Task<EnvironmentApplyResult> ApplyAsync(EnvironmentTarget target) =>
        await Configurator.ExecuteAsync(Configurator.PlanApply(target, EnvConflictPolicy.Fail), _diagnostics, TestContext.Current.CancellationToken);

    private sealed class RecordingDiagnostics : IDiagnosticSink
    {
        public List<string> Warnings { get; } = [];

        public void Warning(string message) => Warnings.Add(message);

        public void Verbose(string message)
        {
        }
    }
}
