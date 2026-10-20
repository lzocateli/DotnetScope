using System.Runtime.InteropServices;
using DotnetScope.Core;

namespace DotnetScope.Infrastructure.Platform;

/// <summary>Detecta o RID do sistema operacional atual (por exemplo, linux-x64, linux-musl-arm64, win-x64, osx-arm64).</summary>
public static class PlatformDetector
{
    public static string DetectRid()
    {
        var os = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsMacOS() ? "osx"
            : OperatingSystem.IsLinux() ? "linux"
            : throw new DotnetScopeException(ExitCodes.NotFound, $"Sistema operacional não suportado: {RuntimeInformation.OSDescription}.");

        return BuildRid(os, RuntimeInformation.OSArchitecture, os == "linux" && IsMusl());
    }

    internal static string BuildRid(string os, Architecture architecture, bool musl)
    {
        var arch = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            Architecture.X86 => "x86",
            _ => architecture.ToString().ToLowerInvariant(),
        };

        return musl ? $"{os}-musl-{arch}" : $"{os}-{arch}";
    }

    private static bool IsMusl()
    {
        try
        {
            return Directory.Exists("/lib") && Directory.EnumerateFiles("/lib", "ld-musl-*.so.1").Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
