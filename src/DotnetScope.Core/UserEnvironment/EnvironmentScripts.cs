namespace DotnetScope.Core.UserEnvironment;

/// <summary>Gera os scripts de ambiente e o bloco que os carrega nos perfis.</summary>
public static class EnvironmentScripts
{
    public const string BeginMarker = "# >>> dotnetscope >>>";
    public const string EndMarker = "# <<< dotnetscope <<<";
    private const string Header = "# Gerado por dotnetscope; não edite. Use `dotnetscope env apply` ou `dotnetscope env remove`.";

    /// <summary>Script POSIX idempotente: só acrescenta ao PATH entradas ausentes.</summary>
    public static string RenderPosix(EnvironmentTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var lines = new List<string> { Header, $"export DOTNET_ROOT={ShellQuoting.Posix(target.DotnetRoot)}" };
        foreach (var directory in new[] { target.ToolsDirectory, target.DotnetRoot })
        {
            var quoted = ShellQuoting.Posix(directory);
            lines.Add("case \":${PATH}:\" in");
            lines.Add($"  *:{quoted}:*) ;;");
            lines.Add($"  *) export PATH={quoted}:\"${{PATH}}\" ;;");
            lines.Add("esac");
        }

        if (target.TelemetryOptOut)
        {
            lines.Add("export DOTNET_CLI_TELEMETRY_OPTOUT=1");
        }

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>Script do fish (conf.d), idempotente.</summary>
    public static string RenderFish(EnvironmentTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var lines = new List<string> { Header, $"set -gx DOTNET_ROOT {ShellQuoting.Fish(target.DotnetRoot)}" };
        foreach (var directory in new[] { target.ToolsDirectory, target.DotnetRoot })
        {
            var quoted = ShellQuoting.Fish(directory);
            lines.Add($"if not contains -- {quoted} $PATH");
            lines.Add($"    set -gx PATH {quoted} $PATH");
            lines.Add("end");
        }

        if (target.TelemetryOptOut)
        {
            lines.Add("set -gx DOTNET_CLI_TELEMETRY_OPTOUT 1");
        }

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>Linhas do bloco de perfil que carrega o script POSIX (sem marcador de fim de linha final).</summary>
    /// <param name="scriptExpression">Caminho do script já em forma de expressão shell segura.</param>
    public static IReadOnlyList<string> ProfileBlock(string scriptExpression) =>
    [
        BeginMarker,
        "# Gerenciado por dotnetscope. Use `dotnetscope env remove` para remover.",
        $"if [ -f {scriptExpression} ]; then . {scriptExpression}; fi",
        EndMarker,
    ];

    /// <summary>
    /// Expressão shell do caminho do script: relativa a <c>"$HOME"</c> quando está no home (portável em dotfiles),
    /// senão o caminho absoluto entre aspas simples.
    /// </summary>
    public static string ScriptExpression(string scriptPath, string home)
    {
        ArgumentNullException.ThrowIfNull(scriptPath);
        ArgumentNullException.ThrowIfNull(home);
        var prefix = home.TrimEnd('/') + "/";
        return scriptPath.StartsWith(prefix, StringComparison.Ordinal)
            ? "\"$HOME\"/" + ShellQuoting.Posix(scriptPath[prefix.Length..])
            : ShellQuoting.Posix(scriptPath);
    }
}
