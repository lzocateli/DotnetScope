namespace DotnetScope.Infrastructure.Http;

/// <summary>Allowlist de origens para metadata e downloads.</summary>
internal sealed class TrustedEndpoints
{
    private readonly HashSet<string> _hosts;
    private readonly bool _requireHttps;

    internal TrustedEndpoints(IEnumerable<string> hosts, bool requireHttps)
    {
        _hosts = new HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase);
        _requireHttps = requireHttps;
    }

    /// <summary>Hosts oficiais do metadata e dos artefatos .NET, somente via HTTPS na porta padrão.</summary>
    public static TrustedEndpoints Official { get; } =
        new(["builds.dotnet.microsoft.com", "download.visualstudio.microsoft.com"], requireHttps: true);

    public static Uri OfficialReleasesIndex { get; } =
        new("https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json");

    public bool IsTrusted(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.UserInfo.Length > 0 || !_hosts.Contains(uri.IdnHost))
        {
            return false;
        }

        return _requireHttps
            ? uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
            : uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }
}
