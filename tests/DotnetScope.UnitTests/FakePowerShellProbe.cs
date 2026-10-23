using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.UnitTests;

/// <summary>Hosts do PowerShell simulados: políticas configuráveis e verificação predefinida, sem abrir processos.</summary>
internal sealed class FakePowerShellProbe(string documents) : IPowerShellHostProbe
{
    public List<PowerShellHost> Hosts { get; } = [];

    public LoginShellResult? Verification { get; set; }

    public int DetectCalls { get; private set; }

    public PowerShellHost? Verified { get; private set; }

    public IReadOnlyList<string> ProfileDirectories =>
        [Path.Combine(documents, "WindowsPowerShell"), Path.Combine(documents, "PowerShell")];

    public static FakePowerShellProbe Create(string documents, string? windowsPowerShellPolicy = "RemoteSigned", string? pwshPolicy = "RemoteSigned")
    {
        var probe = new FakePowerShellProbe(documents);
        probe.Hosts.Add(new PowerShellHost("powershell", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", probe.ProfileDirectories[0], windowsPowerShellPolicy));
        probe.Hosts.Add(new PowerShellHost("pwsh", @"C:\Program Files\PowerShell\7\pwsh.exe", probe.ProfileDirectories[1], pwshPolicy));
        return probe;
    }

    public IReadOnlyList<PowerShellHost> Detect()
    {
        DetectCalls++;
        return Hosts;
    }

    public Task<LoginShellResult?> VerifyAsync(PowerShellHost host, string root, CancellationToken cancellationToken)
    {
        Verified = host;
        return Task.FromResult(Verification);
    }
}
