using System.Text.Json;
using DotnetScope.Cli;
using DotnetScope.Core;
using DotnetScope.Core.Releases;
using DotnetScope.Infrastructure.Metadata;

namespace DotnetScope.UnitTests;

public sealed class ListCommandTests
{
    [Fact]
    public async Task List_SemAvailable_ErroDeUso()
    {
        var result = await RunAsync("list");

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Contains("--available", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAvailable_SemValor_ListaCanaisSemPrerelease()
    {
        var result = await RunAsync("list", "--available");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Canal", lines[0], StringComparison.Ordinal);
        Assert.Equal(["10.0", "9.0", "7.0"], lines.Skip(1).Select(line => line.Split(' ')[0]));
        Assert.Contains("2028-11-14", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAvailable_ComPrerelease_IncluiCanalGoLive()
    {
        var result = await RunAsync("list", "--available", "--prerelease");

        Assert.Contains("11.0   sts   go-live", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAvailable_FeatureBand_MarcaVersaoResolvida()
    {
        var result = await RunAsync("list", "--available", "10.0.4xx");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("*  10.0.402", result.Output, StringComparison.Ordinal);
        Assert.Contains("10.0.401  10.0.12  2026-09-08  não        indisponível", result.Output, StringComparison.Ordinal);
        Assert.Contains("Resolvida: sdk 10.0.402 (dotnet-sdk-linux-x64.tar.gz)", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.303", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAvailable_JsonDeVersoes_TemContratoEstavel()
    {
        var result = await RunAsync("list", "--available", "lts", "--component", "runtime", "--output", "json");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        Assert.Equal("runtime", root.GetProperty("component").GetString());
        Assert.Equal("lts", root.GetProperty("request").GetString());
        Assert.Equal("linux-x64", root.GetProperty("rid").GetString());
        Assert.Equal("10.0", root.GetProperty("channel").GetProperty("channel").GetString());
        Assert.Equal("active", root.GetProperty("channel").GetProperty("supportPhase").GetString());
        Assert.Equal("10.0.13", root.GetProperty("versions")[0].GetProperty("version").GetString());
        Assert.True(root.GetProperty("versions")[0].GetProperty("archiveAvailable").GetBoolean());
        var resolved = root.GetProperty("resolved");
        Assert.Equal("dotnet-runtime-linux-x64.tar.gz", resolved.GetProperty("fileName").GetString());
        Assert.Matches("^[0-9a-f]{128}$", resolved.GetProperty("sha512").GetString());
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task ListAvailable_JsonDeCanais_ListaCamposEsperados()
    {
        var result = await RunAsync("list", "--available", "--output", "json");

        using var document = JsonDocument.Parse(result.Output);
        var first = document.RootElement.GetProperty("channels")[0];
        Assert.Equal("10.0", first.GetProperty("channel").GetString());
        Assert.Equal("lts", first.GetProperty("releaseType").GetString());
        Assert.Equal("10.0.402", first.GetProperty("latestSdk").GetString());
        Assert.Equal("2028-11-14", first.GetProperty("eolDate").GetString());
    }

    [Fact]
    public async Task ListAvailable_WindowsDesktopNoLinux_ListaENaoResolve()
    {
        var result = await RunAsync("list", "--available", "WINDOWSDESKTOP-10.0");

        Assert.Equal(ExitCodes.NotFound, result.ExitCode);
        Assert.Contains("10.0.13", result.Output, StringComparison.Ordinal);
        Assert.Contains("RIDs Windows", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAvailable_OrigemRecusada_ListaEFalhaComIntegridade()
    {
        var result = await RunAsync(["list", "--available", "9.0", "--output", "json"], rid: "linux-arm64");

        Assert.Equal(ExitCodes.Integrity, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.False(document.RootElement.GetProperty("versions")[0].GetProperty("archiveAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("resolved").ValueKind);
        Assert.DoesNotContain("example.invalid", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAvailable_CanalEol_EmiteAviso()
    {
        var result = await RunAsync("list", "--available", "RUNTIME-7.0");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("aviso: o canal 7.0 está fora de suporte desde 2024-05-14", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ExitCodes.Usage, "list", "--available", "latest")]
    [InlineData(ExitCodes.Usage, "list", "--available", "SDK-10.0", "-c", "runtime")]
    [InlineData(ExitCodes.Usage, "list", "--available", "-c", "runtime")]
    [InlineData(ExitCodes.Usage, "list", "--available", "10.0", "-c", "dotnet")]
    [InlineData(ExitCodes.Usage, "list", "--available", "--output", "xml")]
    [InlineData(ExitCodes.Usage, "list", "--available", "SDK-11.0.100-rc.2.26504.105")]
    [InlineData(ExitCodes.NotFound, "list", "--available", "6.0")]
    [InlineData(ExitCodes.NotFound, "list", "--available", "11.0")]
    public async Task ListAvailable_EntradasInvalidas_RetornamCodigoDocumentado(int expected, params string[] args)
    {
        var result = await RunAsync(args);

        Assert.Equal(expected, result.ExitCode);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task ListAvailable_Offline_RepassaOpcaoParaFonte()
    {
        MetadataOptions? received = null;
        await RunAsync(["list", "--available", "--offline"], options => received = options);

        Assert.True(received?.Offline);
    }

    [Fact]
    public async Task ListAvailable_FalhaDeRede_RetornaCodigoDeRede()
    {
        var services = new CliServices(
            new StringWriter(),
            new StringWriter(),
            (_, _) => new FailingSource(),
            () => "linux-x64");

        var exitCode = await CliApplication.RunAsync(["list", "--available"], services, TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Network, exitCode);
        Assert.Contains("erro: sem rede", services.Error.ToString(), StringComparison.Ordinal);
    }

    private static Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args) => RunAsync(args, null);

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string[] args,
        Action<MetadataOptions>? onCreate = null,
        string rid = "linux-x64")
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var services = new CliServices(
            output,
            error,
            (options, _) =>
            {
                onCreate?.Invoke(options);
                return new FixtureMetadataSource();
            },
            () => rid);
        var exitCode = await CliApplication.RunAsync(args, services, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private sealed class FailingSource : IReleaseMetadataSource
    {
        public Task<IReadOnlyList<ReleaseChannel>> GetChannelsAsync(CancellationToken cancellationToken) =>
            throw new DotnetScopeException(ExitCodes.Network, "sem rede");

        public Task<ChannelReleases> GetChannelReleasesAsync(ReleaseChannel channel, CancellationToken cancellationToken) =>
            throw new DotnetScopeException(ExitCodes.Network, "sem rede");
    }
}
