using System.Diagnostics;
using DotnetScope.Core;

namespace DotnetScope.Infrastructure.SystemScope;

public sealed record CommandResult(int ExitCode, string Output, string Error);

/// <summary>Executa ferramentas do sistema (gerenciadores de pacotes) como o processo elevado.</summary>
public interface ISystemCommandRunner
{
    /// <summary>Indica se o executável existe no caminho absoluto informado.</summary>
    bool Exists(string executable);

    Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Executa somente caminhos absolutos, com <c>ArgumentList</c> (sem shell), ambiente fixo e prazo (modelo de ameaças,
/// R11): nada herdado de quem elevou, como <c>LD_PRELOAD</c>, <c>APT_CONFIG</c> ou <c>PATH</c>, influencia a ferramenta;
/// apenas as variáveis de proxy são repassadas.
/// </summary>
public sealed class SystemCommandRunner : ISystemCommandRunner
{
    private static readonly string[] ProxyVariables = ["http_proxy", "https_proxy", "no_proxy", "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY"];

    public bool Exists(string executable) => Path.IsPathFullyQualified(executable) && File.Exists(executable);

    public async Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!Exists(executable))
        {
            throw new DotnetScopeException(ExitCodes.MissingDependency, $"{executable} não foi encontrado.");
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = "/",
        };
        // Somente as variáveis de proxy sobrevivem: sem elas o gerenciador não alcança os espelhos em redes corporativas.
        // Um proxy não adultera pacotes, porque apt, dnf e apk verificam as assinaturas dos índices e pacotes.
        var proxies = ProxyVariables
            .Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name)))
            .Where(variable => !string.IsNullOrEmpty(variable.Value))
            .ToList();
        startInfo.Environment.Clear();
        foreach (var (name, value) in proxies)
        {
            startInfo.Environment[name] = value;
        }

        startInfo.Environment["PATH"] = "/usr/sbin:/usr/bin:/sbin:/bin";
        startInfo.Environment["LANG"] = "C";
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["DEBIAN_FRONTEND"] = "noninteractive";
        startInfo.Environment["HOME"] = OperatingSystem.IsMacOS() ? "/var/root" : "/root";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new DotnetScopeException(ExitCodes.UnexpectedError, $"Não foi possível executar {executable}.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var error = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new DotnetScopeException(ExitCodes.UnexpectedError, $"{executable} excedeu {timeout.TotalMinutes:0} min.");
        }

        return new CommandResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }
}
