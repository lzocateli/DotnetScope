using DotnetScope.Core.Versions;

namespace DotnetScope.UnitTests;

public sealed class DotnetVersionTests
{
    [Theory]
    [InlineData("10.0.402", 10, 0, 402, null)]
    [InlineData("8.0.0", 8, 0, 0, null)]
    [InlineData("11.0.100-rc.2.26504.105", 11, 0, 100, "rc.2.26504.105")]
    [InlineData("10.0.0-preview.7.25380.108", 10, 0, 0, "preview.7.25380.108")]
    public void TryParse_VersaoValida_ExtraiComponentes(string text, int major, int minor, int patch, string? prerelease)
    {
        Assert.True(DotnetVersion.TryParse(text, out var version));
        Assert.Equal((major, minor, patch, prerelease), (version.Major, version.Minor, version.Patch, version.Prerelease));
        Assert.Equal(text, version.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("10.0")]
    [InlineData("10.0.4xx")]
    [InlineData("10.0.400.1")]
    [InlineData("010.0.400")]
    [InlineData("10.0.400-")]
    [InlineData("10.0.400-rc..1")]
    [InlineData("10.0.400-rc_1")]
    [InlineData(" 10.0.400")]
    [InlineData("-1.0.0")]
    [InlineData("99999999999.0.0")]
    public void TryParse_VersaoInvalida_Rejeita(string? text) =>
        Assert.False(DotnetVersion.TryParse(text, out _));

    [Theory]
    [InlineData("10.0.402", "10.0.401")]
    [InlineData("10.0.400", "10.0.303")]
    [InlineData("10.0.10", "10.0.9")]
    [InlineData("10.0.0", "10.0.0-rc.2.25502.107")]
    [InlineData("10.0.0-rc.2.25502.107", "10.0.0-rc.1.25451.107")]
    [InlineData("10.0.0-rc.1", "10.0.0-preview.7")]
    [InlineData("10.0.0-preview.10", "10.0.0-preview.9")]
    [InlineData("10.0.0-rc.1.1", "10.0.0-rc.1")]
    [InlineData("10.0.0-alpha", "10.0.0-1")]
    public void CompareTo_SegueSemVer(string greater, string smaller)
    {
        Assert.True(DotnetVersion.Parse(greater) > DotnetVersion.Parse(smaller));
        Assert.True(DotnetVersion.Parse(smaller) < DotnetVersion.Parse(greater));
    }

    [Fact]
    public void FeatureBand_E_Channel_DerivamDoPatch()
    {
        var version = DotnetVersion.Parse("10.0.402");

        Assert.Equal(4, version.FeatureBand);
        Assert.Equal(new ChannelVersion(10, 0), version.Channel);
    }

    [Fact]
    public void Equals_VersoesIguais_SaoIguaisComMesmoHash()
    {
        var left = DotnetVersion.Parse("11.0.100-rc.2.26504.105");
        var right = DotnetVersion.Parse("11.0.100-rc.2.26504.105");

        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }
}
