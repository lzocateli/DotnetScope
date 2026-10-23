using System.Diagnostics;
using System.Text;
using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;

namespace DotnetScope.Infrastructure.UserEnvironment;

/// <summary>Host do PowerShell que carrega os perfis do usuário no Windows.</summary>
/// <param name="Name"><c>powershell</c> (Windows PowerShell 5.1) ou <c>pwsh</c> (PowerShell 7 ou mais novo).</param>
/// <param name="Executable">Caminho absoluto do executável.</param>
/// <param name="ProfileDirectory">Diretório dos perfis do usuário (Documentos\WindowsPowerShell ou Documentos\PowerShell).</param>
/// <param name="ExecutionPolicy">Política efetiva (<c>Get-ExecutionPolicy</c>), ou nula quando não foi possível obtê-la.</param>
public sealed record PowerShellHost(string Name, string Executable, string ProfileDirectory, string? ExecutionPolicy)
{
    /// <summary>Perfil de todos os hosts do usuário atual (<c>$PROFILE.CurrentUserAllHosts</c>).</summary>
    public string Profile => Path.Combine(ProfileDirectory, "profile.ps1");

    /// <summary>
    /// A política permite carregar o perfil local sem assinatura. <c>Restricted</c> e <c>AllSigned</c> o bloqueiam, e a
    /// política desconhecida é tratada como bloqueio (falha fechada).
    /// </summary>
    public bool RunsProfile =>
        ExecutionPolicy is { } policy
        && (policy.Equals("RemoteSigned", StringComparison.OrdinalIgnoreCase)
            || policy.Equals("Unrestricted", StringComparison.OrdinalIgnoreCase)
            || policy.Equals("Bypass", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Detecta os hosts do PowerShell e verifica o ambiente que um terminal novo receberia.</summary>
public interface IPowerShellHostProbe
{
    /// <summary>Diretórios de perfis do usuário de todos os hosts, instalados ou não (remoção e conflitos).</summary>
    IReadOnlyList<string> ProfileDirectories { get; }

    /// <summary>Hosts instalados e a política de execução efetiva de cada um.</summary>
    IReadOnlyList<PowerShellHost> Detect();

    /// <summary>Abre o host carregando os perfis, com o Path de um terminal novo, e informa para onde <c>dotnet</c> resolve.</summary>
    Task<LoginShellResult?> VerifyAsync(PowerShellHost host, string root, CancellationToken cancellationToken);
}

/// <summary>
/// Hosts reais: <c>%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe</c> e o <c>pwsh.exe</c> encontrado no Path
/// de um terminal novo (máquina seguido do usuário). Os comandos usam <c>-EncodedCommand</c> e <c>ArgumentList</c>, sem
/// interpolação, e a saída da verificação vem em Base64 depois de um marcador, tolerando perfis que escrevem no console.
/// </summary>
/// <param name="documents">
/// Pasta Documentos do usuário, ou nula quando ela não existe (contas de serviço): nesse caso não há perfis a remover e
/// somente <see cref="Detect"/> falha, para que o destino <c>registry</c> continue funcionando.
/// </param>
public sealed class PowerShellHostProbe(string? documents, IEnvironmentVariableStore store) : IPowerShellHostProbe
{
    private const string Marker = "__DOTNETSCOPE__";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    public IReadOnlyList<string> ProfileDirectories { get; } = documents is null
        ? []
        : [Path.Combine(documents, "WindowsPowerShell"), Path.Combine(documents, "PowerShell")];

    /// <summary>Documentos do usuário pela pasta conhecida (respeita redirecionamento e OneDrive), como o PowerShell.</summary>
    public static PowerShellHostProbe CreateDefault(IEnvironmentVariableStore store)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return new PowerShellHostProbe(Path.IsPathFullyQualified(documents) ? documents : null, store);
    }

    public IReadOnlyList<PowerShellHost> Detect()
    {
        if (ProfileDirectories.Count == 0)
        {
            throw new DotnetScopeException(
                ExitCodes.UnexpectedError,
                "Não foi possível determinar a pasta Documentos do usuário, onde ficam os perfis do PowerShell; use --windows-env-target registry.");
        }

        var hosts = new List<PowerShellHost>();
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var windowsPowerShell = Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (Path.IsPathFullyQualified(windowsPowerShell) && File.Exists(windowsPowerShell))
        {
            hosts.Add(new PowerShellHost("powershell", windowsPowerShell, ProfileDirectories[0], ReadPolicy(windowsPowerShell)));
        }

        if (FindPwsh() is { } pwsh)
        {
            hosts.Add(new PowerShellHost("pwsh", pwsh, ProfileDirectories[1], ReadPolicy(pwsh)));
        }

        return hosts;
    }

    public async Task<LoginShellResult?> VerifyAsync(PowerShellHost host, string root, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        const string script =
            "$d = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source\n"
            + "$u = [Text.Encoding]::UTF8\n"
            + "'" + Marker + "' + [Convert]::ToBase64String($u.GetBytes([string]$d)) + '|' + [Convert]::ToBase64String($u.GetBytes([string]$env:DOTNET_ROOT))\n";
        var startInfo = CreateStartInfo(host.Executable, script, loadProfile: true);
        startInfo.Environment.Remove("DOTNET_ROOT");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Limit);
        string text;
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            process.StandardInput.Close();
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                _ = await error.ConfigureAwait(false);
                text = await output.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return new LoginShellResult(host.Name, null, null, Matches: false);
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }

        var line = text.Split('\n').Select(l => l.TrimEnd('\r')).LastOrDefault(l => l.StartsWith(Marker, StringComparison.Ordinal));
        if (line is null)
        {
            return new LoginShellResult(host.Name, null, null, Matches: false);
        }

        var parts = line[Marker.Length..].Split('|', 2);
        var dotnet = Decode(parts[0]);
        var dotnetRoot = parts.Length > 1 ? Decode(parts[1]) : null;
        var matches = dotnet is not null
            && Path.GetDirectoryName(dotnet) is { } directory
            && string.Equals(
                ThirdPartyEnvironment.NormalizeWindows(directory),
                ThirdPartyEnvironment.NormalizeWindows(root),
                StringComparison.OrdinalIgnoreCase);
        return new LoginShellResult(host.Name, dotnet, dotnetRoot, matches);
    }

    /// <summary>Path de um terminal novo: o da máquina seguido do do usuário, expandidos, como o Windows monta.</summary>
    private string NewProcessPath() =>
        string.Join(';', new[] { store.GetMachine("Path"), store.GetUser("Path")?.Value }
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => store.Expand(p!)));

    /// <summary>
    /// Ambiente de um terminal novo para os hosts: Path da máquina seguido do do usuário e <c>PSModulePath</c> persistido
    /// (o do usuário substitui o da máquina, como o Windows monta). O <c>PSModulePath</c> herdado de um <c>pwsh</c> aponta
    /// para módulos do PowerShell 7, que o Windows PowerShell 5.1 não carrega (<c>Get-ExecutionPolicy</c> falharia).
    /// </summary>
    private void ApplyNewTerminalEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["PATH"] = NewProcessPath();
        var modulePath = store.GetUser("PSModulePath")?.Value ?? store.GetMachine("PSModulePath");
        if (string.IsNullOrEmpty(modulePath))
        {
            startInfo.Environment.Remove("PSModulePath");
        }
        else
        {
            startInfo.Environment["PSModulePath"] = store.Expand(modulePath);
        }
    }

    private string? FindPwsh()
    {
        foreach (var directory in WindowsPathList.Split(NewProcessPath()))
        {
            var candidate = directory.Trim().Trim('"');
            if (candidate.Length == 0 || !Path.IsPathFullyQualified(candidate))
            {
                continue;
            }

            var executable = Path.Combine(candidate, "pwsh.exe");
            if (File.Exists(executable))
            {
                return executable;
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var installed = programFiles.Length == 0 ? null : Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
        return installed is not null && File.Exists(installed) ? installed : null;
    }

    /// <summary>Política efetiva do host, sem carregar perfis; nula se o host falhar ou exceder o prazo.</summary>
    private string? ReadPolicy(string executable)
    {
        var startInfo = CreateStartInfo(executable, "Get-ExecutionPolicy", loadProfile: false);
        var output = new StringBuilder();
        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    lock (output)
                    {
                        output.AppendLine(e.Data);
                    }
                }
            };
            process.ErrorDataReceived += static (_, _) => { };
            process.Start();
            process.StandardInput.Close();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(Limit))
            {
                TryKill(process);
                return null;
            }

            // Sem perfis, nenhum processo filho mantém os pipes: esta espera só drena a leitura assíncrona.
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return null;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }

        string text;
        lock (output)
        {
            text = output.ToString();
        }

        var policy = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
        return policy is not null && policy.All(char.IsAsciiLetter) ? policy : null;
    }

    private ProcessStartInfo CreateStartInfo(string executable, string script, bool loadProfile)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        if (!loadProfile)
        {
            startInfo.ArgumentList.Add("-NoProfile");
        }

        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));

        // Uma política de escopo Process herdada deste processo não vale para um terminal novo.
        startInfo.Environment.Remove("PSExecutionPolicyPreference");
        ApplyNewTerminalEnvironment(startInfo);
        return startInfo;
    }

    private static string? Decode(string base64)
    {
        try
        {
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(base64.Trim()));
            return value.Length == 0 ? null : value;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
