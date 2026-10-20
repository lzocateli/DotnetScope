using DotnetScope.Core;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Resolution;

namespace DotnetScope.UnitTests;

public sealed class VersionResolverTests
{
    private const string LinuxX64 = "linux-x64";

    [Theory]
    [InlineData("sdk", "10.0.400", false, "10.0.400")]
    [InlineData("sdk", "10.0", false, "10.0.402")]
    [InlineData("sdk", "10.0.4xx", false, "10.0.402")]
    [InlineData("sdk", "10.0.3xx", false, "10.0.303")]
    [InlineData("sdk", "10.0.1xx", false, "10.0.113")]
    [InlineData("sdk", "lts", false, "10.0.402")]
    [InlineData("sdk", "sts", false, "9.0.319")]
    [InlineData("sdk", "sts", true, "11.0.100-rc.2.26504.105")]
    [InlineData("sdk", "10.0.1xx", true, "10.0.113")]
    [InlineData("runtime", "10.0", false, "10.0.13")]
    [InlineData("runtime", "9.0", false, "9.0.21")]
    [InlineData("aspnetcore", "lts", false, "10.0.13")]
    [InlineData("runtime", "11.0", true, "11.0.0-rc.2.26504.105")]
    [InlineData("sdk", "11.0.100-rc.1.26451.101", true, "11.0.100-rc.1.26451.101")]
    public async Task ResolveAsync_EscolheVersaoEsperada(string component, string spec, bool prerelease, string expected)
    {
        var artifact = await ResolveAsync($"{component.ToUpperInvariant()}-{spec}", LinuxX64, prerelease);

        Assert.Equal(expected, artifact.Version.ToString());
    }

    [Fact]
    public async Task ResolveAsync_ArtefatoTemNomeOficialRidEHashNormalizado()
    {
        var artifact = await ResolveAsync("SDK-10.0.402", LinuxX64);

        Assert.Equal("dotnet-sdk-linux-x64.tar.gz", artifact.File.Name);
        Assert.Equal(LinuxX64, artifact.File.Rid);
        Assert.Equal("builds.dotnet.microsoft.com", artifact.File.Url.Host);
        Assert.Matches("^[0-9a-f]{128}$", artifact.Sha512);
        Assert.Equal("10.0.13", artifact.ReleaseVersion);
    }

    [Theory]
    [InlineData("SDK-10.0", "win-x64", "dotnet-sdk-win-x64.zip")]
    [InlineData("SDK-10.0", "osx-arm64", "dotnet-sdk-osx-arm64.tar.gz")]
    [InlineData("SDK-10.0", "linux-musl-x64", "dotnet-sdk-linux-musl-x64.tar.gz")]
    [InlineData("ASPNETCORE-10.0", LinuxX64, "aspnetcore-runtime-linux-x64.tar.gz")]
    [InlineData("WINDOWSDESKTOP-10.0", "win-x64", "windowsdesktop-runtime-win-x64.zip")]
    public async Task ResolveAsync_EscolheArquivoCompactadoPorRid(string request, string rid, string fileName)
    {
        var artifact = await ResolveAsync(request, rid);

        Assert.Equal(fileName, artifact.File.Name);
    }

    [Fact]
    public async Task ResolveAsync_VersaoSemSha512_NaoEhResolvida()
    {
        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => ResolveAsync("SDK-10.0.401", LinuxX64));

        Assert.Equal(ExitCodes.NotFound, exception.ExitCode);
        Assert.Contains("SHA-512", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_WindowsDesktopForaDoWindows_ExplicaRestricao()
    {
        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => ResolveAsync("WINDOWSDESKTOP-10.0", LinuxX64));

        Assert.Equal(ExitCodes.NotFound, exception.ExitCode);
        Assert.Contains("RIDs Windows", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_ArquivoForaDaAllowlist_FalhaComIntegridadeSemRecorrerAVersaoAnterior()
    {
        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => ResolveAsync("SDK-9.0", "linux-arm64"));

        Assert.Equal(ExitCodes.Integrity, exception.ExitCode);
        Assert.Contains("example.invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_OutraBandaComOrigemConfiavel_Resolve()
    {
        var artifact = await ResolveAsync("SDK-9.0.1xx", "linux-arm64");

        Assert.Equal("9.0.115", artifact.Version.ToString());
        Assert.True(artifact.File.IsTrusted);
    }

    [Fact]
    public async Task ResolveAsync_RidSemArquivo_NaoEncontrado()
    {
        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => ResolveAsync("SDK-10.0", "linux-s390x"));

        Assert.Equal(ExitCodes.NotFound, exception.ExitCode);
    }

    [Fact]
    public async Task ResolveAsync_PrereleaseExataSemFlag_ErroDeUso()
    {
        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => ResolveAsync("SDK-11.0.100-rc.2.26504.105", LinuxX64));

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
        Assert.Contains("--prerelease", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_CanalSoComPrereleaseSemFlag_SugerePrerelease()
    {
        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => ResolveAsync("SDK-11.0", LinuxX64));

        Assert.Equal(ExitCodes.NotFound, exception.ExitCode);
        Assert.Contains("--prerelease", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SDK-6.0")]
    [InlineData("SDK-10.0.999")]
    [InlineData("SDK-10.0.9xx")]
    [InlineData("SDK-12.0")]
    public async Task ResolveAsync_InexistenteNoMetadata_NaoEncontrado(string request)
    {
        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => ResolveAsync(request, LinuxX64));

        Assert.Equal(ExitCodes.NotFound, exception.ExitCode);
    }

    [Fact]
    public async Task QueryAsync_ListaCandidatasDaMaisNovaParaAMaisAntigaSemPrerelease()
    {
        var resolver = new VersionResolver(new FixtureMetadataSource());

        var query = await resolver.QueryAsync(
            ComponentRequest.Parse(null, "SDK-10.0"),
            new ResolveOptions(LinuxX64, AllowPrerelease: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["10.0.402", "10.0.401", "10.0.400", "10.0.303", "10.0.113", "10.0.112", "10.0.111"],
            query.Candidates.Select(c => c.Version.ToString()));
        Assert.Null(query.Candidates.Single(c => c.Version.ToString() == "10.0.401").Archive);
    }

    [Fact]
    public async Task ResolveAsync_CanalEol_AindaResolveParaAvisoNaCli()
    {
        var artifact = await ResolveAsync("RUNTIME-7.0", LinuxX64);

        Assert.Equal(SupportPhase.Eol, artifact.Channel.Phase);
        Assert.Equal("7.0.20", artifact.Version.ToString());
    }

    private static Task<ResolvedArtifact> ResolveAsync(string request, string rid, bool prerelease = false) =>
        new VersionResolver(new FixtureMetadataSource()).ResolveAsync(
            ComponentRequest.Parse(null, request),
            new ResolveOptions(rid, prerelease),
            TestContext.Current.CancellationToken);
}
