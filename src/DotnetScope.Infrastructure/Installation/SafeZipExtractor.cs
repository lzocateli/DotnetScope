using System.IO.Compression;
using DotnetScope.Core;

namespace DotnetScope.Infrastructure.Installation;

/// <summary>Extração segura de um arquivo para um diretório vazio.</summary>
internal interface IArchiveExtractor
{
    Task ExtractAsync(string archivePath, string destination, CancellationToken cancellationToken);
}

/// <summary>
/// Extrai <c>.zip</c> aceitando somente diretórios e arquivos com caminhos relativos confinados ao destino.
/// Recusa caminhos absolutos ou com letra de unidade, <c>..</c>, <c>\</c>, <c>:</c> (fluxos alternativos do NTFS),
/// nomes reservados do Windows, segmentos terminados em ponto ou espaço, nomes duplicados sem distinção de
/// maiúsculas, links simbólicos, entradas criptografadas e conteúdo acima dos limites (medido nos bytes reais).
/// </summary>
internal sealed class SafeZipExtractor(long maxTotalBytes = 8L * 1024 * 1024 * 1024, int maxEntries = 200_000) : IArchiveExtractor
{
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymlink = 0xA000;
    private const int UnixRegularFile = 0x8000;
    private const int UnixDirectory = 0x4000;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    public async Task ExtractAsync(string archivePath, string destination, CancellationToken cancellationToken)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidOperationException($"O destino da extração precisa estar vazio: {root}.");
        }

        Directory.CreateDirectory(root);
        var rootPrefix = root + Path.DirectorySeparatorChar;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count > maxEntries)
            {
                throw Rejected($"o arquivo excede {maxEntries} entradas");
            }

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isDirectory = entry.FullName.EndsWith('/');
                var name = NormalizeName(entry.FullName);
                if (name is null)
                {
                    continue;
                }

                if (!seen.Add(name))
                {
                    throw Rejected($"entrada duplicada: {name}");
                }

                if (entry.IsEncrypted)
                {
                    throw Rejected($"entrada criptografada: {name}");
                }

                var fileType = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
                if (fileType != 0 && fileType != UnixRegularFile && fileType != UnixDirectory)
                {
                    throw Rejected(fileType == UnixSymlink ? $"link simbólico não permitido: {name}" : $"tipo de entrada não permitido: {name}");
                }

                // Um arquivo não pode ser usado como diretório de outra entrada (nem o inverso).
                for (var slash = name.LastIndexOf('/'); slash > 0; slash = name.LastIndexOf('/', slash - 1))
                {
                    var parent = name[..slash];
                    if (files.Contains(parent))
                    {
                        throw Rejected($"arquivo usado como diretório: {parent}");
                    }

                    directories.Add(parent);
                }

                var fullPath = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
                if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal))
                {
                    throw Rejected($"caminho fora do destino: {name}");
                }

                if (isDirectory || fileType == UnixDirectory)
                {
                    directories.Add(name);
                    Directory.CreateDirectory(fullPath);
                    continue;
                }

                if (directories.Contains(name))
                {
                    throw Rejected($"arquivo usado como diretório: {name}");
                }

                files.Add(name);
                totalBytes = await WriteFileAsync(entry, fullPath, totalBytes, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            throw Rejected("arquivo corrompido ou truncado", exception);
        }
    }

    /// <summary>Normaliza o nome da entrada; retorna nulo para a raiz.</summary>
    internal static string? NormalizeName(string name)
    {
        if (name.Contains('\0', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal) || name.Contains(':', StringComparison.Ordinal))
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

        foreach (var segment in name.Split('/'))
        {
            if (segment is "" or "." or "..")
            {
                throw Rejected($"segmento de caminho inválido: {name}");
            }

            // O Windows remove ponto e espaço finais, o que faria dois nomes distintos apontarem para o mesmo arquivo.
            if (segment.EndsWith('.') || segment.EndsWith(' '))
            {
                throw Rejected($"segmento terminado em ponto ou espaço: {name}");
            }

            if (segment.Any(c => c < 0x20 || c is '<' or '>' or '"' or '|' or '?' or '*'))
            {
                throw Rejected($"caractere inválido no Windows: {name}");
            }

            var stem = segment.Split('.')[0].TrimEnd(' ');
            if (ReservedNames.Contains(stem))
            {
                throw Rejected($"nome reservado do Windows: {name}");
            }
        }

        return name;
    }

    private async Task<long> WriteFileAsync(ZipArchiveEntry entry, string path, long totalBytes, CancellationToken cancellationToken)
    {
        if (entry.Length < 0 || totalBytes + entry.Length > maxTotalBytes)
        {
            throw Rejected($"o conteúdo extraído excede {maxTotalBytes / (1024 * 1024)} MiB");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            var mode = (UnixFileMode)((entry.ExternalAttributes >> 16) & 0x1FF);
            options.UnixCreateMode = (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute))
                | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        }

        var source = entry.Open();
        await using (source.ConfigureAwait(false))
        {
            var target = new FileStream(path, options);
            await using (target.ConfigureAwait(false))
            {
                // O tamanho declarado no cabeçalho não é confiável: a cópia é limitada pelos bytes realmente lidos.
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > entry.Length || totalBytes + written > maxTotalBytes)
                    {
                        throw Rejected($"tamanho inconsistente ou acima do limite: {entry.FullName}");
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                if (written != entry.Length)
                {
                    throw Rejected($"tamanho inconsistente: {entry.FullName}");
                }

                return totalBytes + written;
            }
        }
    }

    private static DotnetScopeException Rejected(string reason, Exception? inner = null) =>
        new(ExitCodes.Integrity, $"Arquivo recusado na extração: {reason}.", inner);
}
