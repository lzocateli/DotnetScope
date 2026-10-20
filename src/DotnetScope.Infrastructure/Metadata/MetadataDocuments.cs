using System.Text.Json.Serialization;

namespace DotnetScope.Infrastructure.Metadata;

// Esquema: https://json.schemastore.org/dotnet-releases-index.json e releases.json por canal.
internal sealed class ReleasesIndexDocument
{
    [JsonPropertyName("releases-index")]
    public List<ReleasesIndexEntry>? Entries { get; set; }
}

internal sealed class ReleasesIndexEntry
{
    [JsonPropertyName("channel-version")]
    public string? ChannelVersion { get; set; }

    [JsonPropertyName("latest-release")]
    public string? LatestRelease { get; set; }

    [JsonPropertyName("latest-runtime")]
    public string? LatestRuntime { get; set; }

    [JsonPropertyName("latest-sdk")]
    public string? LatestSdk { get; set; }

    [JsonPropertyName("support-phase")]
    public string? SupportPhase { get; set; }

    [JsonPropertyName("release-type")]
    public string? ReleaseType { get; set; }

    [JsonPropertyName("eol-date")]
    public string? EolDate { get; set; }

    [JsonPropertyName("releases.json")]
    public string? ReleasesJson { get; set; }
}

internal sealed class ChannelReleasesDocument
{
    [JsonPropertyName("channel-version")]
    public string? ChannelVersion { get; set; }

    [JsonPropertyName("releases")]
    public List<ReleaseEntryDocument>? Releases { get; set; }
}

internal sealed class ReleaseEntryDocument
{
    [JsonPropertyName("release-version")]
    public string? ReleaseVersion { get; set; }

    [JsonPropertyName("release-date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("security")]
    public bool Security { get; set; }

    [JsonPropertyName("runtime")]
    public ComponentDocument? Runtime { get; set; }

    [JsonPropertyName("sdk")]
    public ComponentDocument? Sdk { get; set; }

    [JsonPropertyName("sdks")]
    public List<ComponentDocument>? Sdks { get; set; }

    [JsonPropertyName("aspnetcore-runtime")]
    public ComponentDocument? AspNetCoreRuntime { get; set; }

    [JsonPropertyName("windowsdesktop")]
    public ComponentDocument? WindowsDesktop { get; set; }
}

internal sealed class ComponentDocument
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("files")]
    public List<FileDocument>? Files { get; set; }
}

internal sealed class FileDocument
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("rid")]
    public string? Rid { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("hash")]
    public string? Hash { get; set; }
}

[JsonSourceGenerationOptions(ReadCommentHandling = System.Text.Json.JsonCommentHandling.Disallow)]
[JsonSerializable(typeof(ReleasesIndexDocument))]
[JsonSerializable(typeof(ChannelReleasesDocument))]
internal sealed partial class MetadataJsonContext : JsonSerializerContext;
