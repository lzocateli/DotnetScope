using DotnetScope.Core;
using DotnetScope.Core.Releases;
using DotnetScope.Infrastructure.Http;

namespace DotnetScope.Infrastructure.Metadata;

/// <summary>Opções de acesso ao metadata oficial.</summary>
public sealed record MetadataOptions(bool Offline, string? CacheDirectory = null)
{
    public static TimeSpan DefaultTtl { get; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Fonte do metadata oficial com cache local. Usa o cache dentro do TTL; ao expirar, baixa novamente e, se a rede
/// falhar, recorre ao cache expirado com aviso. Com <see cref="MetadataOptions.Offline"/>, usa somente o cache.
/// </summary>
public sealed class ReleaseMetadataClient : IReleaseMetadataSource, IDisposable
{
    private readonly TrustedHttpFetcher _fetcher;
    private readonly MetadataCache _cache;
    private readonly Uri _indexUrl;
    private readonly bool _offline;
    private readonly IDiagnosticSink _diagnostics;

    internal ReleaseMetadataClient(
        TrustedHttpFetcher fetcher,
        MetadataCache cache,
        Uri indexUrl,
        bool offline,
        IDiagnosticSink diagnostics)
    {
        _fetcher = fetcher;
        _cache = cache;
        _indexUrl = indexUrl;
        _offline = offline;
        _diagnostics = diagnostics;
    }

    public static ReleaseMetadataClient CreateOfficial(MetadataOptions options, IDiagnosticSink diagnostics)
    {
        ArgumentNullException.ThrowIfNull(options);
        var directory = options.CacheDirectory ?? Path.Combine(DotnetScopePaths.GetCacheDirectory(), "metadata");
        return new ReleaseMetadataClient(
            TrustedHttpFetcher.CreateOfficial(diagnostics),
            new MetadataCache(directory, TimeProvider.System, MetadataOptions.DefaultTtl),
            TrustedEndpoints.OfficialReleasesIndex,
            options.Offline,
            diagnostics);
    }

    public Task<IReadOnlyList<ReleaseChannel>> GetChannelsAsync(CancellationToken cancellationToken) =>
        GetAsync("releases-index.json", _indexUrl, content => MetadataMapper.ParseIndex(content, _indexUrl), cancellationToken);

    public Task<ChannelReleases> GetChannelReleasesAsync(ReleaseChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return GetAsync(
            $"releases-{channel.Channel}.json",
            channel.ReleasesUrl,
            content => MetadataMapper.ParseChannel(content, channel, _fetcher.Endpoints),
            cancellationToken);
    }

    public void Dispose() => _fetcher.Dispose();

    private async Task<T> GetAsync<T>(string key, Uri url, Func<byte[], T> parse, CancellationToken cancellationToken)
    {
        var cached = _cache.TryRead(key);
        if (_offline)
        {
            if (cached is null)
            {
                throw new DotnetScopeException(
                    ExitCodes.Network,
                    $"O metadata {key} não está em cache ({_cache.Directory}); execute sem --offline ao menos uma vez.");
            }

            _diagnostics.Verbose($"modo offline: usando {key} do cache de {cached.WrittenAtUtc:u}");
            return parse(cached.Content);
        }

        if (cached is { IsFresh: true } && TryParse(cached.Content, parse, out var fresh))
        {
            _diagnostics.Verbose($"cache válido: {key}");
            return fresh;
        }

        try
        {
            var content = await _fetcher.GetBytesAsync(url, cancellationToken).ConfigureAwait(false);
            var result = parse(content);
            TryWriteCache(key, content);
            return result;
        }
        catch (DotnetScopeException exception) when (exception.ExitCode == ExitCodes.Network
            && cached is not null
            && TryParse(cached.Content, parse, out var stale))
        {
            _diagnostics.Warning(
                $"{exception.Message} Usando o metadata em cache de {cached.WrittenAtUtc:yyyy-MM-dd HH:mm} UTC.");
            return stale;
        }
    }

    private void TryWriteCache(string key, byte[] content)
    {
        try
        {
            _cache.Write(key, content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _diagnostics.Warning($"não foi possível gravar o cache em {_cache.Directory}: {exception.Message}");
        }
    }

    private static bool TryParse<T>(byte[] content, Func<byte[], T> parse, out T result)
    {
        try
        {
            result = parse(content);
            return true;
        }
        catch (DotnetScopeException)
        {
            result = default!;
            return false;
        }
    }
}
