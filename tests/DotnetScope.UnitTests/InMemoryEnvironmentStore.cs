using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.UnitTests;

/// <summary>Registro simulado: variáveis do usuário e da máquina, arquivos existentes e contagem de avisos.</summary>
internal sealed class InMemoryEnvironmentStore : IEnvironmentVariableStore
{
    public Dictionary<string, EnvironmentVariableValue> User { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Machine { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase) { ["USERPROFILE"] = @"C:\Users\teste" };

    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int Broadcasts { get; private set; }

    public bool BroadcastResult { get; set; } = true;

    /// <summary>Simula uma política que bloqueia a escrita em <c>HKCU\Environment</c>.</summary>
    public bool DenyWrites { get; set; }

    public string Location => @"HKCU\Environment";

    public EnvironmentVariableValue? GetUser(string name) => User.GetValueOrDefault(name);

    public void SetUser(string name, string value, bool expandable)
    {
        ThrowIfDenied();
        User[name] = new EnvironmentVariableValue(value, expandable);
    }

    public void DeleteUser(string name)
    {
        ThrowIfDenied();
        User.Remove(name);
    }

    public string? GetMachine(string name) => Machine.GetValueOrDefault(name);

    public string Expand(string value)
    {
        foreach (var (name, replacement) in Variables)
        {
            value = value.Replace($"%{name}%", replacement, StringComparison.OrdinalIgnoreCase);
        }

        return value;
    }

    public bool FileExists(string path) => Files.Contains(path);

    public bool Broadcast()
    {
        Broadcasts++;
        return BroadcastResult;
    }

    private void ThrowIfDenied()
    {
        if (DenyWrites)
        {
            throw new UnauthorizedAccessException("Acesso negado.");
        }
    }
}
