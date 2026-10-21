using System.Text.Json.Serialization;

namespace DotnetScope.Cli.Output;

// Contrato estável de `install --output json`; documentado em docs/cli.md.
internal sealed record InstallJson(
    string Status,
    string Component,
    string Request,
    string Version,
    string Rid,
    string Scope,
    string InstallDir,
    ArtifactJson Artifact,
    IReadOnlyList<string> AddedPaths,
    IReadOnlyList<string> SharedPaths,
    bool HostReplaced,
    ValidationJson? Validation,
    IReadOnlyList<string> MissingDependencies,
    EnvironmentJson Environment);

internal sealed record ArtifactJson(string FileName, string Url, string Sha512);

internal sealed record ValidationJson(bool Succeeded, string Detail);

internal sealed record EnvironmentJson(string DotnetRoot, IReadOnlyList<string> PathEntries);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(InstallJson))]
internal sealed partial class InstallJsonContext : JsonSerializerContext;
