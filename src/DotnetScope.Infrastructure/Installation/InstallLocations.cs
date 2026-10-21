using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;

namespace DotnetScope.Infrastructure.Installation;

/// <summary>Raiz padrão e validação de <c>--install-dir</c> no escopo user.</summary>
public static class InstallLocations
{
    private static readonly string[] SystemPrefixes =
    [
        "/bin", "/boot", "/dev", "/etc", "/lib", "/lib32", "/lib64", "/libx32", "/opt", "/proc", "/root", "/run",
        "/sbin", "/srv", "/sys", "/usr", "/var", "/snap", "/System", "/Library", "/Applications", "/private/etc",
        "/private/var/db", "/cores",
    ];

    private static readonly string[] HomeContainers = ["/home", "/Users"];

    // Diretórios temporários por usuário no macOS, que ficam sob /var.
    private static readonly string[] AllowedExceptions = ["/var/folders", "/private/var/folders"];

    /// <summary>Indica se a pasta versionada do componente já existe na raiz, qualquer que seja a origem.</summary>
    public static bool IsInstalled(string root, ComponentKind component, DotnetVersion version) =>
        LayoutMerger.EntryExists(Path.Combine(root, DotnetLayout.PrimaryPath(component, version)));

    /// <summary>Compara raízes normalizadas; no Windows, sem distinção de maiúsculas.</summary>
    public static bool SameRoot(string left, string right) =>
        string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static string DefaultUserRoot(string home) =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet")
            : Path.Combine(home, ".dotnet");

    /// <summary>
    /// Normaliza e valida a raiz do escopo user: precisa ser absoluta, não pode ser a raiz do sistema, o próprio home,
    /// um diretório do sistema nem o home de outro usuário, inclusive após resolver links simbólicos.
    /// </summary>
    public static string ValidateUserRoot(string path, string home)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new DotnetScopeException(ExitCodes.Usage, $"--install-dir precisa ser um caminho absoluto: {path}.");
        }

        if (OperatingSystem.IsWindows())
        {
            return ValidateWindowsRoot(path, home);
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var normalizedHome = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));
        EnsureAllowed(normalized, normalized, normalizedHome);
        var resolved = ResolveLinks(normalized);
        if (!string.Equals(resolved, normalized, StringComparison.Ordinal))
        {
            EnsureAllowed(normalized, resolved, ResolveLinks(normalizedHome));
        }

        return normalized;
    }

    private static string ValidateWindowsRoot(string path, string home)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            throw Denied(path, "um caminho de rede ou de dispositivo");
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var normalizedHome = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));
        var protectedDirectories = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetEnvironmentVariable("ProgramW6432") ?? string.Empty,
            }
            .Where(d => d.Length > 0)
            .Select(Path.TrimEndingDirectorySeparator)
            .ToList();
        var usersDirectory = Path.GetDirectoryName(normalizedHome);

        EnsureAllowedWindows(path, normalized, normalizedHome, protectedDirectories, usersDirectory);
        var resolved = ResolveWindowsLinks(normalized);
        if (!string.Equals(resolved, normalized, StringComparison.OrdinalIgnoreCase))
        {
            EnsureAllowedWindows(path, resolved, ResolveWindowsLinks(normalizedHome), protectedDirectories, usersDirectory);
        }

        return normalized;
    }

    /// <summary>
    /// Regras do Windows sobre caminhos já normalizados (sem barra final): recusa a raiz de uma unidade, o próprio
    /// perfil, diretórios do sistema e de programas e perfis de outros usuários; subdiretórios do perfil são permitidos.
    /// </summary>
    internal static void EnsureAllowedWindows(
        string requested,
        string path,
        string home,
        IReadOnlyList<string> protectedDirectories,
        string? usersDirectory)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(protectedDirectories);
        if ((path.Length <= 3 && path.Length >= 2 && path[1] == ':') || string.Equals(path, home, StringComparison.OrdinalIgnoreCase))
        {
            throw Denied(requested, "a raiz de uma unidade ou o próprio perfil do usuário");
        }

        if (IsUnderWindows(path, home))
        {
            return;
        }

        if (protectedDirectories.Any(directory => IsUnderWindows(path, directory)))
        {
            throw Denied(requested, "um diretório do sistema ou de programas");
        }

        if (usersDirectory is not null && IsUnderWindows(path, usersDirectory))
        {
            throw Denied(requested, "o perfil de outro usuário");
        }
    }

    private static bool IsUnderWindows(string path, string prefix)
    {
        var trimmed = prefix.TrimEnd('\\');
        return string.Equals(path, trimmed, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(trimmed + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resolve junções e links simbólicos componente a componente até o ancestral existente mais profundo.</summary>
    private static string ResolveWindowsLinks(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        var current = root;
        foreach (var segment in path[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            var info = new DirectoryInfo(next);
            if (info.LinkTarget is not null)
            {
                next = Path.TrimEndingDirectorySeparator(Path.GetFullPath(info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next));
            }

            current = next;
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    private static void EnsureAllowed(string requested, string path, string home)
    {
        if (path == "/" || string.Equals(path, home, StringComparison.Ordinal))
        {
            throw Denied(requested, "a raiz do sistema ou o próprio diretório home");
        }

        // Subdiretórios do próprio home são sempre permitidos, inclusive /root/.dotnet com --allow-root.
        if (home != "/" && IsUnder(path, home))
        {
            return;
        }

        if (SystemPrefixes.Any(prefix => IsUnder(path, prefix)) && !AllowedExceptions.Any(prefix => IsUnder(path, prefix)))
        {
            throw Denied(requested, "um diretório do sistema");
        }

        if (HomeContainers.Any(container => IsUnder(path, container)) && !IsUnder(path, home))
        {
            throw Denied(requested, "o diretório de outro usuário");
        }
    }

    private static bool IsUnder(string path, string prefix) =>
        string.Equals(path, prefix, StringComparison.Ordinal)
        || path.StartsWith(prefix + "/", StringComparison.Ordinal);

    /// <summary>Resolve links simbólicos componente a componente até o ancestral existente mais profundo.</summary>
    internal static string ResolveLinks(string path)
    {
        var current = "/";
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            var info = new DirectoryInfo(next);
            if (info.LinkTarget is not null)
            {
                next = Path.TrimEndingDirectorySeparator(Path.GetFullPath(info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next));
            }

            current = next;
        }

        return current;
    }

    private static DotnetScopeException Denied(string path, string reason) =>
        new(ExitCodes.Privilege, $"--install-dir {path} aponta para {reason}; o escopo user não grava nesse local.");
}
