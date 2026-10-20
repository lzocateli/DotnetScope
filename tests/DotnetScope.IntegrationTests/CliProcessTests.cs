using System.Diagnostics;
using DotnetScope.Core;

namespace DotnetScope.IntegrationTests;

public sealed class CliProcessTests
{
    [Fact]
    public async Task Processo_Version_RetornaSucessoNoStdout()
    {
        var result = await RunCliAsync("--version");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.StartsWith("0.1.0", result.Output.Trim(), StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task Processo_OpcaoDesconhecida_RetornaCodigoDeUsoNoStderr()
    {
        var result = await RunCliAsync("--opcao-inexistente");

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--help", result.Error, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        var cliAssembly = Path.Combine(AppContext.BaseDirectory, "dotnetscope.dll");
        Assert.True(File.Exists(cliAssembly), $"CLI não encontrada em {cliAssembly}");

        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(cliAssembly);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var cancellationToken = TestContext.Current.CancellationToken;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await output, await error);
    }
}
