using DotnetScope.Core;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.Platform;

namespace DotnetScope.Infrastructure.SystemScope;

/// <summary>
/// Regras de caminho do escopo global no Linux (modelo de ameaças, R05): diretórios em que o processo elevado grava
/// precisam, junto com todos os ancestrais existentes, pertencer ao root e não aceitar escrita de grupo ou outros.
/// Caso contrário um usuário comum poderia trocar arquivos ou plantar binários executados por todos.
/// </summary>
public static class GlobalLocations
{
    private const int GroupOrOtherWrite = 0b000_010_010;

    private static readonly string[] ForbiddenExact =
    [
        "/", "/bin", "/sbin", "/lib", "/lib64", "/usr", "/usr/bin", "/usr/sbin", "/usr/lib", "/usr/lib64", "/usr/local",
        "/usr/local/bin", "/usr/share", "/opt", "/var", "/var/lib", "/home", "/root", "/srv", "/mnt", "/media", "/tmp",
    ];

    private static readonly string[] ForbiddenTrees = ["/proc", "/sys", "/dev", "/etc", "/boot", "/run"];

    // Raízes dos pacotes .NET das distribuições (apt, dnf); misturar instalação por arquivo nelas corrompe o pacote.
    private static readonly string[] PackageRoots = ["/usr/lib/dotnet", "/usr/share/dotnet", "/usr/lib64/dotnet"];

    /// <summary>Normaliza <c>--install-dir</c> do escopo global e recusa diretórios do sistema (código 7).</summary>
    public static string ValidateRoot(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new DotnetScopeException(ExitCodes.Usage, $"--install-dir precisa ser um caminho absoluto: {path}.");
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        foreach (var candidate in new[] { normalized, InstallLocations.ResolveLinks(normalized) }.Distinct(StringComparer.Ordinal))
        {
            if (PackageRoots.Contains(candidate, StringComparer.Ordinal))
            {
                throw new DotnetScopeException(
                    ExitCodes.Conflict,
                    $"{candidate} é a raiz dos pacotes .NET da distribuição; use o gerenciador de pacotes ou outro --install-dir.");
            }

            if (ForbiddenExact.Contains(candidate, StringComparer.Ordinal)
                || ForbiddenTrees.Any(tree => candidate == tree || candidate.StartsWith(tree + "/", StringComparison.Ordinal)))
            {
                throw new DotnetScopeException(
                    ExitCodes.Privilege,
                    $"--install-dir {path} aponta para um diretório do sistema; use um subdiretório dedicado, como /usr/local/share/dotnet.");
            }
        }

        return normalized;
    }

    /// <summary>
    /// Exige que <paramref name="path"/> e todos os ancestrais existentes, antes e depois de resolver links,
    /// pertençam ao root (UID 0) e não sejam graváveis por grupo ou outros. Falha com código 7.
    /// </summary>
    public static void EnsureTrusted(string path, Func<string, UnixFileStatus?> stat, string purpose)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(stat);
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        foreach (var candidate in new[] { normalized, InstallLocations.ResolveLinks(normalized) }.Distinct(StringComparer.Ordinal))
        {
            foreach (var prefix in Prefixes(candidate))
            {
                if (stat(prefix) is not { } status)
                {
                    break;
                }

                if (status.Uid != 0)
                {
                    throw Untrusted(prefix, purpose, $"pertence ao UID {status.Uid}, não ao root");
                }

                if ((status.Mode & GroupOrOtherWrite) != 0)
                {
                    throw Untrusted(prefix, purpose, $"aceita escrita de grupo ou outros (modo {Convert.ToString(status.Mode, 8)})");
                }
            }
        }
    }

    /// <summary>
    /// Exige que o arquivo de estado (manifesto, estado de ambiente, lock), se existir, não seja link simbólico, pertença
    /// ao root e não aceite escrita de grupo ou outros. Estado com dono inesperado é recusado, nunca usado (código 6).
    /// </summary>
    public static void EnsureTrustedFile(string path, Func<string, UnixFileStatus?> stat)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(stat);
        var info = new FileInfo(path);
        if (info.LinkTarget is not null)
        {
            throw new DotnetScopeException(ExitCodes.Conflict, $"{path} é um link simbólico; o estado global não é usado por links.");
        }

        if (stat(path) is { } status && (status.Uid != 0 || (status.Mode & GroupOrOtherWrite) != 0))
        {
            throw new DotnetScopeException(
                ExitCodes.Conflict,
                $"{path} não pertence ao root ou aceita escrita de outros (UID {status.Uid}, modo {Convert.ToString(status.Mode, 8)}); o estado global não será usado.");
        }
    }

    /// <summary>Cria o diretório (e ancestrais ausentes) com modo 0755, independentemente do umask.</summary>
    public static void CreateDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        var parent = Path.GetDirectoryName(path);
        if (parent is not null && !Directory.Exists(parent))
        {
            CreateDirectory(parent);
        }

        if (!Directory.Exists(path))
        {
            const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            Directory.CreateDirectory(path, mode);
            File.SetUnixFileMode(path, mode);
        }
    }

    private static IEnumerable<string> Prefixes(string path)
    {
        yield return "/";
        var current = string.Empty;
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + segment;
            yield return current;
        }
    }

    private static DotnetScopeException Untrusted(string path, string purpose, string reason) =>
        new(ExitCodes.Privilege, $"{path} {reason}; ele não é seguro para {purpose} no escopo global. Ajuste o dono e as permissões ou use outro diretório.");
}
