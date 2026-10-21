using System.Text.Json.Serialization;

namespace DotnetScope.Infrastructure.UserEnvironment;

// Estado persistido para remover o bloco restaurando o perfil byte a byte.
internal sealed record EnvironmentState(int SchemaVersion, List<ProfileState> Profiles, string? DotnetRoot = null);

internal sealed record ProfileState(string Path, bool Created, bool LeadingNewline);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(EnvironmentState))]
internal sealed partial class EnvironmentJsonContext : JsonSerializerContext;
