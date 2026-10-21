using System.Formats.Tar;
using System.IO.Compression;
using DotnetScope.Core;

namespace DotnetScope.Infrastructure.Installation;

/// <summary>
/// Extrai <c>.tar.gz</c> aceitando somente diretórios e arquivos regulares com caminhos relativos confinados ao destino.
/// Links, dispositivos, FIFOs, caminhos absolutos, <c>..</c>, nomes duplicados e arquivos acima dos limites são recusados.
/// </summary>
internal sealed class SafeTarExtractor(long maxTotalBytes = 8L * 1024 * 1024 * 1024, int maxEntries = 200_000)
{
    private const UnixFileMode PermissionBits =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    public async Task ExtractAsync(string archivePath, string destination, CancellationToken cancellationToken)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidOperationException($"O destino da extração precisa estar vazio: {root}.");
        }

        Directory.CreateDirectory(root);
        var rootPrefix = root + Path.DirectorySeparatorChar;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = 0;
        long totalBytes = 0;

        var file = File.OpenRead(archivePath);
        await using (file.ConfigureAwait(false))
        {
            var gzip = new GZipStream(file, CompressionMode.Decompress);
            await using (gzip.ConfigureAwait(false))
            {
                var reader = new TarReader(gzip);
                await using (reader.ConfigureAwait(false))
                {
                    try
                    {
                        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is { } entry)
                        {
                            if (entry.EntryType == TarEntryType.GlobalExtendedAttributes)
                            {
                                continue;
                            }

                            if (++entries > maxEntries)
                            {
                                throw Rejected($"o arquivo excede {maxEntries} entradas");
                            }

                            var name = NormalizeName(entry.Name);
                            if (name is null)
                            {
                                continue;
                            }

                            if (!seen.Add(name))
                            {
                                throw Rejected($"entrada duplicada: {name}");
                            }

                            var fullPath = Path.GetFullPath(Path.Combine(root, name));
                            if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal))
                            {
                                throw Rejected($"caminho fora do destino: {name}");
                            }

                            switch (entry.EntryType)
                            {
                                case TarEntryType.Directory:
                                    CreateDirectory(fullPath, entry.Mode);
                                    break;
                                case TarEntryType.RegularFile:
                                case TarEntryType.V7RegularFile:
                                    totalBytes += entry.Length;
                                    if (entry.Length < 0 || totalBytes > maxTotalBytes)
                                    {
                                        throw Rejected($"o conteúdo extraído excede {maxTotalBytes / (1024 * 1024)} MiB");
                                    }

                                    await WriteFileAsync(entry, fullPath, cancellationToken).ConfigureAwait(false);
                                    break;
                                default:
                                    throw Rejected($"tipo de entrada não permitido ({entry.EntryType}): {name}");
                            }
                        }
                    }
                    catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or FormatException)
                    {
                        throw Rejected("arquivo corrompido ou truncado", exception);
                    }
                }
            }
        }
    }

    /// <summary>Normaliza o nome da entrada; retorna nulo para a raiz (<c>.</c>).</summary>
    internal static string? NormalizeName(string name)
    {
        if (name.Contains('\0', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal))
        {
            throw Rejected($"nome de entrada inválido: {name.Replace('\0', '?')}");
        }

        if (name.StartsWith('/'))
        {
            throw Rejected($"caminho absoluto: {name}");
        }

        while (name.StartsWith("./", StringComparison.Ordinal))
        {
            name = name[2..];
        }

        name = name.TrimEnd('/');
        if (name.Length == 0 || name == ".")
        {
            return null;
        }

        var segments = name.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            throw Rejected($"segmento de caminho inválido: {name}");
        }

        return name;
    }

    private static void CreateDirectory(string path, UnixFileMode mode)
    {
        if (File.Exists(path))
        {
            throw Rejected($"diretório conflita com arquivo: {path}");
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(path);
                return;
            }

            Directory.CreateDirectory(path, (mode & PermissionBits) | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (IOException exception) when (AnyAncestorIsFile(path))
        {
            throw Rejected($"arquivo usado como diretório: {path}", exception);
        }
    }

    private static bool AnyAncestorIsFile(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (File.Exists(current))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task WriteFileAsync(TarEntry entry, string path, CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(path)!;
        if (AnyAncestorIsFile(parent))
        {
            throw Rejected($"arquivo usado como diretório: {parent}");
        }

        Directory.CreateDirectory(parent);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            // Bits setuid/setgid/sticky nunca são preservados.
            options.UnixCreateMode = (entry.Mode & PermissionBits) | UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        var target = new FileStream(path, options);
        await using (target.ConfigureAwait(false))
        {
            if (entry.DataStream is not null)
            {
                await entry.DataStream.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            if (target.Length != entry.Length)
            {
                throw Rejected($"tamanho inconsistente: {path}");
            }
        }
    }

    private static DotnetScopeException Rejected(string reason, Exception? inner = null) =>
        new(ExitCodes.Integrity, $"Arquivo recusado na extração: {reason}.", inner);
}
