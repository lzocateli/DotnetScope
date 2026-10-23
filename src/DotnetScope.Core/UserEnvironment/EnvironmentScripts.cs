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

    /// <summary>
    /// Script de <c>/etc/profile.d</c> do escopo global: <c>DOTNET_ROOT</c> e somente a raiz no PATH (as ferramentas
    /// globais do .NET ficam no home de cada usuário), com guarda contra duplicação.
    /// </summary>
    public static string RenderSystemProfile(string dotnetRoot, bool telemetryOptOut)
    {
        ArgumentNullException.ThrowIfNull(dotnetRoot);
        var quoted = ShellQuoting.Posix(dotnetRoot);
        var lines = new List<string>
        {
            "# Gerado por dotnetscope (escopo global); não edite. Use `dotnetscope env apply|remove --scope global`.",
            $"export DOTNET_ROOT={quoted}",
            "case \":${PATH}:\" in",
            $"  *:{quoted}:*) ;;",
            $"  *) export PATH={quoted}:\"${{PATH}}\" ;;",
            "esac",
        };
        if (telemetryOptOut)
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

    /// <summary>
    /// Script do PowerShell para a sessão atual, idempotente; use com <c>| Out-String | Invoke-Expression</c>. O separador
    /// do PATH vem de <c>[IO.Path]::PathSeparator</c> para funcionar também no pwsh do Linux e do macOS.
    /// </summary>
    public static string RenderPowerShell(EnvironmentTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var lines = new List<string> { Header, $"$env:DOTNET_ROOT = {ShellQuoting.PowerShell(target.DotnetRoot)}" };
        foreach (var directory in new[] { target.ToolsDirectory, target.DotnetRoot })
        {
            var quoted = ShellQuoting.PowerShell(directory);
            lines.Add(
                $"if (-not (($env:PATH -split [IO.Path]::PathSeparator) -contains {quoted})) "
                + $"{{ $env:PATH = {quoted} + [IO.Path]::PathSeparator + $env:PATH }}");
        }

        if (target.TelemetryOptOut)
        {
            lines.Add("$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'");
        }

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>
    /// Comandos do cmd.exe para a sessão atual. Caminhos com <c>%</c>, <c>!</c>, <c>^</c> ou <c>"</c> não são
    /// representáveis com segurança e geram erro.
    /// </summary>
    public static string RenderCmd(EnvironmentTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        foreach (var path in new[] { target.DotnetRoot, target.ToolsDirectory })
        {
            if (path.IndexOfAny(['%', '!', '^', '"']) >= 0)
            {
                throw new DotnetScopeException(ExitCodes.Usage, $"O caminho {path} não pode ser representado no cmd.exe; use --shell pwsh.");
            }
        }

        var lines = new List<string>
        {
            "rem Gerado por dotnetscope; não edite. Use `dotnetscope env apply` para configurar novos terminais.",
            $"set \"DOTNET_ROOT={target.DotnetRoot}\"",
            $"set \"PATH={target.DotnetRoot};{target.ToolsDirectory};%PATH%\"",
        };
        if (target.TelemetryOptOut)
        {
            lines.Add("set \"DOTNET_CLI_TELEMETRY_OPTOUT=1\"");
        }

        return string.Join("\r\n", lines) + "\r\n";
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
    /// Linhas do bloco que carrega <c>%LOCALAPPDATA%\dotnetscope\env.ps1</c> nos perfis do PowerShell (Windows). O bloco é
    /// somente ASCII e não contém caminhos literais: o Windows PowerShell 5.1 lê perfis sem BOM na página de código ANSI.
    /// </summary>
    public static IReadOnlyList<string> PowerShellProfileBlock() =>
    [
        BeginMarker,
        "# Gerenciado por dotnetscope. Use `dotnetscope env remove` para remover.",
        "$__dotnetscope = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA.TrimEnd('\\') + '\\dotnetscope\\env.ps1' }",
        "if ($__dotnetscope -and (Test-Path -LiteralPath $__dotnetscope)) { . $__dotnetscope }",
        "Remove-Variable __dotnetscope -ErrorAction Ignore",
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
