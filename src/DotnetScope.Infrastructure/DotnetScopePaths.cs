using DotnetScope.Core;

namespace DotnetScope.Infrastructure;

/// <summary>Diretórios de estado do DotnetScope por sistema operacional.</summary>
public static class DotnetScopePaths
{
    /// <summary>
    /// Cache: <c>%LOCALAPPDATA%\dotnetscope\cache</c> no Windows; <c>$XDG_CACHE_HOME/dotnetscope</c> (se absoluto)
    /// ou <c>~/.cache/dotnetscope</c> no Linux e macOS.
    /// </summary>
    public static string GetCacheDirectory() =>
        GetCacheDirectory(
            Environment.GetEnvironmentVariable,
            OperatingSystem.IsWindows(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    internal static string GetCacheDirectory(Func<string, string?> environment, bool isWindows, string home)
    {
        if (isWindows)
        {
            var localAppData = environment("LOCALAPPDATA");
            return IsAbsolute(localAppData)
                ? Path.Combine(localAppData!, "dotnetscope", "cache")
                : throw new DotnetScopeException(ExitCodes.UnexpectedError, "LOCALAPPDATA não está definido como caminho absoluto.");
        }

        var xdgCache = environment("XDG_CACHE_HOME");
        if (IsAbsolute(xdgCache))
        {
            return Path.Combine(xdgCache!, "dotnetscope");
        }

        return IsAbsolute(home)
            ? Path.Combine(home, ".cache", "dotnetscope")
            : throw new DotnetScopeException(ExitCodes.UnexpectedError, "Não foi possível determinar o diretório home do usuário.");
    }

    private static bool IsAbsolute(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);
}
