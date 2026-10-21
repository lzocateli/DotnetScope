using DotnetScope.Core;
using DotnetScope.Core.Resolution;
using DotnetScope.Core.Versions;
using DotnetScope.Infrastructure.Installation;

namespace DotnetScope.UnitTests;

public sealed class GlobalJsonTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));

    public GlobalJsonTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("10.0.400", "latestPatch", false, "10.0.402")]
    [InlineData("10.0.300", "latestPatch", false, "10.0.303")]
    [InlineData("10.0.300", "patch", false, "10.0.303")]
    [InlineData("10.0.111", "patch", false, "10.0.111")]
    [InlineData("10.0.111", "disable", false, "10.0.111")]
    [InlineData("10.0.114", "feature", false, "10.0.303")]
    [InlineData("10.0.114", "latestFeature", false, "10.0.402")]
    [InlineData("9.0.320", "major", false, "10.0.113")]
    [InlineData("9.0.100", "latestMinor", false, "9.0.319")]
    [InlineData("9.0.100", "latestMajor", false, "10.0.402")]
    [InlineData("9.0.100", "latestMajor", true, "11.0.100-rc.2.26504.105")]
    [InlineData(null, "latestMajor", false, "10.0.402")]
    public async Task ResolveGlobalJson_SegueSemanticaDeRollForward(string? version, string policy, bool prerelease, string expected)
    {
        Assert.True(RollForwardPolicies.TryParse(policy, out var rollForward));
        var sdk = new GlobalJsonSdk("global.json", version is null ? null : DotnetVersion.Parse(version), rollForward, prerelease);

        var artifact = await new VersionResolver(new FixtureMetadataSource()).ResolveGlobalJsonAsync(
            sdk, new ResolveOptions("linux-x64", prerelease), TestContext.Current.CancellationToken);

        Assert.Equal(expected, artifact.Version.ToString());
    }

    [Theory]
    [InlineData("10.0.110", "disable")]
    [InlineData("9.0.320", "minor")]
    [InlineData("9.0.320", "latestPatch")]
    [InlineData("10.0.500", "latestFeature")]
    public async Task ResolveGlobalJson_SemSdkCompativel_NaoEncontrado(string version, string policy)
    {
        Assert.True(RollForwardPolicies.TryParse(policy, out var rollForward));
        var sdk = new GlobalJsonSdk("global.json", DotnetVersion.Parse(version), rollForward, false);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => new VersionResolver(new FixtureMetadataSource())
            .ResolveGlobalJsonAsync(sdk, new ResolveOptions("linux-x64", false), TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.NotFound, exception.ExitCode);
    }

    [Fact]
    public void Read_AceitaComentariosEVirgulasFinaisEAplicaPadroes()
    {
        Write("global.json", "{\n  // comentário\n  \"sdk\": { \"version\": \"10.0.400\", },\n}");
        Write("sem-versao/global.json", "{ \"sdk\": { \"allowPrerelease\": true } }");

        var withVersion = GlobalJsonReader.Read(null, _directory);
        var withoutVersion = GlobalJsonReader.Read(Path.Combine(_directory, "sem-versao"), _directory);

        Assert.Equal(DotnetVersion.Parse("10.0.400"), withVersion.Version);
        Assert.Equal(RollForwardPolicy.LatestPatch, withVersion.RollForward);
        Assert.False(withVersion.AllowPrerelease);
        Assert.Null(withoutVersion.Version);
        Assert.Equal(RollForwardPolicy.LatestMajor, withoutVersion.RollForward);
        Assert.True(withoutVersion.AllowPrerelease);
    }

    [Fact]
    public void Read_ProcuraNosDiretoriosAcima()
    {
        Write("global.json", "{ \"sdk\": { \"version\": \"10.0.100\", \"rollForward\": \"latestFeature\" } }");
        var nested = Path.Combine(_directory, "src", "app");
        Directory.CreateDirectory(nested);

        var sdk = GlobalJsonReader.Read(null, nested);

        Assert.Equal(Path.Combine(_directory, "global.json"), sdk.Path);
        Assert.Equal(RollForwardPolicy.LatestFeature, sdk.RollForward);
    }

    [Theory]
    [InlineData("{ invalido", ExitCodes.Usage)]
    [InlineData("{ \"tools\": {} }", ExitCodes.Usage)]
    [InlineData("{ \"sdk\": { \"version\": \"10.0\" } }", ExitCodes.Usage)]
    [InlineData("{ \"sdk\": { \"version\": \"10.0.100\", \"rollForward\": \"latest\" } }", ExitCodes.Usage)]
    public void Read_ArquivoInvalido_ErroDeUso(string content, int expected)
    {
        Write("global.json", content);

        var exception = Assert.Throws<DotnetScopeException>(() => GlobalJsonReader.Read(Path.Combine(_directory, "global.json"), _directory));

        Assert.Equal(expected, exception.ExitCode);
    }

    [Fact]
    public void Read_ArquivoAusente_NaoEncontrado()
    {
        var exception = Assert.Throws<DotnetScopeException>(() => GlobalJsonReader.Read(Path.Combine(_directory, "x.json"), _directory));

        Assert.Equal(ExitCodes.NotFound, exception.ExitCode);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
