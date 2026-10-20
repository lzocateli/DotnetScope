using DotnetScope.Cli;
using DotnetScope.Core;

namespace DotnetScope.UnitTests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsync_SemArgumentos_ExibeAjudaERetornaSucesso()
    {
        var (exitCode, output, error) = await RunAsync([]);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains(CliApplication.Description, output, StringComparison.Ordinal);
        Assert.Empty(error);
    }

    [Fact]
    public async Task RunAsync_OpcaoDesconhecida_RetornaErroDeUsoNoStderr()
    {
        var (exitCode, output, error) = await RunAsync(["--opcao-inexistente"]);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Empty(output);
        Assert.Contains("--opcao-inexistente", error, StringComparison.Ordinal);
        Assert.Contains("--help", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Version_ImprimeVersaoDoProduto()
    {
        var (exitCode, output, _) = await RunAsync(["--version"]);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.StartsWith("0.1.0", output.Trim(), StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CliApplication.RunAsync(args, output, error, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }
}
