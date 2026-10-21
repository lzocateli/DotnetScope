using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DotnetScope.IntegrationTests;

/// <summary>
/// Servidor HTTP em loopback que publica as fixtures com URLs reescritas para si mesmo e registra as requisições.
/// </summary>
internal sealed class LocalMetadataServer : IDisposable
{
    private const string OfficialPrefix = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/";
    private readonly HttpListener _listener;
    private readonly Task _loop;

    public LocalMetadataServer()
    {
        // A porta livre pode ser tomada por outro teste em paralelo entre a sondagem e o Start; tenta novamente.
        for (var attempt = 1; ; attempt++)
        {
            var listener = new HttpListener();
            var url = new Uri($"http://127.0.0.1:{FreePort()}/");
            listener.Prefixes.Add(url.ToString());
            try
            {
                listener.Start();
                _listener = listener;
                BaseUrl = url;
                break;
            }
            catch (HttpListenerException) when (attempt < 20)
            {
                listener.Close();
            }
        }

        _loop = Task.Run(LoopAsync);
    }

    public Uri BaseUrl { get; }

    public Uri IndexUrl => new(BaseUrl, "releases-index.json");

    public ConcurrentQueue<string> Requests { get; } = new();

    /// <summary>Arquivos binários publicados por caminho, com suporte a <c>Range: bytes=N-</c>.</summary>
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();

    /// <summary>Quando definido, a próxima resposta de arquivo envia só esta quantidade de bytes e aborta a conexão.</summary>
    public int? InterruptAfterBytes { get; set; }

    /// <summary>Ignora o cabeçalho Range e responde sempre o arquivo completo.</summary>
    public bool IgnoreRange { get; set; }

    public ConcurrentQueue<string?> RangeHeaders { get; } = new();

    public Uri FileUrl(string path) => new(BaseUrl, path.TrimStart('/'));

    /// <summary>Respostas alternativas por caminho (por exemplo, "/releases-index.json").</summary>
    public ConcurrentDictionary<string, Func<HttpListenerResponse, Task>> Overrides { get; } = new();

    public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "metadata", name);

    public byte[] Fixture(string name) =>
        Encoding.UTF8.GetBytes(File.ReadAllText(FixturePath(name)).Replace(OfficialPrefix, BaseUrl.ToString(), StringComparison.Ordinal));

    public void Dispose()
    {
        // No Linux, Close() após Stop() pode tentar reassociar a porta ao remover prefixos; Close() já encerra.
        try
        {
            _listener.Close();
        }
        catch (HttpListenerException)
        {
        }

        try
        {
            _loop.GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is ObjectDisposedException or HttpListenerException)
        {
        }
    }

    private async Task ServeFileAsync(HttpListenerRequest request, HttpListenerResponse response, byte[] content)
    {
        var range = request.Headers["Range"];
        RangeHeaders.Enqueue(range);
        var start = 0;
        if (!IgnoreRange && range is not null && range.StartsWith("bytes=", StringComparison.Ordinal) && range.EndsWith('-'))
        {
            start = int.Parse(range[6..^1], System.Globalization.CultureInfo.InvariantCulture);
            if (start >= content.Length)
            {
                response.StatusCode = 416;
                response.Close();
                return;
            }

            response.StatusCode = 206;
            response.Headers["Content-Range"] = $"bytes {start}-{content.Length - 1}/{content.Length}";
        }

        response.ContentLength64 = content.Length - start;
        var interrupt = InterruptAfterBytes;
        InterruptAfterBytes = null;
        try
        {
            if (interrupt is not null)
            {
                await response.OutputStream.WriteAsync(content.AsMemory(start, Math.Min(interrupt.Value, content.Length - start)));
                await response.OutputStream.FlushAsync();
                response.Abort();
                return;
            }

            await response.OutputStream.WriteAsync(content.AsMemory(start));
            response.Close();
        }
        catch (HttpListenerException)
        {
            response.Abort();
        }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            try
            {
                await HandleAsync(context);
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or IOException or InvalidOperationException)
            {
                // O cliente pode encerrar a conexão (por exemplo, após recusar uma resposta); o servidor continua.
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var path = context.Request.Url!.AbsolutePath;
        Requests.Enqueue(path);
        var response = context.Response;
        if (Files.TryGetValue(path, out var content))
        {
            await ServeFileAsync(context.Request, response, content);
            return;
        }

        using var disposable = response;
        if (Overrides.TryGetValue(path, out var handler))
        {
            await handler(response);
            return;
        }

        var name = path switch
        {
            "/releases-index.json" => "releases-index.json",
            _ when path.EndsWith("/releases.json", StringComparison.Ordinal) => $"releases-{path.Trim('/').Split('/')[0]}.json",
            _ => null,
        };

        if (name is null || !File.Exists(FixturePath(name)))
        {
            response.StatusCode = 404;
            return;
        }

        var body = Fixture(name);
        response.ContentType = "application/json";
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body);
    }
}
