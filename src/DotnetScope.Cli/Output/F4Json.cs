using System.Text.Json.Serialization;

namespace DotnetScope.Cli.Output;

// Contratos estáveis de list --installed, uninstall e doctor com --output json; documentados em docs/cli.md.
internal sealed record InstalledJson(IReadOnlyList<InstalledItemJson> Installations);

internal sealed record InstalledItemJson(string Component, string Version, string Origin, string? ManagedBy, string Root, bool Present);

internal sealed record UninstallJson(
    string Status,
    string Component,
    string Version,
    string Root,
    string Origin,
    IReadOnlyList<string> RemovedPaths,
    IReadOnlyList<KeptPathJson> KeptPaths,
    string Environment);

internal sealed record KeptPathJson(string Path, string UsedBy);

internal sealed record DoctorJson(bool Healthy, IReadOnlyList<FindingJson> Findings);

internal sealed record FindingJson(string Code, string Severity, string Message, bool Repairable, bool Repaired);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(InstalledJson))]
[JsonSerializable(typeof(UninstallJson))]
[JsonSerializable(typeof(DoctorJson))]
internal sealed partial class F4JsonContext : JsonSerializerContext;
