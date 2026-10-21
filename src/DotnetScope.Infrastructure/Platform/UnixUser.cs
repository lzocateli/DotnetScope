using System.Runtime.InteropServices;

namespace DotnetScope.Infrastructure.Platform;

public static partial class UnixUser
{
    /// <summary>Indica se o processo executa com UID efetivo 0 no Linux ou macOS.</summary>
    public static bool IsRoot() => !OperatingSystem.IsWindows() && GetEffectiveUserId() == 0;

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUserId();
}
