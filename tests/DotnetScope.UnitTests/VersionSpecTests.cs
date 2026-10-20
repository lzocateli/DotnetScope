using DotnetScope.Core;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;

namespace DotnetScope.UnitTests;

public sealed class VersionSpecTests
{
    [Theory]
    [InlineData("10.0.400", typeof(ExactVersionSpec), "10.0.400")]
    [InlineData("11.0.100-rc.2.26504.105", typeof(ExactVersionSpec), "11.0.100-rc.2.26504.105")]
    [InlineData("10.0", typeof(ChannelSpec), "10.0")]
    [InlineData("10.0.4xx", typeof(FeatureBandSpec), "10.0.4xx")]
    [InlineData("10.0.1XX", typeof(FeatureBandSpec), "10.0.1xx")]
    [InlineData("lts", typeof(SupportTrackSpec), "lts")]
    [InlineData("STS", typeof(SupportTrackSpec), "sts")]
    [InlineData(" 10.0 ", typeof(ChannelSpec), "10.0")]
    public void TryParse_FormasSuportadas(string text, Type expectedType, string normalized)
    {
        Assert.True(VersionSpec.TryParse(text, out var spec));
        Assert.IsType(expectedType, spec);
        Assert.Equal(normalized, spec.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("10")]
    [InlineData("10.0.0xx")]
    [InlineData("10.0.4x")]
    [InlineData("10.0.xx")]
    [InlineData("10.x")]
    [InlineData("10.0.4xxx")]
    public void TryParse_FormasInvalidas_Rejeita(string text) => Assert.False(VersionSpec.TryParse(text, out _));

    [Fact]
    public void Parse_Invalida_LancaErroDeUso()
    {
        var exception = Assert.Throws<DotnetScopeException>(() => VersionSpec.Parse("latest"));
        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
    }

    [Theory]
    [InlineData(null, "10.0.400", ComponentKind.Sdk, "10.0.400")]
    [InlineData(ComponentKind.Runtime, "10.0", ComponentKind.Runtime, "10.0")]
    [InlineData(null, "SDK-10.0.400", ComponentKind.Sdk, "10.0.400")]
    [InlineData(null, "runtime-10.0", ComponentKind.Runtime, "10.0")]
    [InlineData(null, "ASPNETCORE-lts", ComponentKind.AspNetCore, "lts")]
    [InlineData(null, "WindowsDesktop-10.0", ComponentKind.WindowsDesktop, "10.0")]
    [InlineData(ComponentKind.Sdk, "SDK-11.0.100-rc.2.26504.105", ComponentKind.Sdk, "11.0.100-rc.2.26504.105")]
    [InlineData(null, "11.0.100-rc.2.26504.105", ComponentKind.Sdk, "11.0.100-rc.2.26504.105")]
    public void ComponentRequest_Parse_AceitaFormaCompacta(ComponentKind? component, string text, ComponentKind expected, string version)
    {
        var request = ComponentRequest.Parse(component, text);

        Assert.Equal(expected, request.Component);
        Assert.Equal(version, request.Version.ToString());
    }

    [Theory]
    [InlineData(ComponentKind.Runtime, "SDK-10.0")]
    [InlineData(ComponentKind.Runtime, "10.0.4xx")]
    [InlineData(null, "RUNTIME-10.0.4xx")]
    [InlineData(null, "SDK-")]
    [InlineData(null, "FOO-10.0")]
    public void ComponentRequest_Parse_Invalida_LancaErroDeUso(ComponentKind? component, string text)
    {
        var exception = Assert.Throws<DotnetScopeException>(() => ComponentRequest.Parse(component, text));
        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
    }
}
