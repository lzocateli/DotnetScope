using System.Net;
using System.Security.Authentication;
using DotnetScope.Core;
using DotnetScope.Infrastructure.Http;

namespace DotnetScope.UnitTests;

public sealed class TrustedHttpFetcherTests
{
    private static readonly Uri Index = new("https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json");

    [Theory]
    [InlineData("https://builds.dotnet.microsoft.com/dotnet/x.json", true)]
    [InlineData("https://BUILDS.dotnet.microsoft.com/dotnet/x.json", true)]
    [InlineData("https://download.visualstudio.microsoft.com/x.zip", true)]
    [InlineData("http://builds.dotnet.microsoft.com/dotnet/x.json", false)]
    [InlineData("https://builds.dotnet.microsoft.com:8443/dotnet/x.json", false)]
    [InlineData("https://user:pw@builds.dotnet.microsoft.com/dotnet/x.json", false)]
    [InlineData("https://builds.dotnet.microsoft.com.example.invalid/x.json", false)]
    [InlineData("https://example.invalid/x.json", false)]
    [InlineData("file:///etc/passwd", false)]
    public void Official_AceitaSomenteHttpsNosHostsOficiais(string url, bool trusted) =>
        Assert.Equal(trusted, TrustedEndpoints.Official.IsTrusted(new Uri(url)));

    [Fact]
    public async Task GetBytesAsync_OrigemNaoConfiavel_RecusaSemRequisicao()
    {
        var handler = new ScriptedHandler();
        using var fetcher = Create(handler);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() =>
            fetcher.GetBytesAsync(new Uri("https://example.invalid/x.json"), TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Integrity, exception.ExitCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetBytesAsync_RedirectParaHostConfiavel_Segue()
    {
        var handler = new ScriptedHandler(
            _ => Redirect("https://download.visualstudio.microsoft.com/index.json"),
            _ => Ok("conteudo"));
        using var fetcher = Create(handler);

        var bytes = await fetcher.GetBytesAsync(Index, TestContext.Current.CancellationToken);

        Assert.Equal("conteudo"u8.ToArray(), bytes);
        Assert.Equal("download.visualstudio.microsoft.com", handler.Requests[1].Host);
    }

    [Theory]
    [InlineData("https://example.invalid/index.json")]
    [InlineData("http://builds.dotnet.microsoft.com/index.json")]
    public async Task GetBytesAsync_RedirectParaOrigemNaoConfiavel_Recusa(string location)
    {
        var handler = new ScriptedHandler(_ => Redirect(location));
        using var fetcher = Create(handler);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() =>
            fetcher.GetBytesAsync(Index, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Integrity, exception.ExitCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetBytesAsync_FalhasTransitorias_RepeteComBackoff()
    {
        var delays = new List<TimeSpan>();
        var handler = new ScriptedHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => throw new HttpRequestException("conexão recusada"),
            _ => Ok("ok"));
        using var fetcher = Create(handler, delays);

        var bytes = await fetcher.GetBytesAsync(Index, TestContext.Current.CancellationToken);

        Assert.Equal("ok"u8.ToArray(), bytes);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
    }

    [Fact]
    public async Task GetBytesAsync_FalhaTransitoriaPersistente_ErroDeRede()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var fetcher = Create(handler, []);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() =>
            fetcher.GetBytesAsync(Index, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Network, exception.ExitCode);
        Assert.Equal(TrustedHttpFetcher.MaxAttempts, handler.Requests.Count);
    }

    [Fact]
    public async Task GetBytesAsync_ErroDoCliente_NaoRepete()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var fetcher = Create(handler, []);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() =>
            fetcher.GetBytesAsync(Index, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Network, exception.ExitCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetBytesAsync_FalhaTls_NaoRepeteEOrientaSobreCa()
    {
        var handler = new ScriptedHandler(_ => throw new HttpRequestException("tls", new AuthenticationException("cert")));
        using var fetcher = Create(handler, []);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() =>
            fetcher.GetBytesAsync(Index, TestContext.Current.CancellationToken));

        Assert.Contains("SSL_CERT_DIR", exception.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetBytesAsync_RespostaAcimaDoLimite_Recusa(bool declareLength)
    {
        var handler = new ScriptedHandler(_ =>
        {
            var content = new StreamContent(new MemoryStream(new byte[2048]));
            if (declareLength)
            {
                content.Headers.ContentLength = 2048;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var fetcher = new TrustedHttpFetcher(handler, TrustedEndpoints.Official, NullDiagnosticSink.Instance, maxBytes: 1024);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() =>
            fetcher.GetBytesAsync(Index, TestContext.Current.CancellationToken));

        Assert.Contains("limite", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetBytesAsync_RedirectsExcessivos_Falha()
    {
        var handler = new ScriptedHandler(_ => Redirect(Index.ToString()));
        using var fetcher = Create(handler, []);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() =>
            fetcher.GetBytesAsync(Index, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Network, exception.ExitCode);
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task GetBytesAsync_CorpoTravado_EsgotaPrazoERepete()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) });
        using var fetcher = new TrustedHttpFetcher(
            handler,
            TrustedEndpoints.Official,
            NullDiagnosticSink.Instance,
            delay: (_, _) => Task.CompletedTask,
            attemptTimeout: TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() =>
            fetcher.GetBytesAsync(Index, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Network, exception.ExitCode);
        Assert.Contains("tempo limite", exception.Message, StringComparison.Ordinal);
        Assert.Equal(TrustedHttpFetcher.MaxAttempts, handler.Requests.Count);
    }

    [Fact]
    public async Task GetBytesAsync_CanceladoPeloUsuario_PropagaCancelamentoSemRepetir()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) });
        using var fetcher = Create(handler, []);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetcher.GetBytesAsync(Index, cancellation.Token));

        Assert.Single(handler.Requests);
    }

    private static TrustedHttpFetcher Create(ScriptedHandler handler, List<TimeSpan>? delays = null) =>
        new(handler, TrustedEndpoints.Official, NullDiagnosticSink.Instance, delay: (wait, _) =>
        {
            delays?.Add(wait);
            return Task.CompletedTask;
        });

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    }

    /// <summary>Corpo que nunca entrega dados, simulando servidor ou proxy travado após os cabeçalhos.</summary>
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] steps) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var step = steps[Math.Min(Requests.Count - 1, steps.Length - 1)];
            return Task.FromResult(step(request));
        }
    }
}
