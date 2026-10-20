using System.Globalization;
using System.Text.Json;
using DotnetScope.Core;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;
using DotnetScope.Infrastructure.Http;

namespace DotnetScope.Infrastructure.Metadata;

/// <summary>Converte o metadata oficial em modelos do domínio, descartando entradas inválidas.</summary>
internal static class MetadataMapper
{
    public static IReadOnlyList<ReleaseChannel> ParseIndex(byte[] content, Uri source)
    {
        var document = Deserialize(content, MetadataJsonContext.Default.ReleasesIndexDocument, source);
        if (document.Entries is null)
        {
            throw Malformed(source);
        }

        var channels = new List<ReleaseChannel>();
        foreach (var entry in document.Entries)
        {
            if (!ChannelVersion.TryParse(entry.ChannelVersion, out var channel)
                || !Uri.TryCreate(entry.ReleasesJson, UriKind.Absolute, out var releasesUrl))
            {
                continue;
            }

            channels.Add(new ReleaseChannel(
                channel,
                ParseTrack(entry.ReleaseType),
                ParsePhase(entry.SupportPhase),
                entry.LatestRelease,
                DotnetVersion.TryParse(entry.LatestRuntime, out var runtime) ? runtime : null,
                DotnetVersion.TryParse(entry.LatestSdk, out var sdk) ? sdk : null,
                ParseDate(entry.EolDate),
                releasesUrl));
        }

        return channels.OrderByDescending(c => c.Channel).ToList();
    }

    public static ChannelReleases ParseChannel(byte[] content, ReleaseChannel channel, TrustedEndpoints endpoints)
    {
        var document = Deserialize(content, MetadataJsonContext.Default.ChannelReleasesDocument, channel.ReleasesUrl);
        if (document.Releases is null
            || !ChannelVersion.TryParse(document.ChannelVersion, out var documentChannel)
            || documentChannel != channel.Channel)
        {
            throw Malformed(channel.ReleasesUrl);
        }

        var releases = new List<ChannelRelease>();
        foreach (var release in document.Releases)
        {
            if (string.IsNullOrWhiteSpace(release.ReleaseVersion))
            {
                continue;
            }

            var components = new List<ComponentRelease>();
            var sdks = release.Sdks is { Count: > 0 } ? release.Sdks : release.Sdk is null ? [] : [release.Sdk];
            components.AddRange(sdks.Select(sdk => MapComponent(ComponentKind.Sdk, sdk, endpoints)).OfType<ComponentRelease>());
            AddIfValid(components, MapComponent(ComponentKind.Runtime, release.Runtime, endpoints));
            AddIfValid(components, MapComponent(ComponentKind.AspNetCore, release.AspNetCoreRuntime, endpoints));
            AddIfValid(components, MapComponent(ComponentKind.WindowsDesktop, release.WindowsDesktop, endpoints));

            releases.Add(new ChannelRelease(release.ReleaseVersion, ParseDate(release.ReleaseDate), release.Security, components));
        }

        return new ChannelReleases(channel, releases);
    }

    /// <summary>Normaliza SHA-512 hexadecimal; hashes ausentes, SHA-256 ou malformados retornam nulo.</summary>
    internal static string? NormalizeSha512(string? hash) =>
        hash is { Length: 128 } && hash.All(char.IsAsciiHexDigit) ? hash.ToLowerInvariant() : null;

    private static void AddIfValid(List<ComponentRelease> components, ComponentRelease? component)
    {
        if (component is not null)
        {
            components.Add(component);
        }
    }

    private static ComponentRelease? MapComponent(ComponentKind kind, ComponentDocument? document, TrustedEndpoints endpoints)
    {
        if (document is null || !DotnetVersion.TryParse(document.Version, out var version))
        {
            return null;
        }

        var files = (document.Files ?? [])
            .Where(file => !string.IsNullOrWhiteSpace(file.Name))
            .Select(file => Uri.TryCreate(file.Url, UriKind.Absolute, out var url)
                ? new ReleaseFile(file.Name!, file.Rid ?? string.Empty, url, NormalizeSha512(file.Hash), endpoints.IsTrusted(url))
                : null)
            .OfType<ReleaseFile>()
            .ToList();

        return new ComponentRelease(kind, version, files);
    }

    private static T Deserialize<T>(byte[] content, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, Uri source)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(content, typeInfo) ?? throw Malformed(source);
        }
        catch (JsonException exception)
        {
            throw Malformed(source, exception);
        }
    }

    private static DotnetScopeException Malformed(Uri source, Exception? inner = null) =>
        new(ExitCodes.Network, $"Metadata malformado ou inconsistente em {source}.", inner);

    private static ReleaseTrack ParseTrack(string? value) => value?.ToUpperInvariant() switch
    {
        "LTS" => ReleaseTrack.Lts,
        "STS" => ReleaseTrack.Sts,
        _ => ReleaseTrack.Unknown,
    };

    private static SupportPhase ParsePhase(string? value) => value?.ToUpperInvariant() switch
    {
        "PREVIEW" => SupportPhase.Preview,
        "GO-LIVE" => SupportPhase.GoLive,
        "ACTIVE" => SupportPhase.Active,
        "MAINTENANCE" => SupportPhase.Maintenance,
        "EOL" => SupportPhase.Eol,
        _ => SupportPhase.Unknown,
    };

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
