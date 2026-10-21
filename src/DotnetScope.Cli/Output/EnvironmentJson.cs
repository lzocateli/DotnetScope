using System.Text.Json.Serialization;

namespace DotnetScope.Cli.Output;

// Contrato estável do bloco de ambiente em `install` e de `env apply|remove --output json`; ver docs/cli.md.
internal sealed record EnvironmentJson(
    string DotnetRoot,
    IReadOnlyList<string> PathEntries,
    string Status,
    string? Script,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> Backups,
    IReadOnlyList<ConflictJson> Conflicts,
    LoginShellJson? LoginShell);

internal sealed record ConflictJson(string File, int Line, string Value);

internal sealed record LoginShellJson(string Shell, string? Dotnet, string? DotnetRoot, bool Matches);

internal sealed record EnvCommandJson(string Action, EnvironmentJson Environment);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(EnvCommandJson))]
internal sealed partial class EnvJsonContext : JsonSerializerContext;
