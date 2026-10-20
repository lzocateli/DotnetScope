using System.Net;
using System.Security.Authentication;
using DotnetScope.Core;

namespace DotnetScope.Infrastructure.Http;

/// <summary>
/// Cliente HTTP restrito à allowlist: segue redirects manualmente, limita o tamanho da resposta e repete apenas
/// falhas transitórias. Proxy (HTTPS_PROXY/NO_PROXY) e CAs do sistema (SSL_CERT_DIR/SSL_CERT_FILE) são respeitados.
/// </summary>
internal sealed class TrustedHttpFetcher : IDisposable
{
    internal const int MaxAttempts = 3;
    private const int MaxRedirects = 5;

    private readonly HttpClient _client;
    private readonly TrustedEndpoints _endpoints;
    private readonly long _maxBytes;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly IDiagnosticSink _diagnostics;
    private readonly TimeSpan _attemptTimeout;

    internal TrustedHttpFetcher(
        HttpMessageHandler handler,
        TrustedEndpoints endpoints,
        IDiagnosticSink diagnostics,
        long maxBytes = 64 * 1024 * 1024,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? attemptTimeout = null)
    {
        // O prazo de cada tentativa cobre cabeçalhos e corpo; HttpClient.Timeout cobriria só até os cabeçalhos.
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _attemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(60);
        var version = typeof(TrustedHttpFetcher).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"dotnetscope/{version}");
        _endpoints = endpoints;
        _diagnostics = diagnostics;
        _maxBytes = maxBytes;
        _delay = delay ?? Task.Delay;
    }

    public static TrustedHttpFetcher CreateOfficial(IDiagnosticSink diagnostics) =>
        new(
            new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(20),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            },
            TrustedEndpoints.Official,
            diagnostics);

    public TrustedEndpoints Endpoints => _endpoints;

    public async Task<byte[]> GetBytesAsync(Uri url, CancellationToken cancellationToken)
    {
        EnsureTrusted(url);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCancellation.CancelAfter(_attemptTimeout);
                try
                {
                    return await GetOnceAsync(url, attemptCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TransientHttpException($"tempo limite de {_attemptTimeout.TotalSeconds:0}s esgotado", exception);
                }
            }
            catch (TransientHttpException exception) when (attempt < MaxAttempts)
            {
                var wait = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                _diagnostics.Verbose($"falha transitória em {url} ({exception.Message}); nova tentativa em {wait.TotalSeconds:0}s");
                await _delay(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (TransientHttpException exception)
            {
                throw new DotnetScopeException(
                    ExitCodes.Network,
                    $"Falha ao obter {url} após {MaxAttempts} tentativas: {exception.Message}",
                    exception.InnerException);
            }
        }
    }

    public void Dispose() => _client.Dispose();

    private async Task<byte[]> GetOnceAsync(Uri url, CancellationToken cancellationToken)
    {
        var current = url;
        for (var redirect = 0; ; redirect++)
        {
            _diagnostics.Verbose($"GET {current}");
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (exception.InnerException is AuthenticationException)
            {
                throw new DotnetScopeException(
                    ExitCodes.Network,
                    $"Falha TLS ao acessar {current}. Em redes com inspeção TLS, exponha a CA corporativa via SSL_CERT_DIR ou SSL_CERT_FILE.",
                    exception);
            }
            catch (HttpRequestException exception)
            {
                throw new TransientHttpException(exception.Message, exception);
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (redirect >= MaxRedirects || response.Headers.Location is null)
                    {
                        throw new DotnetScopeException(ExitCodes.Network, $"Redirecionamentos excessivos ou inválidos a partir de {url}.");
                    }

                    current = new Uri(current, response.Headers.Location);
                    EnsureTrusted(current);
                    continue;
                }

                var status = (int)response.StatusCode;
                if (status is 408 or 429 or >= 500)
                {
                    throw new TransientHttpException($"HTTP {status}");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new DotnetScopeException(ExitCodes.Network, $"Falha ao obter {current}: HTTP {status}.");
                }

                return await ReadLimitedAsync(response.Content, current, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<byte[]> ReadLimitedAsync(HttpContent content, Uri url, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > _maxBytes)
        {
            throw TooLarge(url);
        }

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            try
            {
                while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > _maxBytes)
                    {
                        throw TooLarge(url);
                    }

                    buffer.Write(chunk, 0, read);
                }
            }
            catch (IOException exception)
            {
                throw new TransientHttpException("conexão interrompida", exception);
            }

            return buffer.ToArray();
        }
    }

    private void EnsureTrusted(Uri url)
    {
        if (!_endpoints.IsTrusted(url))
        {
            throw new DotnetScopeException(ExitCodes.Integrity, $"Origem fora da allowlist recusada: {url}.");
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private DotnetScopeException TooLarge(Uri url) =>
        new(ExitCodes.Network, $"A resposta de {url} excede o limite de {_maxBytes / (1024 * 1024)} MiB.");

    private sealed class TransientHttpException(string message, Exception? inner = null) : Exception(message, inner);
}
