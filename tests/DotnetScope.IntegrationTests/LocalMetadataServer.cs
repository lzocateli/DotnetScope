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
    private readonly HttpListener _listener = new();
    private readonly Task _loop;

    public LocalMetadataServer()
    {
        var port = FreePort();
        BaseUrl = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(BaseUrl.ToString());
        _listener.Start();
        _loop = Task.Run(LoopAsync);
    }

    public Uri BaseUrl { get; }

    public Uri IndexUrl => new(BaseUrl, "releases-index.json");

    public ConcurrentQueue<string> Requests { get; } = new();

    /// <summary>Respostas alternativas por caminho (por exemplo, "/releases-index.json").</summary>
    public ConcurrentDictionary<string, Func<HttpListenerResponse, Task>> Overrides { get; } = new();

    public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "metadata", name);

    public byte[] Fixture(string name) =>
        Encoding.UTF8.GetBytes(File.ReadAllText(FixturePath(name)).Replace(OfficialPrefix, BaseUrl.ToString(), StringComparison.Ordinal));

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
        try
        {
            _loop.GetAwaiter().GetResult();
        }
        catch (ObjectDisposedException)
        {
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

            var path = context.Request.Url!.AbsolutePath;
            Requests.Enqueue(path);
            using var response = context.Response;
            if (Overrides.TryGetValue(path, out var handler))
            {
                await handler(response);
                continue;
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
                continue;
            }

            var body = Fixture(name);
            response.ContentType = "application/json";
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body);
        }
    }
}
