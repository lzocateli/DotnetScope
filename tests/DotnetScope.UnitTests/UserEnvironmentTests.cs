using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;

namespace DotnetScope.UnitTests;

public sealed class UserEnvironmentTests
{
    private static readonly IReadOnlyList<string> Block = EnvironmentScripts.ProfileBlock("\"$HOME\"/'.config/dotnetscope/env.sh'");

    [Theory]
    [InlineData(false, "/bin/bash", false, true)]
    [InlineData(true, "/bin/zsh", false, false)]
    [InlineData(true, "/bin/bash", false, true)]
    [InlineData(true, "/bin/bash", true, false)]
    public void Layout_ProfileCriavelNoMacOSSomenteParaBashSemPerfilProprio(bool macOS, string shell, bool hasBashProfile, bool expected)
    {
        var home = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        try
        {
            if (hasBashProfile)
            {
                File.WriteAllText(Path.Combine(home, ".bash_profile"), string.Empty);
            }

            var layout = Infrastructure.UserEnvironment.EnvironmentLayout.Create(home, name => name == "SHELL" ? shell : null, macOS, "/usr/bin");

            Assert.Equal(expected, layout.Profiles.Single(p => p.Path == Path.Combine(home, ".profile")).Creatable);
            Assert.Equal(macOS, layout.Profiles.Single(p => p.Path == Path.Combine(home, ".zprofile")).Creatable);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Theory]
    [InlineData("/home/u/.dotnet", "'/home/u/.dotnet'")]
    [InlineData("/home/o'brien", "'/home/o'\\''brien'")]
    [InlineData("/tmp/$(id) `id` \"x\"", "'/tmp/$(id) `id` \"x\"'")]
    public void Posix_ImpedeExpansao(string value, string expected) => Assert.Equal(expected, ShellQuoting.Posix(value));

    [Theory]
    [InlineData("/home/u/.dotnet", "'/home/u/.dotnet'")]
    [InlineData("/home/o'brien", "'/home/o\\'brien'")]
    [InlineData("/tmp/a\\b", "'/tmp/a\\\\b'")]
    public void Fish_ImpedeExpansao(string value, string expected) => Assert.Equal(expected, ShellQuoting.Fish(value));

    [Fact]
    public void RenderPosix_DefineRootEPathComGuardas()
    {
        var script = EnvironmentScripts.RenderPosix(new EnvironmentTarget("/home/u/.dotnet", "/home/u/.dotnet/tools", TelemetryOptOut: false));

        Assert.Contains("export DOTNET_ROOT='/home/u/.dotnet'", script, StringComparison.Ordinal);
        Assert.Contains("  *:'/home/u/.dotnet':*) ;;", script, StringComparison.Ordinal);
        Assert.Contains("  *) export PATH='/home/u/.dotnet/tools':\"${PATH}\" ;;", script, StringComparison.Ordinal);
        Assert.DoesNotContain("TELEMETRY", script, StringComparison.Ordinal);
        Assert.EndsWith("\n", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_TelemetryOptOut_IncluiVariavel()
    {
        var target = new EnvironmentTarget("/r", "/t", TelemetryOptOut: true);

        Assert.Contains("export DOTNET_CLI_TELEMETRY_OPTOUT=1", EnvironmentScripts.RenderPosix(target), StringComparison.Ordinal);
        Assert.Contains("set -gx DOTNET_CLI_TELEMETRY_OPTOUT 1", EnvironmentScripts.RenderFish(target), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/home/u/.config/dotnetscope/env.sh", "/home/u", "\"$HOME\"/'.config/dotnetscope/env.sh'")]
    [InlineData("/home/u/.config/dotnetscope/env.sh", "/home/u/", "\"$HOME\"/'.config/dotnetscope/env.sh'")]
    [InlineData("/xdg/dotnetscope/env.sh", "/home/u", "'/xdg/dotnetscope/env.sh'")]
    [InlineData("/home/user2/env.sh", "/home/u", "'/home/user2/env.sh'")]
    public void ScriptExpression_RelativaAoHomeQuandoPossivel(string path, string home, string expected) =>
        Assert.Equal(expected, EnvironmentScripts.ScriptExpression(path, home));

    [Theory]
    [InlineData("")]
    [InlineData("export A=1\n")]
    [InlineData("export A=1")]
    [InlineData("export A=1\r\nexport B=2\r\n")]
    public void Upsert_DuasVezes_EhIdempotenteERemoveRestauraBytes(string original)
    {
        var first = ProfileBlockEditor.Upsert(original, Block, "perfil");
        var second = ProfileBlockEditor.Upsert(first.Content, Block, "perfil");
        var removed = ProfileBlockEditor.Remove(second.Content, "perfil", first.AddedLeadingNewline);

        Assert.True(first.Changed);
        Assert.False(second.Changed);
        Assert.Equal(first.Content, second.Content);
        Assert.Equal(original, removed.Content);
        Assert.StartsWith(original, first.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Upsert_ArquivoCrlf_BlocoUsaLfParaSerInterpretavel()
    {
        var edit = ProfileBlockEditor.Upsert("a=1\r\n", Block, "perfil");
        var withoutFinalNewline = ProfileBlockEditor.Upsert("a=1\r\nb=2", Block, "perfil");

        Assert.StartsWith("a=1\r\n" + EnvironmentScripts.BeginMarker + "\n", edit.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", edit.Content[5..], StringComparison.Ordinal);
        Assert.StartsWith("a=1\r\nb=2\r\n" + EnvironmentScripts.BeginMarker, withoutFinalNewline.Content, StringComparison.Ordinal);
        Assert.Equal("a=1\r\nb=2", ProfileBlockEditor.Remove(withoutFinalNewline.Content, "perfil", removeLeadingNewline: true).Content);
    }

    [Fact]
    public void Upsert_BlocoDiferente_SubstituiNoLugarSemTocarNoRestante()
    {
        var original = "antes\n" + string.Join('\n', EnvironmentScripts.ProfileBlock("'/velho'")) + "\ndepois\n";

        var edit = ProfileBlockEditor.Upsert(original, Block, "perfil");

        Assert.StartsWith("antes\n" + EnvironmentScripts.BeginMarker, edit.Content, StringComparison.Ordinal);
        Assert.EndsWith(EnvironmentScripts.EndMarker + "\ndepois\n", edit.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("velho", edit.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Upsert_MoveToEnd_LevaBlocoParaOFim()
    {
        var original = string.Join('\n', Block) + "\nexport DOTNET_ROOT=/outro\n";

        var edit = ProfileBlockEditor.Upsert(original, Block, "perfil", moveToEnd: true);

        Assert.StartsWith("export DOTNET_ROOT=/outro\n" + EnvironmentScripts.BeginMarker, edit.Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("# >>> dotnetscope >>>\n")]
    [InlineData("# <<< dotnetscope <<<\n")]
    [InlineData("# <<< dotnetscope <<<\n# >>> dotnetscope >>>\n")]
    [InlineData("# >>> dotnetscope >>>\n# <<< dotnetscope <<<\n# >>> dotnetscope >>>\n# <<< dotnetscope <<<\n")]
    public void Find_BlocoMalformado_Conflito(string content)
    {
        var exception = Assert.Throws<DotnetScopeException>(() => ProfileBlockEditor.Upsert(content, Block, "perfil"));
        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
    }

    [Theory]
    [InlineData("export DOTNET_ROOT=/home/u/.dotnet", "/home/u/.dotnet")]
    [InlineData("export DOTNET_ROOT=/home/u/.dotnet/", "/home/u/.dotnet")]
    [InlineData("DOTNET_ROOT=\"$HOME/.dotnet\"", "/home/u/.dotnet")]
    [InlineData("export DOTNET_ROOT=${HOME}/.dotnet", "/home/u/.dotnet")]
    [InlineData("export DOTNET_ROOT='/opt/dotnet'", "/opt/dotnet")]
    [InlineData("export DOTNET_ROOT=~/.dotnet # comentário", "/home/u/.dotnet")]
    [InlineData("  export DOTNET_ROOT=/usr/lib/dotnet;", "/usr/lib/dotnet")]
    [InlineData("set -gx DOTNET_ROOT /home/u/.dotnet", "/home/u/.dotnet")]
    [InlineData("export DOTNET_ROOT=$(dirname $(which dotnet))", null)]
    [InlineData("export DOTNET_ROOT=${DOTNET_ROOT:-/x}", null)]
    public void FindAssignments_AvaliaValoresLiteraisEHome(string line, string? expected)
    {
        var assignment = Assert.Single(ThirdPartyEnvironment.FindAssignments("perfil", line + "\n", "/home/u"));

        Assert.Equal(expected, assignment.ResolvedValue);
        Assert.Equal(1, assignment.Line);
    }

    [Fact]
    public void FindAssignments_IgnoraComentariosEOProprioBloco()
    {
        var content = "# export DOTNET_ROOT=/x\n" + string.Join('\n', Block) + "\nexport DOTNET_ROOT_X=1\n";

        Assert.Empty(ThirdPartyEnvironment.FindAssignments("perfil", content, "/home/u"));
    }

    [Fact]
    public void FindAssignments_BlocoAnsibleReal_EncontraLinha()
    {
        const string ansible = "# BEGIN ANSIBLE USER DOTNET\nexport DOTNET_ROOT=/home/u/.dotnet\ncase \":${PATH}:\" in\n  *\":${DOTNET_ROOT}:\"*) ;;\n  *) export PATH=\"${DOTNET_ROOT}:${PATH}\" ;;\nesac\n# END ANSIBLE USER DOTNET\n";

        var assignment = Assert.Single(ThirdPartyEnvironment.FindAssignments("~/.profile", "fi\n" + ansible, "/home/u"));

        Assert.Equal(3, assignment.Line);
        Assert.Equal("/home/u/.dotnet", assignment.ResolvedValue);
    }

    [Fact]
    public void PowerShellProfileBlock_SomenteAsciiSemCaminhoLiteralEComMarcadores()
    {
        var block = EnvironmentScripts.PowerShellProfileBlock();

        Assert.Equal(EnvironmentScripts.BeginMarker, block[0]);
        Assert.Equal(EnvironmentScripts.EndMarker, block[^1]);
        Assert.All(block, line => Assert.True(line.All(char.IsAscii), line));
        Assert.Contains(block, line => line.Contains("$env:LOCALAPPDATA.TrimEnd('\\') + '\\dotnetscope\\env.ps1'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("$env:DOTNET_ROOT = 'D:\\dotnet'", "D:\\dotnet")]
    [InlineData("  $Env:dotnet_root='D:\\dotnet\\' # comentário", "D:\\dotnet")]
    [InlineData("$env:DOTNET_ROOT = \"D:\\dotnet\";", "D:\\dotnet")]
    [InlineData("$env:DOTNET_ROOT = \"$HOME\\dotnet\"", null)]
    [InlineData("$env:DOTNET_ROOT = Join-Path $HOME dotnet", null)]
    [InlineData("$env:DOTNET_ROOT = 'o''brien'", null)]
    public void FindPowerShellAssignments_ResolveSomenteLiterais(string line, string? expected)
    {
        var assignment = Assert.Single(ThirdPartyEnvironment.FindPowerShellAssignments("perfil.ps1", "# perfil\r\n" + line + "\r\n"));

        Assert.Equal(2, assignment.Line);
        Assert.Equal(expected, assignment.ResolvedValue);
    }

    [Fact]
    public void FindPowerShellAssignments_IgnoraOBlocoProprioEOutrasVariaveis()
    {
        var content = string.Join('\n', EnvironmentScripts.PowerShellProfileBlock()) + "\n$env:DOTNET_ROOT_X64 = 'C:\\x'\n$env:PATH = 'C:\\y'\n";

        Assert.Empty(ThirdPartyEnvironment.FindPowerShellAssignments("perfil.ps1", content));
    }
}
