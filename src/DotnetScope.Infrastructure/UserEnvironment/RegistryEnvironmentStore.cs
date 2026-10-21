using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DotnetScope.Core;
using Microsoft.Win32;

namespace DotnetScope.Infrastructure.UserEnvironment;

/// <summary>Valor bruto de uma variável persistente e se ele é expansível (<c>REG_EXPAND_SZ</c>).</summary>
public sealed record EnvironmentVariableValue(string Value, bool Expandable);

/// <summary>Variáveis de ambiente persistentes do usuário e da máquina; abstrai o registro do Windows.</summary>
public interface IEnvironmentVariableStore
{
    /// <summary>Localização exibida ao usuário, por exemplo <c>HKCU\Environment</c>.</summary>
    string Location { get; }

    EnvironmentVariableValue? GetUser(string name);

    void SetUser(string name, string value, bool expandable);

    void DeleteUser(string name);

    /// <summary>Valor bruto da variável da máquina (somente leitura).</summary>
    string? GetMachine(string name);

    /// <summary>Expande referências <c>%VARIAVEL%</c>.</summary>
    string Expand(string value);

    bool FileExists(string path);

    /// <summary>Avisa as janelas abertas (Explorer) da mudança; retorna falso se o aviso falhou.</summary>
    bool Broadcast();
}

/// <summary>
/// <c>HKCU\Environment</c> (ou uma subchave de teste em <c>HKCU</c>) e <c>HKLM\...\Session Manager\Environment</c>.
/// Valores são lidos sem expansão e gravados preservando o tipo; nunca usa <c>setx</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class RegistryEnvironmentStore(string userSubKey = "Environment", bool broadcast = true) : IEnvironmentVariableStore
{
    private const string MachineSubKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private const nint HwndBroadcast = 0xFFFF;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    public string Location => @"HKCU\" + userSubKey;

    public EnvironmentVariableValue? GetUser(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(userSubKey, writable: false);
        return Read(key, name, Location);
    }

    public void SetUser(string name, string value, bool expandable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(userSubKey, writable: true);
        key.SetValue(name, value, expandable ? RegistryValueKind.ExpandString : RegistryValueKind.String);
    }

    public void DeleteUser(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(userSubKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public string? GetMachine(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(MachineSubKey, writable: false);
        return Read(key, name, @"HKLM\" + MachineSubKey)?.Value;
    }

    public string Expand(string value) => Environment.ExpandEnvironmentVariables(value);

    public bool FileExists(string path) => File.Exists(path);

    public bool Broadcast() =>
        !broadcast || SendMessageTimeout(HwndBroadcast, WmSettingChange, 0, "Environment", SmtoAbortIfHung, 5000, out _) != 0;

    private static EnvironmentVariableValue? Read(RegistryKey? key, string name, string location)
    {
        if (key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } value)
        {
            return null;
        }

        return key.GetValueKind(name) switch
        {
            RegistryValueKind.String => new EnvironmentVariableValue((string)value, Expandable: false),
            RegistryValueKind.ExpandString => new EnvironmentVariableValue((string)value, Expandable: true),
            var kind => throw new DotnetScopeException(
                ExitCodes.Conflict,
                $@"{location}\{name} tem tipo {kind} (esperado REG_SZ ou REG_EXPAND_SZ); ele não será alterado."),
        };
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeout(nint window, uint message, nuint wParam, string lParam, uint flags, uint timeout, out nuint result);
}
