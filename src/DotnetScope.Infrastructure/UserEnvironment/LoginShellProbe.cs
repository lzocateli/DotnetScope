using System.Diagnostics;

namespace DotnetScope.Infrastructure.UserEnvironment;

/// <summary>Resultado da verificação em um shell de login novo.</summary>
public sealed record LoginShellResult(string Shell, string? ResolvedDotnet, string? DotnetRoot);

/// <summary>
/// Abre um shell de login novo, com ambiente mínimo, para confirmar que <c>dotnet</c> resolve para a raiz
/// configurada (seção 10 do plano). Perfis com saída própria são tolerados por um marcador.
/// </summary>
public static class LoginShellProbe
{
    private const string Marker = "__DOTNETSCOPE__";

    public static Task<LoginShellResult?> ProbeAsync(string? shell, string home, CancellationToken cancellationToken) =>
        ProbeAsync(shell, home, TimeSpan.FromSeconds(20), cancellationToken);

    internal static async Task<LoginShellResult?> ProbeAsync(string? shell, string home, TimeSpan limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shell) || !File.Exists(shell))
        {
            return null;
        }

        var name = Path.GetFileName(shell);
        string[] arguments = name switch
        {
            "fish" => ["-l", "-c", $"printf '{Marker}%s|%s\\n' (command -v dotnet) \"$DOTNET_ROOT\""],
            "zsh" => ["-l", "-i", "-c", $"printf '{Marker}%s|%s\\n' \"$(command -v dotnet)\" \"$DOTNET_ROOT\""],
            "bash" or "sh" or "dash" or "ksh" => ["-l", "-c", $"printf '{Marker}%s|%s\\n' \"$(command -v dotnet)\" \"$DOTNET_ROOT\""],
            _ => [],
        };
        if (arguments.Length == 0)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(shell)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = home,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        startInfo.Environment["HOME"] = home;
        startInfo.Environment["SHELL"] = shell;
        startInfo.Environment["PATH"] = "/usr/local/bin:/usr/bin:/bin";
        startInfo.Environment["TERM"] = "dumb";
        foreach (var variable in new[] { "USER", "LOGNAME", "XDG_CONFIG_HOME", "XDG_STATE_HOME" })
        {
            if (Environment.GetEnvironmentVariable(variable) is { } value)
            {
                startInfo.Environment[variable] = value;
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            process.StandardInput.Close();
            string text;
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

                // Processos em segundo plano iniciados pelo perfil podem manter os pipes abertos após o shell sair.
                _ = await error.ConfigureAwait(false);
                text = await output.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return new LoginShellResult(shell, null, null);
            }

            var line = text.Split('\n').LastOrDefault(l => l.StartsWith(Marker, StringComparison.Ordinal));
            if (line is null)
            {
                return new LoginShellResult(shell, null, null);
            }

            var parts = line[Marker.Length..].Split('|', 2);
            return new LoginShellResult(shell, NullIfEmpty(parts[0]), parts.Length > 1 ? NullIfEmpty(parts[1].TrimEnd('\r')) : null);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Shell de login do usuário: <c>$SHELL</c> ou, na ausência, o campo de <c>/etc/passwd</c>.</summary>
    public static string? DefaultShell()
    {
        if (System.Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } shell)
        {
            return shell;
        }

        try
        {
            var user = System.Environment.UserName;
            return File.Exists("/etc/passwd")
                ? File.ReadLines("/etc/passwd")
                    .Select(line => line.Split(':'))
                    .FirstOrDefault(fields => fields.Length >= 7 && fields[0] == user)?[6]
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
