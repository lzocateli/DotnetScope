using System.Text.Json.Serialization;

namespace DotnetScope.Infrastructure.UserEnvironment;

// Estado persistido para remover o bloco restaurando o perfil byte a byte.
internal sealed record EnvironmentState(int SchemaVersion, List<ProfileState> Profiles, string? DotnetRoot = null);

internal sealed record ProfileState(string Path, bool Created, bool LeadingNewline);

/// <summary>
/// Estado do Windows: raiz configurada, entradas que o DotnetScope acrescentou ao Path do usuário e valores anteriores
/// das variáveis que ele definiu, para que a remoção restaure exatamente o que existia.
/// </summary>
internal sealed record WindowsEnvironmentState(
    int SchemaVersion,
    string? DotnetRoot,
    List<string> PathEntries,
    bool PathCreated,
    List<SavedVariable> Variables);

/// <param name="Value">Valor gravado pelo DotnetScope.</param>
/// <param name="PreviousValue">Valor anterior ao DotnetScope, ou nulo se a variável não existia.</param>
internal sealed record SavedVariable(string Name, string Value, string? PreviousValue, bool PreviousExpandable);

/// <summary>Valores anteriores gravados em backup antes de cada alteração no registro.</summary>
internal sealed record VariableBackup(string Location, List<VariableBackupEntry> Values);

internal sealed record VariableBackupEntry(string Name, string? Value, bool Expandable);

/// <summary>Estado do escopo global: raiz configurada e, por arquivo de sistema, o que foi gravado e o que existia antes.</summary>
internal sealed record SystemEnvironmentState(int SchemaVersion, string? DotnetRoot, List<SystemFileState> Files);

/// <param name="Written">Conteúdo gravado pelo DotnetScope.</param>
/// <param name="Previous">Conteúdo anterior ao DotnetScope, ou nulo se o arquivo não existia.</param>
internal sealed record SystemFileState(string Path, string Written, string? Previous);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(EnvironmentState))]
[JsonSerializable(typeof(SystemEnvironmentState))]
[JsonSerializable(typeof(WindowsEnvironmentState))]
[JsonSerializable(typeof(VariableBackup))]
internal sealed partial class EnvironmentJsonContext : JsonSerializerContext;
