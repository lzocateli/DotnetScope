using System.Diagnostics;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Resolution;

namespace DotnetScope.Infrastructure.Installation;

/// <summary>Executa o host instalado (<c>--list-sdks</c>/<c>--list-runtimes</c>) para confirmar o componente.</summary>
internal static class DotnetHostValidator
{
    /// <summary>
    /// Validação sem executar o host: confere a pasta versionada do componente. Usada quando a raiz pode ser alterada
    /// por outro usuário, para nunca executar como root um binário que ele controla.
    /// </summary>
    public static HostValidation ValidateFiles(string root, ResolvedArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var primary = Path.Combine(root, DotnetLayout.PrimaryPath(artifact.Component, artifact.Version));
        return Directory.Exists(primary)
            ? new HostValidation(true, $"{DotnetLayout.PrimaryPath(artifact.Component, artifact.Version)} presente (raiz não confiável: o host não foi executado como root)")
            : new HostValidation(false, $"{primary} não existe após a instalação");
    }

    public static async Task<HostValidation> ValidateAsync(string root, ResolvedArtifact artifact, CancellationToken cancellationToken)
    {
        var host = Path.Combine(root, DotnetLayout.HostExecutable(OperatingSystem.IsWindows()));
        var (argument, expected) = artifact.Component switch
        {
            ComponentKind.Sdk => ("--list-sdks", $"{artifact.Version} ["),
            ComponentKind.Runtime => ("--list-runtimes", $"Microsoft.NETCore.App {artifact.Version} ["),
            ComponentKind.AspNetCore => ("--list-runtimes", $"Microsoft.AspNetCore.App {artifact.Version} ["),
            _ => ("--list-runtimes", $"Microsoft.WindowsDesktop.App {artifact.Version} ["),
        };

        var startInfo = new ProcessStartInfo(host)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(argument);
        // Ambiente mínimo: nada herdado (LD_PRELOAD, DOTNET_*, COREHOST_TRACE, proxies) influencia o host executado,
        // inclusive como root ou administrador.
        if (OperatingSystem.IsWindows())
        {
            var preserved = new[] { "SystemRoot", "windir", "SystemDrive", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData" }
                .Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name)))
                .Where(variable => !string.IsNullOrEmpty(variable.Value))
                .ToList();
            startInfo.Environment.Clear();
            foreach (var (name, value) in preserved)
            {
                startInfo.Environment[name] = value;
            }

            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            startInfo.Environment["PATH"] = Path.Combine(systemRoot, "System32") + ";" + systemRoot;
        }
        else
        {
            startInfo.Environment.Clear();
            startInfo.Environment["PATH"] = "/usr/bin:/bin";
            startInfo.Environment["LANG"] = "C.UTF-8";
        }

        startInfo.Environment["DOTNET_ROOT"] = root;
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new HostValidation(false, $"não foi possível executar {host}");
            }

            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                return new HostValidation(false, $"{host} {argument} excedeu 60s");
            }

            var lines = (await output.ConfigureAwait(false)).Split('\n');
            _ = await error.ConfigureAwait(false);
            return process.ExitCode == 0 && lines.Any(line => line.StartsWith(expected, StringComparison.Ordinal))
                ? new HostValidation(true, $"dotnet {argument} lista {artifact.Version}")
                : new HostValidation(false, $"dotnet {argument} terminou com código {process.ExitCode} sem listar {artifact.Version}");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            return new HostValidation(false, $"não foi possível executar {host}: {exception.Message}");
        }
    }
}
