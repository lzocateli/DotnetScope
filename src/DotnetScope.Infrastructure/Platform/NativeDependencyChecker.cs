namespace DotnetScope.Infrastructure.Platform;

/// <summary>
/// Verifica, sem privilégios, bibliotecas nativas exigidas pelo .NET no Linux. Retorna descrições do que falta.
/// </summary>
public static class NativeDependencyChecker
{
    private static readonly (string Description, string[] Patterns)[] Requirements =
    [
        ("ICU (libicu; ou defina DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1)", ["libicuuc.so.*"]),
        ("OpenSSL (libssl)", ["libssl.so.3", "libssl.so.1.1"]),
        ("libstdc++", ["libstdc++.so.6"]),
        ("libgcc_s", ["libgcc_s.so.1"]),
    ];

    public static IReadOnlyList<string> FindMissing() =>
        OperatingSystem.IsLinux() ? FindMissing(LibraryDirectories(), Directory.Exists("/etc/ssl/certs") || File.Exists("/etc/ssl/cert.pem")) : [];

    internal static IReadOnlyList<string> FindMissing(IReadOnlyList<string> directories, bool hasCertificates)
    {
        var missing = Requirements
            .Where(requirement => !requirement.Patterns.Any(pattern =>
                directories.Any(directory => SafeEnumerate(directory, pattern).Any())))
            .Select(requirement => requirement.Description)
            .ToList();
        if (!hasCertificates)
        {
            missing.Add("certificados de CA (ca-certificates)");
        }

        return missing;
    }

    private static List<string> LibraryDirectories()
    {
        var bases = new[] { "/lib", "/lib64", "/usr/lib", "/usr/lib64", "/usr/local/lib" };
        var directories = new List<string>(bases);
        foreach (var directory in bases)
        {
            directories.AddRange(SafeEnumerateDirectories(directory, "*-linux-*"));
        }

        return directories;
    }

    private static IEnumerable<string> SafeEnumerate(string directory, string pattern)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern).Take(1).ToList() : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string directory, string pattern)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.EnumerateDirectories(directory, pattern).ToList() : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
