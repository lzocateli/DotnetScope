using System.Net;
using DotnetScope.Core;
using DotnetScope.Core.Releases;
using DotnetScope.Infrastructure.Http;
using DotnetScope.Infrastructure.Metadata;

namespace DotnetScope.IntegrationTests;

public sealed class ReleaseMetadataClientTests : IDisposable
{
    private readonly LocalMetadataServer _server = new();
    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly RecordingDiagnostics _diagnostics = new();

    [Fact]
    public async Task PrimeiraConsulta_BaixaEGravaCachePrivado()
    {
        using var client = CreateClient();

        var channels = await client.GetChannelsAsync(TestContext.Current.CancellationToken);
        var ten = channels.Single(c => c.Channel.ToString() == "10.0");
        var releases = await client.GetChannelReleasesAsync(ten, TestContext.Current.CancellationToken);

        Assert.Equal("10.0.13", releases.Releases[0].ReleaseVersion);
        Assert.Equal(["/releases-index.json", "/10.0/releases.json"], _server.Requests);
        Assert.True(File.Exists(Path.Combine(_cacheDirectory, "releases-index.json")));
        Assert.True(File.Exists(Path.Combine(_cacheDirectory, "releases-10.0.json")));
        Assert.Empty(Directory.EnumerateFiles(_cacheDirectory, "*.tmp", SearchOption.AllDirectories));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(_cacheDirectory));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(_cacheDirectory, "releases-index.json")));
        }
    }

    [Fact]
    public async Task CacheDentroDoTtl_NaoConsultaRede()
    {
        using (var first = CreateClient())
        {
            await first.GetChannelsAsync(TestContext.Current.CancellationToken);
        }

        _time.Advance(TimeSpan.FromMinutes(30));
        using var second = CreateClient();
        await second.GetChannelsAsync(TestContext.Current.CancellationToken);

        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task CacheExpirado_BaixaNovamente()
    {
        using (var first = CreateClient())
        {
            await first.GetChannelsAsync(TestContext.Current.CancellationToken);
        }

        _time.Advance(TimeSpan.FromHours(2));
        using var second = CreateClient();
        await second.GetChannelsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, _server.Requests.Count);
    }

    [Fact]
    public async Task RedeIndisponivelComCacheExpirado_UsaCacheComAviso()
    {
        using (var first = CreateClient())
        {
            await first.GetChannelsAsync(TestContext.Current.CancellationToken);
        }

        _time.Advance(TimeSpan.FromHours(2));
        _server.Overrides["/releases-index.json"] = response =>
        {
            response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            return Task.CompletedTask;
        };
        using var second = CreateClient();
        var channels = await second.GetChannelsAsync(TestContext.Current.CancellationToken);

        Assert.Contains(channels, c => c.Channel.ToString() == "10.0");
        Assert.Contains(_diagnostics.Warnings, w => w.Contains("Usando o metadata em cache", StringComparison.Ordinal));
        Assert.Equal(1 + TrustedHttpFetcher.MaxAttempts, _server.Requests.Count);
    }

    [Fact]
    public async Task MetadataMalformado_PreservaCacheValidoEUsaComAviso()
    {
        using (var first = CreateClient())
        {
            await first.GetChannelsAsync(TestContext.Current.CancellationToken);
        }

        var cachePath = Path.Combine(_cacheDirectory, "releases-index.json");
        var cached = await File.ReadAllBytesAsync(cachePath, TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromHours(2));
        ServeMalformedIndex();
        using var second = CreateClient();
        var channels = await second.GetChannelsAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(channels);
        Assert.Contains(_diagnostics.Warnings, w => w.Contains("malformado", StringComparison.Ordinal));
        Assert.Equal(cached, await File.ReadAllBytesAsync(cachePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MetadataMalformadoSemCache_FalhaSemGravarCache()
    {
        ServeMalformedIndex();
        using var client = CreateClient();

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => client.GetChannelsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Network, exception.ExitCode);
        Assert.False(File.Exists(Path.Combine(_cacheDirectory, "releases-index.json")));
    }

    [Fact]
    public async Task CacheNaoGravavel_RetornaDadosComAviso()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cacheDirectory)!);
        await File.WriteAllTextAsync(_cacheDirectory, "arquivo no lugar do diretório", TestContext.Current.CancellationToken);
        try
        {
            using var client = CreateClient();

            var channels = await client.GetChannelsAsync(TestContext.Current.CancellationToken);

            Assert.NotEmpty(channels);
            Assert.Contains(_diagnostics.Warnings, w => w.Contains("não foi possível gravar o cache", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(_cacheDirectory);
        }
    }

    [Fact]
    public async Task Offline_SemCache_FalhaSemRede()
    {
        using var client = CreateClient(offline: true);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => client.GetChannelsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Network, exception.ExitCode);
        Assert.Contains("--offline", exception.Message, StringComparison.Ordinal);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task Offline_ComCacheExpirado_UsaCacheSemRede()
    {
        using (var first = CreateClient())
        {
            await first.GetChannelsAsync(TestContext.Current.CancellationToken);
        }

        _time.Advance(TimeSpan.FromDays(30));
        using var offline = CreateClient(offline: true);
        await offline.GetChannelsAsync(TestContext.Current.CancellationToken);

        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task ReleasesJsonForaDaAllowlist_RecusaComErroDeIntegridade()
    {
        _server.Overrides["/releases-index.json"] = async response =>
        {
            var body = _server.Fixture("releases-index.json");
            var text = System.Text.Encoding.UTF8.GetString(body)
                .Replace(_server.BaseUrl + "10.0/releases.json", "http://example.invalid/10.0/releases.json", StringComparison.Ordinal);
            await response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(text));
        };
        using var client = CreateClient();
        var ten = (await client.GetChannelsAsync(TestContext.Current.CancellationToken)).Single(c => c.Channel.ToString() == "10.0");

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => client.GetChannelReleasesAsync(ten, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Integrity, exception.ExitCode);
        Assert.False(File.Exists(Path.Combine(_cacheDirectory, "releases-10.0.json")));
    }

    [Fact]
    public async Task RedirectParaForaDaAllowlist_NaoESeguido()
    {
        _server.Overrides["/releases-index.json"] = response =>
        {
            response.StatusCode = 302;
            response.RedirectLocation = "http://example.invalid/releases-index.json";
            return Task.CompletedTask;
        };
        using var client = CreateClient();

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => client.GetChannelsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Integrity, exception.ExitCode);
    }

    public void Dispose()
    {
        _server.Dispose();
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    private void ServeMalformedIndex() =>
        _server.Overrides["/releases-index.json"] = async response =>
        {
            response.StatusCode = 200;
            await response.OutputStream.WriteAsync("{ quebrado"u8.ToArray());
        };

    private ReleaseMetadataClient CreateClient(bool offline = false)
    {
        var endpoints = new TrustedEndpoints(["127.0.0.1"], requireHttps: false);
        var fetcher = new TrustedHttpFetcher(
            new SocketsHttpHandler { AllowAutoRedirect = false },
            endpoints,
            _diagnostics,
            delay: (_, _) => Task.CompletedTask);
        var cache = new MetadataCache(_cacheDirectory, _time, MetadataOptions.DefaultTtl);
        return new ReleaseMetadataClient(fetcher, cache, _server.IndexUrl, offline, _diagnostics);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class RecordingDiagnostics : IDiagnosticSink
    {
        public List<string> Warnings { get; } = [];

        public void Warning(string message) => Warnings.Add(message);

        public void Verbose(string message)
        {
        }
    }
}
