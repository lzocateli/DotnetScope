namespace DotnetScope.Infrastructure.Metadata;

/// <summary>Cache local do metadata em diretório privado, com gravação atômica e expiração por TTL.</summary>
internal sealed class MetadataCache(string directory, TimeProvider timeProvider, TimeSpan ttl)
{
    public string Directory { get; } = Path.GetFullPath(directory);

    public CachedDocument? TryRead(string key)
    {
        var path = PathFor(key);
        try
        {
            // FileShare.Delete permite que outro processo substitua o arquivo durante a leitura (Windows).
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var writtenAt = File.GetLastWriteTimeUtc(path);
            var age = timeProvider.GetUtcNow().UtcDateTime - writtenAt;
            return new CachedDocument(buffer.ToArray(), writtenAt, IsFresh: age >= TimeSpan.Zero && age < ttl);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Write(string key, byte[] content)
    {
        EnsurePrivateDirectory(Directory);
        var path = PathFor(key);
        var temporary = Path.Combine(Directory, $".{key}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = OpenPrivateFile(temporary))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private string PathFor(string key)
    {
        if (key.Length == 0 || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || key.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Chave de cache inválida: '{key}'.", nameof(key));
        }

        return Path.Combine(Directory, key);
    }

    private static void EnsurePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            System.IO.Directory.CreateDirectory(path);
            return;
        }

        System.IO.Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static FileStream OpenPrivateFile(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(path, options);
    }
}

internal sealed record CachedDocument(byte[] Content, DateTime WrittenAtUtc, bool IsFresh);
