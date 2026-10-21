using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using DotnetScope.Cli;
using DotnetScope.Core;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.IntegrationTests;

/// <summary>
/// Configuração de ambiente com perfis reais em um home temporário, scripts executados por sh/bash reais e a CLI
/// em processo. Nunca toca no perfil de quem executa os testes.
/// </summary>
// Usa host de teste em shell script, modos Unix e /bin/sh; filtrado no Windows por Platform=Unix.
[UnsupportedOSPlatform("windows")]
[Trait("Platform", "Unix")]
public sealed class UserEnvironmentTests : IDisposable
{
    private const string OriginalBashrc = "# bashrc do usuário\nalias ll='ls -l'";
    private readonly string _home = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _systemFiles = [];
    private bool _interactive;
    private bool _answer;

    public UserEnvironmentTests()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "dotnet"), "#!/bin/sh\necho host\n");
        File.SetUnixFileMode(Path.Combine(Root, "dotnet"), (UnixFileMode)0b111_101_101);
        File.WriteAllText(Bashrc, OriginalBashrc);
        File.SetUnixFileMode(Bashrc, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private string Root => Path.Combine(_home, ".dotnet");

    private string Bashrc => Path.Combine(_home, ".bashrc");

    private string ProfilePath => Path.Combine(_home, ".profile");

    private string EnvScript => Path.Combine(_home, ".config/dotnetscope/env.sh");

    [Fact]
    public async Task Apply_CriaProfileEditaBashrcENaoCriaPerfisAusentes()
    {
        var result = await RunAsync("env", "apply");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.True(File.Exists(EnvScript));
        Assert.StartsWith(OriginalBashrc + "\n# >>> dotnetscope >>>", File.ReadAllText(Bashrc), StringComparison.Ordinal);
        Assert.Contains("# >>> dotnetscope >>>", File.ReadAllText(ProfilePath), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_home, ".zshrc")));
        Assert.False(File.Exists(Path.Combine(_home, ".bash_profile")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Bashrc));
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(_home, ".local/state/dotnetscope/backups")));
        Assert.Equal(OriginalBashrc, File.ReadAllText(backup));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(backup));
    }

    [Fact]
    public async Task Apply_DuasVezes_ArquivosIdenticosSemNovosBackups()
    {
        await RunAsync("env", "apply");
        var before = Hashes();
        var backups = Directory.GetFiles(Path.Combine(_home, ".local/state/dotnetscope/backups")).Length;

        var second = await RunAsync("env", "apply");

        Assert.Equal(ExitCodes.Success, second.ExitCode);
        Assert.Contains("já configurado", second.Output, StringComparison.Ordinal);
        Assert.Equal(before, Hashes());
        Assert.Equal(backups, Directory.GetFiles(Path.Combine(_home, ".local/state/dotnetscope/backups")).Length);
    }

    [Fact]
    public async Task Remove_RestauraPerfisByteAByteERemoveCriados()
    {
        await RunAsync("env", "apply");

        var result = await RunAsync("env", "remove");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Equal(OriginalBashrc, File.ReadAllText(Bashrc));
        Assert.False(File.Exists(ProfilePath));
        Assert.False(File.Exists(EnvScript));
        Assert.Contains("Nenhuma configuração", (await RunAsync("env", "remove")).Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScriptGerado_ComSh_DefineValoresSemExecutarComandos()
    {
        var root = Path.Combine(_home, "dotnet 'x' $(touch pwned) `touch pwned2`");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "dotnet"), "#!/bin/sh\n");
        await RunAsync("env", "apply", "--install-dir", root);

        var output = await ShellAsync("/bin/sh", $". '{EnvScript}'; . '{EnvScript}'; printf '%s\\n%s' \"$DOTNET_ROOT\" \"$PATH\"");

        var lines = output.Split('\n');
        Assert.Equal(root, lines[0]);
        Assert.StartsWith($"{root}:{Path.Combine(_home, ".dotnet/tools")}:/usr/bin", lines[1], StringComparison.Ordinal);
        Assert.Single(lines[1].Split(':'), entry => entry == root);
        Assert.False(File.Exists(Path.Combine(_home, "pwned")));
        Assert.False(File.Exists(Path.Combine(_home, "pwned2")));
    }

    [Fact]
    public async Task ShellDeLoginNovo_ResolveDotnetParaARaiz()
    {
        Assert.SkipUnless(File.Exists("/bin/bash"), "bash indisponível.");
        await RunAsync("env", "apply");

        var probe = await LoginShellProbe.ProbeAsync("/bin/bash", _home, TestContext.Current.CancellationToken);

        Assert.NotNull(probe);
        Assert.Equal(Path.Combine(Root, "dotnet"), probe.ResolvedDotnet);
        Assert.Equal(Root, probe.DotnetRoot);
    }

    [Fact]
    public async Task ShellDeLogin_PerfilComProcessoEmSegundoPlano_NaoTrava()
    {
        Assert.SkipUnless(File.Exists("/bin/bash"), "bash indisponível.");
        File.WriteAllText(ProfilePath, "sleep 30 &\n");

        var started = DateTime.UtcNow;
        var probe = await LoginShellProbe.ProbeAsync("/bin/bash", _home, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.NotNull(probe);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task PerfilAlteradoDepoisDoPlano_NadaEhGravado()
    {
        var configurator = new UnixEnvironmentConfigurator(Layout());
        var plan = configurator.PlanApply(new Core.UserEnvironment.EnvironmentTarget(Root, Path.Combine(_home, ".dotnet/tools"), false), Core.UserEnvironment.EnvConflictPolicy.Fail);
        File.AppendAllText(Bashrc, "\nexport EDITADO=1\n");
        var before = Hashes();

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() =>
            configurator.ExecuteAsync(plan, Core.NullDiagnosticSink.Instance, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
        Assert.Equal(before, Hashes());
    }

    [Fact]
    public async Task LinkQuebradoEmArquivoDoSistema_EhIgnorado()
    {
        var broken = Path.Combine(_home, "quebrado.sh");
        File.CreateSymbolicLink(broken, Path.Combine(_home, "inexistente.sh"));
        _systemFiles.Add(broken);

        var result = await RunAsync("env", "apply");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
    }

    [Fact]
    public async Task DryRunComPerfilLinkSimbolico_NaoExigeConfirmacao()
    {
        var target = Path.Combine(_home, "bashrc-real");
        File.Move(Bashrc, target);
        File.CreateSymbolicLink(Bashrc, target);

        var result = await RunAsync("env", "apply", "--dry-run");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.DoesNotContain("# >>> dotnetscope >>>", File.ReadAllText(target), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PerfilCrlf_BlocoContinuaInterpretavelPeloShell()
    {
        File.WriteAllText(ProfilePath, "export A=1\r\n");
        await RunAsync("env", "apply");

        var output = await ShellAsync("/bin/sh", $". '{ProfilePath}'; printf '%s' \"$DOTNET_ROOT\"");

        Assert.Equal(Root, output);
    }

    [Fact]
    public async Task ConflitoAnsibleComOutroValor_FalhaSemAlterarNada()
    {
        File.WriteAllText(ProfilePath, "# BEGIN ANSIBLE USER DOTNET\nexport DOTNET_ROOT=/opt/outro\n# END ANSIBLE USER DOTNET\n");
        var before = Hashes();

        var result = await RunAsync("env", "apply");

        Assert.Equal(ExitCodes.Conflict, result.ExitCode);
        Assert.Contains(".profile:2 DOTNET_ROOT=/opt/outro", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, Hashes());
        Assert.False(File.Exists(EnvScript));
    }

    [Fact]
    public async Task ConflitoAnsibleComMesmoValor_NaoEhConflito()
    {
        File.WriteAllText(ProfilePath, $"# BEGIN ANSIBLE USER DOTNET\nexport DOTNET_ROOT={Root}\n# END ANSIBLE USER DOTNET\n");

        var result = await RunAsync("env", "apply");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("# END ANSIBLE USER DOTNET\n# >>> dotnetscope >>>", File.ReadAllText(ProfilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConflitoEmArquivoDoSistema_TambemEhDetectado()
    {
        var system = Path.Combine(_home, "profile.d-dotnet.sh");
        File.WriteAllText(system, "export DOTNET_ROOT=/usr/lib/dotnet\n");
        _systemFiles.Add(system);

        var result = await RunAsync("env", "apply");

        Assert.Equal(ExitCodes.Conflict, result.ExitCode);
    }

    [Fact]
    public async Task ConflitoComOverride_AplicaBlocoDepoisDoTerceiro()
    {
        File.WriteAllText(Bashrc, "export DOTNET_ROOT=/opt/outro\n");

        var result = await RunAsync("env", "apply", "--env-conflict", "override", "--output", "json");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.StartsWith("export DOTNET_ROOT=/opt/outro\n# >>> dotnetscope >>>", File.ReadAllText(Bashrc), StringComparison.Ordinal);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("/opt/outro", json.RootElement.GetProperty("environment").GetProperty("conflicts")[0].GetProperty("value").GetString());
        var output = await ShellAsync("/bin/sh", $". '{Bashrc}'; printf '%s' \"$DOTNET_ROOT\"");
        Assert.Equal(Root, output);
    }

    [Fact]
    public async Task ConflitoComSkip_NaoAlteraNadaEAvisa()
    {
        File.WriteAllText(Bashrc, "export DOTNET_ROOT=/opt/outro\n");
        var before = Hashes();

        var result = await RunAsync("env", "apply", "--env-conflict", "skip");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("--env-conflict skip", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, Hashes());
    }

    [Theory]
    [InlineData(false, false, ExitCodes.Usage)]
    [InlineData(true, false, ExitCodes.Canceled)]
    [InlineData(true, true, ExitCodes.Success)]
    public async Task PerfilLinkSimbolico_ExigeConfirmacaoEEditaOAlvo(bool interactive, bool answer, int expected)
    {
        var dotfiles = Path.Combine(_home, "dotfiles");
        Directory.CreateDirectory(dotfiles);
        var target = Path.Combine(dotfiles, "bashrc");
        File.Move(Bashrc, target);
        File.CreateSymbolicLink(Bashrc, target);
        _interactive = interactive;
        _answer = answer;

        var result = await RunAsync("env", "apply");

        Assert.Equal(expected, result.ExitCode);
        Assert.NotNull(new FileInfo(Bashrc).LinkTarget);
        Assert.Equal(expected == ExitCodes.Success, File.ReadAllText(target).Contains("# >>> dotnetscope >>>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PerfilLinkSimbolico_ComYes_NaoPergunta()
    {
        var target = Path.Combine(_home, "bashrc-real");
        File.Move(Bashrc, target);
        File.CreateSymbolicLink(Bashrc, target);

        var result = await RunAsync("env", "apply", "--yes");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("link simbólico", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PerfilNaoUtf8_NaoEhEditado()
    {
        File.WriteAllBytes(Bashrc, [0x65, 0x78, 0xFF, 0xFE, 0x0A]);

        var result = await RunAsync("env", "apply");

        Assert.Equal(ExitCodes.Conflict, result.ExitCode);
        Assert.Equal([0x65, 0x78, 0xFF, 0xFE, 0x0A], File.ReadAllBytes(Bashrc));
    }

    [Fact]
    public async Task BlocoMalformado_Conflito()
    {
        File.WriteAllText(Bashrc, "# >>> dotnetscope >>>\nexport X=1\n");

        var result = await RunAsync("env", "apply");

        Assert.Equal(ExitCodes.Conflict, result.ExitCode);
        Assert.Contains("malformado", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_SemDotnetNaRaiz_NaoEncontrado()
    {
        var result = await RunAsync("env", "apply", "--install-dir", Path.Combine(_home, "vazio"));

        Assert.Equal(ExitCodes.NotFound, result.ExitCode);
    }

    [Fact]
    public async Task Show_ImprimeScriptPosixOuFish()
    {
        var posix = await RunAsync("env", "show");
        var fish = await RunAsync("env", "show", "--shell", "fish");

        Assert.Contains($"export DOTNET_ROOT='{Root}'", posix.Output, StringComparison.Ordinal);
        Assert.Contains($"set -gx DOTNET_ROOT '{Root}'", fish.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(EnvScript));
    }

    [Fact]
    public async Task Fish_ScriptConfDEhAplicadoQuandoFishExiste()
    {
        Assert.SkipUnless(File.Exists("/usr/bin/fish"), "fish indisponível.");
        Directory.CreateDirectory(Path.Combine(_home, ".config/fish"));

        await RunAsync("env", "apply");

        var output = await ShellAsync("/usr/bin/fish", "printf '%s' $DOTNET_ROOT", login: true);
        Assert.Equal(Root, output);
    }

    [Fact]
    public async Task DryRun_NaoGravaNada()
    {
        var before = Hashes();

        var result = await RunAsync("env", "apply", "--dry-run");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("Seriam alterados", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, Hashes());
        Assert.False(File.Exists(ProfilePath));
    }

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private Dictionary<string, string> Hashes() => Directory
        .EnumerateFiles(_home, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
        .Where(path => !path.Contains("/.local/state/", StringComparison.Ordinal))
        .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

    private async Task<string> ShellAsync(string shell, string script, bool login = false)
    {
        var startInfo = new ProcessStartInfo(shell) { RedirectStandardOutput = true, UseShellExecute = false };
        if (login)
        {
            startInfo.ArgumentList.Add("-l");
        }

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(script);
        startInfo.Environment.Clear();
        startInfo.Environment["HOME"] = _home;
        startInfo.Environment["PATH"] = "/usr/bin:/bin";
        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return output;
    }

    private EnvironmentLayout Layout() => new(
        _home,
        Path.Combine(_home, ".config"),
        Path.Combine(_home, ".local", "state"),
        IsMacOS: false,
        FishAvailable: Directory.Exists(Path.Combine(_home, ".config", "fish")),
        SystemFiles: _systemFiles);

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var services = new CliServices(output, error, (_, _) => throw new InvalidOperationException(), () => "linux-x64")
        {
            HomeDirectory = () => _home,
            CreateEnvironmentLayout = _ => Layout(),
            LoginShell = () => null,
            Prompt = new Prompt(this),
        };

        var exitCode = await CliApplication.RunAsync(args, services, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private sealed class Prompt(UserEnvironmentTests owner) : IUserPrompt
    {
        public bool IsInteractive => owner._interactive;

        public bool Confirm(string question) => owner._answer;
    }
}
