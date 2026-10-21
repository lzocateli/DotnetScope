using System.Text.Json.Serialization;

namespace DotnetScope.Infrastructure.Installation;

// Formato persistido em manifest.json; mudanças exigem compatibilidade com versões anteriores.
internal sealed record ManifestDocument(int SchemaVersion, List<ManifestEntry> Installations);

internal sealed record ManifestEntry(
    string Component,
    string Version,
    string Rid,
    string Scope,
    string Root,
    string FileName,
    string Url,
    string Sha512,
    DateTimeOffset InstalledAt,
    List<string> Paths,
    List<string> AddedPaths,
    bool HostReplaced);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(ManifestDocument))]
[JsonSerializable(typeof(MergeJournal))]
internal sealed partial class InstallationJsonContext : JsonSerializerContext;
