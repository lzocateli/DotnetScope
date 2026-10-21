using System.Net;
using System.Security.Authentication;
using DotnetScope.Core;
using DotnetScope.Core.Installation;

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
    private readonly long _maxDownloadBytes;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly IDiagnosticSink _diagnostics;
    private readonly TimeSpan _attemptTimeout;

    internal TrustedHttpFetcher(
        HttpMessageHandler handler,
        TrustedEndpoints endpoints,
        IDiagnosticSink diagnostics,
        long maxBytes = 64 * 1024 * 1024,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? attemptTimeout = null,
        long maxDownloadBytes = 2L * 1024 * 1024 * 1024)
    {
        _maxDownloadBytes = maxDownloadBytes;
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
        using var response = await SendAsync(url, rangeStart: null, cancellationToken).ConfigureAwait(false);
        return await ReadLimitedAsync(response.Content, response.RequestMessage?.RequestUri ?? url, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Envia GET seguindo redirects somente dentro da allowlist; o chamador descarta a resposta.</summary>
    private async Task<HttpResponseMessage> SendAsync(Uri url, long? rangeStart, CancellationToken cancellationToken)
    {
        var current = url;
        for (var redirect = 0; ; redirect++)
        {
            _diagnostics.Verbose($"GET {current}{(rangeStart is > 0 ? $" (a partir do byte {rangeStart})" : string.Empty)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            if (rangeStart is > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(rangeStart, null);
            }

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

            var keep = false;
            try
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

                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    throw new DotnetScopeException(ExitCodes.Network, $"Falha ao obter {current}: HTTP {status}.");
                }

                keep = true;
                return response;
            }
            finally
            {
                if (!keep)
                {
                    response.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Baixa <paramref name="url"/> para <paramref name="path"/> e verifica o SHA-512 antes de retornar. Falhas
    /// transitórias retomam do ponto interrompido com <c>Range</c> quando o servidor permite; hash divergente remove o
    /// arquivo e falha com <see cref="ExitCodes.Integrity"/>.
    /// </summary>
    public async Task DownloadToFileAsync(
        Uri url,
        string path,
        string expectedSha512,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(expectedSha512);
        EnsureTrusted(url);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                try
                {
                    await DownloadOnceAsync(url, path, progress, attemptCancellation, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TransientHttpException($"sem dados por {_attemptTimeout.TotalSeconds:0}s", exception);
                }
            }
            catch (TransientHttpException exception) when (attempt < MaxAttempts)
            {
                var wait = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                _diagnostics.Verbose($"download interrompido ({exception.Message}); nova tentativa em {wait.TotalSeconds:0}s");
                await _delay(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (TransientHttpException exception)
            {
                throw new DotnetScopeException(
                    ExitCodes.Network,
                    $"Falha ao baixar {url} após {MaxAttempts} tentativas: {exception.Message}",
                    exception.InnerException);
            }
        }

        await VerifySha512Async(path, expectedSha512, url, cancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadOnceAsync(
        Uri url,
        string path,
        IProgress<DownloadProgress>? progress,
        CancellationTokenSource idle,
        CancellationToken cancellationToken)
    {
        var existing = File.Exists(path) ? new FileInfo(path).Length : 0;
        idle.CancelAfter(_attemptTimeout);
        using var response = await SendAsync(url, existing, idle.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(path);
            throw new TransientHttpException("intervalo solicitado inválido; reiniciando o download");
        }

        var append = existing > 0
            && response.StatusCode == HttpStatusCode.PartialContent
            && response.Content.Headers.ContentRange?.From == existing;
        var received = append ? existing : 0;
        var total = response.Content.Headers.ContentLength + received;
        if (total > _maxDownloadBytes)
        {
            throw TooLarge(url, _maxDownloadBytes);
        }

        var options = new FileStreamOptions
        {
            Mode = append ? FileMode.Append : FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        var name = Path.GetFileName(path);
        var source = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            var target = new FileStream(path, options);
            await using (target.ConfigureAwait(false))
            {
                var buffer = new byte[128 * 1024];
                while (true)
                {
                    idle.CancelAfter(_attemptTimeout);
                    int read;
                    try
                    {
                        read = await source.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                    }
                    catch (IOException exception)
                    {
                        throw new TransientHttpException("conexão interrompida", exception);
                    }
                    catch (HttpRequestException exception)
                    {
                        throw new TransientHttpException("conexão interrompida", exception);
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    received += read;
                    if (received > _maxDownloadBytes)
                    {
                        throw TooLarge(url, _maxDownloadBytes);
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    progress?.Report(new DownloadProgress(name, received, total));
                }
            }
        }

        if (total is not null && received != total)
        {
            throw new TransientHttpException($"download incompleto ({received} de {total} bytes)");
        }
    }

    internal static async Task VerifySha512Async(string path, string expectedSha512, Uri url, CancellationToken cancellationToken)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedSha512);
        }
        catch (FormatException exception)
        {
            throw new DotnetScopeException(ExitCodes.Integrity, $"Hash SHA-512 inválido no metadata de {url}.", exception);
        }

        byte[] actual;
        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            actual = await System.Security.Cryptography.SHA512.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        if (expected.Length != actual.Length
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            File.Delete(path);
            throw new DotnetScopeException(
                ExitCodes.Integrity,
                $"O SHA-512 de {Path.GetFileName(path)} não corresponde ao metadata oficial; o arquivo foi descartado.");
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

    private DotnetScopeException TooLarge(Uri url) => TooLarge(url, _maxBytes);

    private static DotnetScopeException TooLarge(Uri url, long limit) =>
        new(ExitCodes.Network, $"A resposta de {url} excede o limite de {limit / (1024 * 1024)} MiB.");

    private sealed class TransientHttpException(string message, Exception? inner = null) : Exception(message, inner);
}
