using System.Runtime.InteropServices;
using DotnetScope.Core;
using DotnetScope.Infrastructure;
using DotnetScope.Infrastructure.Platform;

namespace DotnetScope.UnitTests;

public sealed class PlatformAndPathsTests
{
    [Theory]
    [InlineData("linux", Architecture.X64, false, "linux-x64")]
    [InlineData("linux", Architecture.Arm64, true, "linux-musl-arm64")]
    [InlineData("linux", Architecture.Arm, false, "linux-arm")]
    [InlineData("osx", Architecture.Arm64, false, "osx-arm64")]
    [InlineData("win", Architecture.X86, false, "win-x86")]
    [InlineData("linux", Architecture.S390x, false, "linux-s390x")]
    public void BuildRid_ComporOsArquiteturaELibc(string os, Architecture architecture, bool musl, string expected) =>
        Assert.Equal(expected, PlatformDetector.BuildRid(os, architecture, musl));

    [Fact]
    public void DetectRid_RetornaRidDoHost() =>
        Assert.Matches("^(linux|linux-musl|osx|win)-[a-z0-9]+$", PlatformDetector.DetectRid());

    [Fact]
    public void GetCacheDirectory_UnixComXdgAbsoluto_UsaXdg()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Caminhos Unix não são absolutos no Windows.");
        var path = DotnetScopePaths.GetCacheDirectory(Env(("XDG_CACHE_HOME", "/tmp/xdg")), isWindows: false, home: "/home/teste");

        Assert.Equal(Path.Combine("/tmp/xdg", "dotnetscope"), path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relativo/cache")]
    public void GetCacheDirectory_UnixSemXdgValido_UsaHome(string? xdg)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Caminhos Unix não são absolutos no Windows.");
        var path = DotnetScopePaths.GetCacheDirectory(Env(("XDG_CACHE_HOME", xdg)), isWindows: false, home: "/home/teste");

        Assert.Equal(Path.Combine("/home/teste", ".cache", "dotnetscope"), path);
    }

    [Fact]
    public void GetCacheDirectory_UnixSemHome_Falha()
    {
        var exception = Assert.Throws<DotnetScopeException>(() =>
            DotnetScopePaths.GetCacheDirectory(Env(), isWindows: false, home: string.Empty));

        Assert.Equal(ExitCodes.UnexpectedError, exception.ExitCode);
    }

    [Fact]
    public void GetCacheDirectory_WindowsSemLocalAppData_Falha() =>
        Assert.Throws<DotnetScopeException>(() => DotnetScopePaths.GetCacheDirectory(Env(), isWindows: true, home: "C:\\Users\\teste"));

    private static Func<string, string?> Env(params (string Name, string? Value)[] values) =>
        name => values.FirstOrDefault(v => v.Name == name).Value;
}
