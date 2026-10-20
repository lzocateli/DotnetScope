using System.Text.Json.Serialization;

namespace DotnetScope.Cli.Output;

// Contrato estável de `list --available --output json`; documentado em docs/cli.md.
internal sealed record ChannelsJson(IReadOnlyList<ChannelJson> Channels);

internal sealed record ChannelJson(
    string Channel,
    string ReleaseType,
    string SupportPhase,
    string? LatestRelease,
    string? LatestRuntime,
    string? LatestSdk,
    string? EolDate);

internal sealed record VersionsJson(
    string Component,
    string Request,
    string Rid,
    ChannelJson Channel,
    IReadOnlyList<VersionJson> Versions,
    ResolvedJson? Resolved);

internal sealed record VersionJson(
    string Version,
    string ReleaseVersion,
    string? ReleaseDate,
    bool Security,
    bool ArchiveAvailable);

internal sealed record ResolvedJson(string Version, string FileName, string Url, string Sha512);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(ChannelsJson))]
[JsonSerializable(typeof(VersionsJson))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
