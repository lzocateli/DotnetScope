using System.Diagnostics;
using System.Reflection;
using DotnetScope.Core;

namespace DotnetScope.IntegrationTests;

public sealed class CliProcessTests
{
    // A versão vem de VersionPrefix em Directory.Build.props, compartilhado com os testes.
    private static readonly string ExpectedVersion = typeof(CliProcessTests).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    [Fact]
    public async Task Processo_Version_RetornaSucessoNoStdout()
    {
        var result = await RunCliAsync("--version");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.StartsWith(ExpectedVersion + "+", result.Output.Trim(), StringComparison.Ordinal);
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

    [Fact]
    public async Task Processo_ListAvailableOffline_UsaCacheDoUsuarioSemRede()
    {
        var home = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
        var cache = OperatingSystem.IsWindows()
            ? Path.Combine(home, "dotnetscope", "cache", "metadata")
            : Path.Combine(home, "dotnetscope", "metadata");
        Directory.CreateDirectory(cache);
        foreach (var fixture in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "metadata"), "*.json"))
        {
            File.Copy(fixture, Path.Combine(cache, Path.GetFileName(fixture)));
        }

        try
        {
            var environment = new Dictionary<string, string> { ["XDG_CACHE_HOME"] = home, ["LOCALAPPDATA"] = home };
            var channels = await RunCliAsync(environment, "list", "--available", "--offline");
            var versions = await RunCliAsync(environment, "list", "--available", "10.0.4xx", "--offline", "--output", "json");

            Assert.Equal(ExitCodes.Success, channels.ExitCode);
            Assert.Contains("10.0   lts   active", channels.Output, StringComparison.Ordinal);
            Assert.Equal(ExitCodes.Success, versions.ExitCode);
            using var document = System.Text.Json.JsonDocument.Parse(versions.Output);
            Assert.Equal("10.0.402", document.RootElement.GetProperty("resolved").GetProperty("version").GetString());
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    private static Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args) =>
        RunCliAsync(new Dictionary<string, string>(), args);

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(
        Dictionary<string, string> environment,
        params string[] args)
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

        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = Process.Start(startInfo)!;
        var cancellationToken = TestContext.Current.CancellationToken;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await output, await error);
    }
}
