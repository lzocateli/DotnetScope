using System.Text;
using DotnetScope.Core;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;
using DotnetScope.Infrastructure.Http;
using DotnetScope.Infrastructure.Metadata;

namespace DotnetScope.UnitTests;

public sealed class MetadataMapperTests
{
    [Fact]
    public void ParseIndex_DescartaEntradasInvalidasEOrdenaPorCanal()
    {
        var channels = MetadataMapper.ParseIndex(Fixtures.Read("releases-index.json"), Fixtures.IndexUrl);

        Assert.Equal(["11.0", "10.0", "9.0", "7.0"], channels.Select(c => c.Channel.ToString()));
        var ten = channels.Single(c => c.Channel == new ChannelVersion(10, 0));
        Assert.Equal(ReleaseTrack.Lts, ten.Track);
        Assert.Equal(SupportPhase.Active, ten.Phase);
        Assert.Equal(new DateOnly(2028, 11, 14), ten.EolDate);
        Assert.Equal(DotnetVersion.Parse("10.0.402"), ten.LatestSdk);
        Assert.Equal(SupportPhase.GoLive, channels[0].Phase);
        Assert.Null(channels[0].EolDate);
    }

    [Fact]
    public void ParseChannel_UsaListaSdksENormalizaHashes()
    {
        var channel = Channel("10.0");
        var releases = MetadataMapper.ParseChannel(Fixtures.Read("releases-10.0.json"), channel, TrustedEndpoints.Official);

        var latest = releases.Releases[0];
        Assert.Equal("10.0.13", latest.ReleaseVersion);
        Assert.Equal(["10.0.402", "10.0.113"], latest.Components.Where(c => c.Component == ComponentKind.Sdk).Select(c => c.Version.ToString()));
        var file = latest.Components.First(c => c.Component == ComponentKind.Sdk).Files.Single(f => f.Name == "dotnet-sdk-linux-x64.tar.gz");
        Assert.Equal(new string('0', 128).Length, file.Sha512!.Length);
        Assert.Equal(file.Sha512, file.Sha512.ToLowerInvariant());
        Assert.Contains(latest.Components, c => c.Component == ComponentKind.WindowsDesktop);
    }

    [Fact]
    public void ParseChannel_HashSha256_FicaNulo()
    {
        var releases = MetadataMapper.ParseChannel(Fixtures.Read("releases-10.0.json"), Channel("10.0"), TrustedEndpoints.Official);

        var sdk401 = releases.Releases.SelectMany(r => r.Components).Single(c => c.Version.ToString() == "10.0.401");
        Assert.All(sdk401.Files, file => Assert.Null(file.Sha512));
    }

    [Fact]
    public void ParseChannel_MarcaArquivosForaDaAllowlist()
    {
        var releases = MetadataMapper.ParseChannel(Fixtures.Read("releases-9.0.json"), Channel("9.0"), TrustedEndpoints.Official);

        var files = releases.Releases[0].Components.Single(c => c.Version.ToString() == "9.0.319").Files;
        Assert.False(files.Single(f => f.Name == "dotnet-sdk-linux-arm64.tar.gz").IsTrusted);
        Assert.True(files.Single(f => f.Name == "dotnet-sdk-linux-x64.tar.gz").IsTrusted);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    public void NormalizeSha512_ValoresInvalidos(string? hash, string? expected) =>
        Assert.Equal(expected, MetadataMapper.NormalizeSha512(hash));

    [Fact]
    public void NormalizeSha512_RejeitaCaracteresNaoHexadecimais() =>
        Assert.Null(MetadataMapper.NormalizeSha512(new string('g', 128)));

    [Fact]
    public void ParseChannel_CanalDiferenteDoSolicitado_FalhaComoMetadataInconsistente()
    {
        var exception = Assert.Throws<DotnetScopeException>(() =>
            MetadataMapper.ParseChannel(Fixtures.Read("releases-9.0.json"), Channel("10.0"), TrustedEndpoints.Official));

        Assert.Equal(ExitCodes.Network, exception.ExitCode);
    }

    [Theory]
    [InlineData("{ invalido")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"releases-index\": 42}")]
    public void ParseIndex_JsonMalformado_FalhaComCodigoDeRede(string json)
    {
        var exception = Assert.Throws<DotnetScopeException>(() =>
            MetadataMapper.ParseIndex(Encoding.UTF8.GetBytes(json), Fixtures.IndexUrl));

        Assert.Equal(ExitCodes.Network, exception.ExitCode);
    }

    private static ReleaseChannel Channel(string version) =>
        MetadataMapper.ParseIndex(Fixtures.Read("releases-index.json"), Fixtures.IndexUrl)
            .Single(c => c.Channel.ToString() == version);
}
