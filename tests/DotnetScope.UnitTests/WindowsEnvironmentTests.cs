using System.Text.Json;
using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.UnitTests;

public sealed class WindowsEnvironmentTests : IDisposable
{
    private const string Root = @"C:\Users\teste\AppData\Local\Microsoft\dotnet";
    private const string Tools = @"C:\Users\teste\.dotnet\tools";
    private static readonly EnvironmentTarget Target = new(Root, Tools, TelemetryOptOut: false);

    private readonly string _state = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
    private readonly InMemoryEnvironmentStore _store = new();
    private readonly RecordingDiagnostics _diagnostics = new();

    private WindowsEnvironmentConfigurator Configurator => new(_store, _state, @"C:\Users\teste");

    public void Dispose()
    {
        if (Directory.Exists(_state))
        {
            Directory.Delete(_state, recursive: true);
        }
    }

    [Fact]
    public async Task Apply_SemValores_CriaDotnetRootEPathExpansivelEAvisa()
    {
        var result = await ApplyAsync(Target);

        Assert.Equal(new EnvironmentVariableValue(Root, Expandable: false), _store.User["DOTNET_ROOT"]);
        Assert.Equal(new EnvironmentVariableValue($@"{Root};%USERPROFILE%\.dotnet\tools", Expandable: true), _store.User["Path"]);
        Assert.Equal(1, _store.Broadcasts);
        Assert.Equal([@"HKCU\Environment\DOTNET_ROOT", @"HKCU\Environment\Path"], result.ChangedFiles);
        var backup = Assert.Single(result.Backups);
        Assert.Contains("\"name\": \"Path\"", await File.ReadAllTextAsync(backup, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(Root, Configurator.ConfiguredRoot());
    }

    [Fact]
    public async Task Apply_DuasVezes_EhIdempotente()
    {
        await ApplyAsync(Target);
        var plan = Configurator.PlanApply(Target, EnvConflictPolicy.Fail);

        Assert.False(plan.HasChanges);
    }

    [Fact]
    public async Task Apply_PreservaPathExistenteTipoEFerramentasJaPresentesPorVariavel()
    {
        _store.User["Path"] = new EnvironmentVariableValue(@"C:\bin;;%USERPROFILE%\.dotnet\tools\;C:\outro;", Expandable: false);

        await ApplyAsync(Target);

        Assert.Equal(new EnvironmentVariableValue($@"{Root};C:\bin;;%USERPROFILE%\.dotnet\tools\;C:\outro;", Expandable: false), _store.User["Path"]);
    }

    [Fact]
    public async Task Apply_PathRegSz_UsaFerramentasComCaminhoAbsoluto()
    {
        _store.User["Path"] = new EnvironmentVariableValue(@"C:\bin", Expandable: false);

        await ApplyAsync(Target);

        Assert.Equal(new EnvironmentVariableValue($@"{Root};{Tools};C:\bin", Expandable: false), _store.User["Path"]);
    }

    [Fact]
    public async Task Remove_RestauraValoresOriginaisExatamente()
    {
        var original = new EnvironmentVariableValue(@"C:\bin;;%USERPROFILE%\x;", Expandable: true);
        _store.User["Path"] = original;
        await ApplyAsync(Target with { TelemetryOptOut = true });

        var result = await Configurator.ExecuteAsync(Configurator.PlanRemove(), _diagnostics, TestContext.Current.CancellationToken);

        Assert.Equal(original, _store.User["Path"]);
        Assert.False(_store.User.ContainsKey("DOTNET_ROOT"));
        Assert.False(_store.User.ContainsKey("DOTNET_CLI_TELEMETRY_OPTOUT"));
        Assert.Contains(Path.Combine(_state, "env-state.json"), result.ChangedFiles);
        Assert.Null(Configurator.ConfiguredRoot());
        Assert.Equal(2, _store.Broadcasts);
    }

    [Fact]
    public async Task Remove_PathCriadoPeloDotnetScope_EhApagado()
    {
        await ApplyAsync(Target);

        await Configurator.ExecuteAsync(Configurator.PlanRemove(), _diagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(_store.User);
    }

    [Fact]
    public async Task Remove_PreservaAlteracoesFeitasPeloUsuarioDepois()
    {
        await ApplyAsync(Target);
        _store.User["Path"] = new EnvironmentVariableValue(_store.User["Path"].Value + @";C:\novo", Expandable: true);
        _store.User["DOTNET_ROOT"] = new EnvironmentVariableValue(@"D:\dotnet", Expandable: false);

        await Configurator.ExecuteAsync(Configurator.PlanRemove(), _diagnostics, TestContext.Current.CancellationToken);

        Assert.Equal(@"C:\novo", _store.User["Path"].Value);
        Assert.Equal(@"D:\dotnet", _store.User["DOTNET_ROOT"].Value);
    }

    [Fact]
    public void Conflito_DotnetRootDeTerceiro_FalhaSemAlterar()
    {
        _store.User["DOTNET_ROOT"] = new EnvironmentVariableValue(@"D:\dotnet", Expandable: false);

        var exception = Assert.Throws<DotnetScopeException>(() => Configurator.PlanApply(Target, EnvConflictPolicy.Fail));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
        Assert.Single(_store.User);
    }

    [Fact]
    public void Conflito_Skip_PlanoSemAlteracoes()
    {
        _store.User["DOTNET_ROOT"] = new EnvironmentVariableValue(@"D:\dotnet", Expandable: false);

        var plan = Configurator.PlanApply(Target, EnvConflictPolicy.Skip);

        Assert.True(plan.Skipped);
        Assert.False(plan.HasChanges);
        Assert.Equal(@"HKCU\Environment\DOTNET_ROOT", Assert.Single(plan.Conflicts).Location);
    }

    [Fact]
    public async Task Conflito_Override_RemoveRestauraValorDoTerceiro()
    {
        _store.User["DOTNET_ROOT"] = new EnvironmentVariableValue(@"%SystemDrive%\dotnet", Expandable: true);

        await ApplyAsync(Target, EnvConflictPolicy.Override);
        Assert.Equal(Root, _store.User["DOTNET_ROOT"].Value);
        await Configurator.ExecuteAsync(Configurator.PlanRemove(), _diagnostics, TestContext.Current.CancellationToken);

        Assert.Equal(new EnvironmentVariableValue(@"%SystemDrive%\dotnet", Expandable: true), _store.User["DOTNET_ROOT"]);
    }

    [Fact]
    public async Task Telemetria_AlteradaPeloUsuarioEntreAplicacoes_RemoveRestauraValorDoUsuario()
    {
        await ApplyAsync(Target with { TelemetryOptOut = true });
        _store.User["DOTNET_CLI_TELEMETRY_OPTOUT"] = new EnvironmentVariableValue("true", Expandable: false);

        await ApplyAsync(Target with { TelemetryOptOut = true });
        await Configurator.ExecuteAsync(Configurator.PlanRemove(), _diagnostics, TestContext.Current.CancellationToken);

        Assert.Equal("true", _store.User["DOTNET_CLI_TELEMETRY_OPTOUT"].Value);
    }

    [Fact]
    public async Task Override_DepoisDeTerceiroTrocarODotnetRoot_RemoveRestauraOTerceiro()
    {
        await ApplyAsync(Target);
        _store.User["DOTNET_ROOT"] = new EnvironmentVariableValue(@"D:\outra", Expandable: false);

        await ApplyAsync(Target, EnvConflictPolicy.Override);
        await Configurator.ExecuteAsync(Configurator.PlanRemove(), _diagnostics, TestContext.Current.CancellationToken);

        Assert.Equal(@"D:\outra", _store.User["DOTNET_ROOT"].Value);
    }

    [Fact]
    public async Task MesmoValorDeTerceiro_NaoEhConflitoNemEhRemovido()
    {
        _store.User["DOTNET_ROOT"] = new EnvironmentVariableValue(@"%USERPROFILE%\appdata\local\microsoft\DOTNET\", Expandable: true);

        await ApplyAsync(Target);
        await Configurator.ExecuteAsync(Configurator.PlanRemove(), _diagnostics, TestContext.Current.CancellationToken);

        Assert.Equal(@"%USERPROFILE%\appdata\local\microsoft\DOTNET\", _store.User["DOTNET_ROOT"].Value);
    }

    [Fact]
    public async Task ReaplicarComOutraRaiz_TrocaEntradaDoPathSemConflito()
    {
        await ApplyAsync(Target);
        const string other = @"C:\Users\teste\dotnet-alt";

        await ApplyAsync(Target with { DotnetRoot = other });

        Assert.Equal($@"{other};%USERPROFILE%\.dotnet\tools", _store.User["Path"].Value);
        Assert.Equal(other, _store.User["DOTNET_ROOT"].Value);
        await Configurator.ExecuteAsync(Configurator.PlanRemove(), _diagnostics, TestContext.Current.CancellationToken);
        Assert.Empty(_store.User);
    }

    [Fact]
    public async Task Telemetria_SemOptOut_RestauraValorAnterior()
    {
        _store.User["DOTNET_CLI_TELEMETRY_OPTOUT"] = new EnvironmentVariableValue("0", Expandable: false);
        await ApplyAsync(Target with { TelemetryOptOut = true });
        Assert.Equal("1", _store.User["DOTNET_CLI_TELEMETRY_OPTOUT"].Value);

        await ApplyAsync(Target);

        Assert.Equal("0", _store.User["DOTNET_CLI_TELEMETRY_OPTOUT"].Value);
    }

    [Fact]
    public async Task PlanoDesatualizado_FalhaSemGravar()
    {
        var plan = Configurator.PlanApply(Target, EnvConflictPolicy.Fail);
        _store.User["Path"] = new EnvironmentVariableValue(@"C:\concorrente", Expandable: true);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(
            () => Configurator.ExecuteAsync(plan, _diagnostics, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
        Assert.False(_store.User.ContainsKey("DOTNET_ROOT"));
        Assert.Equal(0, _store.Broadcasts);
    }

    [Fact]
    public async Task AvisoWmSettingChangeFalho_EmiteAviso()
    {
        _store.BroadcastResult = false;

        await ApplyAsync(Target);

        Assert.Contains(_diagnostics.Warnings, w => w.Contains("WM_SETTINGCHANGE", StringComparison.Ordinal));
    }

    [Fact]
    public void VerificarNovoTerminal_DotnetDaMaquinaTemPrecedencia()
    {
        _store.Machine["Path"] = @"C:\Windows;C:\Program Files\dotnet\";
        _store.User["Path"] = new EnvironmentVariableValue($"{Root};{Tools}", Expandable: true);
        _store.Files.Add(@"C:\Program Files\dotnet\dotnet.exe");
        _store.Files.Add(Root + @"\dotnet.exe");

        var (result, shadowed) = Configurator.VerifyNewProcess(Root);

        Assert.False(result.Matches);
        Assert.True(shadowed);
        Assert.Equal(@"C:\Program Files\dotnet\dotnet.exe", result.ResolvedDotnet);
    }

    [Fact]
    public void VerificarNovoTerminal_ResolveParaARaizDoUsuario()
    {
        _store.Machine["Path"] = @"C:\Windows";
        _store.User["Path"] = new EnvironmentVariableValue(@"%LOCALAPPDATA%\Microsoft\dotnet", Expandable: true);
        _store.Variables["LOCALAPPDATA"] = @"C:\Users\teste\AppData\Local";
        _store.Files.Add(Root + @"\dotnet.exe");

        var (result, shadowed) = Configurator.VerifyNewProcess(Root);

        Assert.True(result.Matches);
        Assert.False(shadowed);
    }

    [Fact]
    public async Task EstadoGravado_RegistraSomenteEntradasAcrescentadas()
    {
        _store.User["Path"] = new EnvironmentVariableValue(Tools, Expandable: true);

        await ApplyAsync(Target);

        using var state = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(_state, "env-state.json"), TestContext.Current.CancellationToken));
        Assert.Equal([Root], state.RootElement.GetProperty("pathEntries").EnumerateArray().Select(e => e.GetString()));
        Assert.False(state.RootElement.GetProperty("pathCreated").GetBoolean());
    }

    [Theory]
    [InlineData(@"C:\Users\o'brien\dotnet", @"'C:\Users\o''brien\dotnet'")]
    [InlineData(@"C:\a $env:X `b", @"'C:\a $env:X `b'")]
    [InlineData("C:\\a\u2019b", "'C:\\a\u2019\u2019b'")]
    public void PowerShell_ImpedeExpansao(string value, string expected) => Assert.Equal(expected, ShellQuoting.PowerShell(value));

    [Fact]
    public void RenderPowerShell_IdempotenteComTelemetria()
    {
        var script = EnvironmentScripts.RenderPowerShell(Target with { TelemetryOptOut = true });

        Assert.Contains($"$env:DOTNET_ROOT = '{Root}'", script, StringComparison.Ordinal);
        Assert.Contains(
            $"if (-not (($env:PATH -split [IO.Path]::PathSeparator) -contains '{Root}')) {{ $env:PATH = '{Root}' + [IO.Path]::PathSeparator + $env:PATH }}",
            script,
            StringComparison.Ordinal);
        Assert.Contains("$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\a%PATH%")]
    [InlineData(@"C:\a!b")]
    [InlineData(@"C:\a^b")]
    public void RenderCmd_RecusaCaracteresInseguros(string root)
    {
        var exception = Assert.Throws<DotnetScopeException>(() => EnvironmentScripts.RenderCmd(Target with { DotnetRoot = root }));

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
    }

    [Fact]
    public void RenderCmd_UsaCrlfEAspasNoSet()
    {
        var script = EnvironmentScripts.RenderCmd(Target);

        Assert.Contains($"set \"PATH={Root};{Tools};%PATH%\"\r\n", script, StringComparison.Ordinal);
    }

    private async Task<EnvironmentApplyResult> ApplyAsync(EnvironmentTarget target, EnvConflictPolicy policy = EnvConflictPolicy.Fail)
    {
        var configurator = Configurator;
        return await configurator.ExecuteAsync(configurator.PlanApply(target, policy), _diagnostics, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Apply_RegistroBloqueadoPorPolitica_FalhaComPrivilegioSemDeixarEstado()
    {
        _store.DenyWrites = true;
        var plan = Configurator.PlanApply(Target, EnvConflictPolicy.Fail);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(
            () => Configurator.ExecuteAsync(plan, _diagnostics, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Privilege, exception.ExitCode);
        Assert.Contains("Nada foi alterado no registro", exception.Message, StringComparison.Ordinal);
        Assert.Contains("--windows-env-target profile", exception.Message, StringComparison.Ordinal);
        Assert.Empty(_store.User);
        Assert.Null(Configurator.ConfiguredRoot());
    }

    [Fact]
    public async Task Apply_RegistroBloqueadoAposApplyAnterior_PreservaEstadoAnterior()
    {
        await ApplyAsync(Target);
        var state = await File.ReadAllBytesAsync(Configurator.StateFile, TestContext.Current.CancellationToken);
        _store.DenyWrites = true;
        var plan = Configurator.PlanApply(Target with { TelemetryOptOut = true }, EnvConflictPolicy.Fail);

        await Assert.ThrowsAsync<DotnetScopeException>(() => Configurator.ExecuteAsync(plan, _diagnostics, TestContext.Current.CancellationToken));

        Assert.Equal(state, await File.ReadAllBytesAsync(Configurator.StateFile, TestContext.Current.CancellationToken));
    }

    private sealed class RecordingDiagnostics : IDiagnosticSink
    {
        public List<string> Warnings { get; } = [];

        public void Warning(string message) => Warnings.Add(message);

        public void Verbose(string message)
        {
        }
    }
}

public sealed class WindowsPathListTests
{
    private static string Expand(string value) => value.Replace("%USERPROFILE%", @"C:\Users\u", StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData(null, @"C:\r;C:\t")]
    [InlineData("", @"C:\r;C:\t")]
    [InlineData(@"C:\a;;C:\b;", @"C:\r;C:\t;C:\a;;C:\b;")]
    [InlineData(@"c:\R\;C:\a", @"C:\t;c:\R\;C:\a")]
    [InlineData(@"""C:\t"";%userprofile%\x", @"C:\r;""C:\t"";%userprofile%\x")]
    public void Apply_AcrescentaSomenteAusentes(string? value, string expected) =>
        Assert.Equal(expected, WindowsPathList.Apply(value, [@"C:\r", @"C:\t"], [], Expand).Value);

    [Fact]
    public void Apply_RemoveEntradasProprias_ForaDoAlvo()
    {
        var (value, added) = WindowsPathList.Apply(@"C:\velho;C:\t;C:\a", [@"C:\r", @"C:\t"], [@"C:\velho", @"C:\t"], Expand);

        Assert.Equal(@"C:\r;C:\t;C:\a", value);
        Assert.Equal([@"C:\r"], added);
    }

    [Theory]
    [InlineData(@"C:\r;C:\t;C:\a;;C:\b;", @"C:\a;;C:\b;")]
    [InlineData(@"C:\r;C:\t", "")]
    [InlineData(@"C:\a;c:\R\;C:\b", @"C:\a;C:\b")]
    public void Remove_RetiraSomenteEntradasProprias(string value, string expected) =>
        Assert.Equal(expected, WindowsPathList.Remove(value, [@"C:\r", @"C:\t"], Expand));

    [Fact]
    public void Contains_ComparaExpandidoSemMaiusculasNemBarraFinal() =>
        Assert.True(WindowsPathList.Contains(@"C:\a;%USERPROFILE%\.DOTNET\tools\", @"C:\Users\u\.dotnet\tools", Expand));

    [Fact]
    public void Normalize_PreservaBarraDaRaizDaUnidade() =>
        Assert.Equal(@"C:\", WindowsPathList.Normalize(@"c:\", Expand));
}

public sealed class WindowsInstallLocationTests
{
    private const string Home = @"C:\Users\teste";
    private static readonly string[] Protected = [@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData"];

    [Theory]
    [InlineData(@"C:\Users\teste\AppData\Local\Microsoft\dotnet")]
    [InlineData(@"c:\users\TESTE\dotnet")]
    [InlineData(@"D:\dotnet")]
    [InlineData(@"C:\tools\dotnet")]
    public void Permitidos(string path) =>
        InstallLocations.EnsureAllowedWindows(path, path, Home, Protected, @"C:\Users");

    [Theory]
    [InlineData(@"C:\", ExitCodes.Privilege)]
    [InlineData(@"D:", ExitCodes.Privilege)]
    [InlineData(@"C:\Users\Teste", ExitCodes.Privilege)]
    [InlineData(@"C:\Windows\System32\dotnet", ExitCodes.Privilege)]
    [InlineData(@"c:\program files\dotnet", ExitCodes.Privilege)]
    [InlineData(@"C:\ProgramData\dotnet", ExitCodes.Privilege)]
    [InlineData(@"C:\Users\outro\dotnet", ExitCodes.Privilege)]
    [InlineData(@"C:\Users\Public\dotnet", ExitCodes.Privilege)]
    [InlineData(@"C:\Users\teste2\dotnet", ExitCodes.Privilege)]
    public void Recusados(string path, int code)
    {
        var exception = Assert.Throws<DotnetScopeException>(() => InstallLocations.EnsureAllowedWindows(path, path, Home, Protected, @"C:\Users"));

        Assert.Equal(code, exception.ExitCode);
    }
}
