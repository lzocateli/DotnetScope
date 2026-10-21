using System.Runtime.InteropServices;

namespace DotnetScope.Infrastructure.Platform;

public static partial class UnixUser
{
    /// <summary>Indica se o processo executa com UID efetivo 0 no Linux ou macOS.</summary>
    public static bool IsRoot() => !OperatingSystem.IsWindows() && GetEffectiveUserId() == 0;

    /// <summary>
    /// Remove a variável do ambiente gerenciado e do nativo. No Unix, bibliotecas nativas como o OpenSSL leem o
    /// ambiente do processo (<c>getenv</c>), que <see cref="Environment.SetEnvironmentVariable(string, string)"/> não altera.
    /// </summary>
    public static void UnsetEnvironmentVariable(string name)
    {
        Environment.SetEnvironmentVariable(name, null);
        if (!OperatingSystem.IsWindows())
        {
            _ = UnsetEnvironment(name);
        }
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUserId();

    [LibraryImport("libc", EntryPoint = "unsetenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int UnsetEnvironment(string name);
}
