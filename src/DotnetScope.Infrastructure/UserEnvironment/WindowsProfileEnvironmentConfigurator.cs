using System.Text;
using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;

namespace DotnetScope.Infrastructure.UserEnvironment;

/// <summary>
/// Configura <c>DOTNET_ROOT</c> e <c>PATH</c> no Windows sem gravar no registro (<c>--windows-env-target profile</c>):
/// escreve <c>%LOCALAPPDATA%\dotnetscope\env.ps1</c> e um bloco delimitado no perfil <c>profile.ps1</c> de cada host do
/// PowerShell cuja política de execução permite carregá-lo. Somente terminais PowerShell recebem o ambiente; o registro
/// é apenas lido, para detectar <c>DOTNET_ROOT</c> de terceiros e montar o Path da verificação.
/// </summary>
public sealed class WindowsProfileEnvironmentConfigurator(
    string stateDirectory,
    IPowerShellHostProbe probe,
    IEnvironmentVariableStore store,
    string home) : IUserEnvironmentConfigurator
{
    private const string DotnetRootName = "DOTNET_ROOT";

    private readonly ProfileFileTransaction _transaction = new(
        Path.Combine(stateDirectory, "env-profile-state.json"),
        Path.Combine(stateDirectory, "env.lock"),
        Path.Combine(stateDirectory, "backups"),
        BackupName);

    private IReadOnlyList<PowerShellHost>? _hosts;

    public string Home { get; } = home;

    /// <summary>Script do PowerShell carregado pelos perfis, também usado na sessão atual.</summary>
    public string EnvScript => Path.Combine(stateDirectory, "env.ps1");

    public string? SessionScript => EnvScript;

    public IEnvironmentVariableStore Store { get; } = store;

    public static WindowsProfileEnvironmentConfigurator CreateDefault(string home)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Os perfis do PowerShell só são configurados no Windows.");
        }

        var store = new RegistryEnvironmentStore(broadcast: false);
        return new WindowsProfileEnvironmentConfigurator(DotnetScopePaths.GetDataDirectory(), PowerShellHostProbe.CreateDefault(store), store, home);
    }

    /// <summary>Hosts do PowerShell detectados uma única vez por instância (o <c>install</c> planeja duas vezes).</summary>
    public IReadOnlyList<PowerShellHost> Hosts => _hosts ??= probe.Detect();

    /// <summary>
    /// Planeja sem gravar. Hosts cuja política bloqueia perfis ficam de fora com aviso; sem nenhum host utilizável, falha
    /// com <see cref="ExitCodes.Conflict"/> antes de qualquer gravação.
    /// </summary>
    public EnvironmentPlan PlanApply(EnvironmentTarget target, EnvConflictPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(target);
        var usable = Hosts.Where(h => h.RunsProfile).ToList();
        if (usable.Count == 0)
        {
            var policies = Hosts.Count == 0
                ? "nenhum PowerShell foi encontrado"
                : string.Join("; ", Hosts.Select(h => $"{h.Name}: política de execução {h.ExecutionPolicy ?? "desconhecida"}"));
            throw new DotnetScopeException(
                ExitCodes.Conflict,
                $"Nenhum PowerShell carregaria o perfil do usuário ({policies}). Nada foi alterado. Use --windows-env-target registry ou "
                + "--configure-env false, ou peça a liberação da política RemoteSigned para o usuário.");
        }

        var notices = Hosts
            .Where(h => !h.RunsProfile)
            .Select(h => $"a política de execução {h.ExecutionPolicy ?? "desconhecida"} do {h.Name} impede carregar perfis; {h.Profile} não será alterado.")
            .ToList();

        var root = ThirdPartyEnvironment.NormalizeWindows(target.DotnetRoot);
        var conflicts = FindThirdParty()
            .Where(a => !string.Equals(a.ResolvedValue, root, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (conflicts.Count > 0 && policy == EnvConflictPolicy.Fail)
        {
            var details = string.Join("; ", conflicts.Select(c => $"{c.Location} DOTNET_ROOT={c.RawValue}"));
            throw new DotnetScopeException(
                ExitCodes.Conflict,
                $"DOTNET_ROOT já é definido com outro valor ({details}). Nada foi alterado; use --env-conflict override ou skip.");
        }

        if (conflicts.Count > 0 && policy == EnvConflictPolicy.Skip)
        {
            return new EnvironmentPlan(target, [], [], conflicts, Skipped: true, Notices: notices);
        }

        var files = new List<OwnedFileChange>();

        // BOM: o Windows PowerShell 5.1 lê scripts sem BOM na página de código ANSI, o que corromperia caminhos acentuados.
        ProfileFileTransaction.AddIfDifferent(files, EnvScript, "\uFEFF" + EnvironmentScripts.RenderPowerShell(target));

        var block = EnvironmentScripts.PowerShellProfileBlock();
        var profiles = new List<ProfileChange>();
        foreach (var path in usable.Select(h => h.Profile).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var (writePath, isSymlink, exists) = ProfileFileTransaction.Resolve(path);
            var content = exists ? ProfileFileTransaction.ReadText(writePath) : string.Empty;
            var hadBlock = ProfileBlockEditor.Find(content, path) is not null;
            var edit = ProfileBlockEditor.Upsert(content, block, path, moveToEnd: conflicts.Count > 0);
            if (edit.Changed)
            {
                profiles.Add(new ProfileChange(path, writePath, isSymlink, exists, edit.Content, !hadBlock, edit.AddedLeadingNewline, exists ? content : null));
            }
        }

        return new EnvironmentPlan(target, profiles, files, conflicts, Skipped: false, Notices: notices);
    }

    /// <summary>Planeja a remoção do bloco dos perfis atuais e dos registrados no estado, e do <c>env.ps1</c>.</summary>
    public EnvironmentPlan PlanRemove()
    {
        var state = _transaction.LoadState();
        var candidates = probe.ProfileDirectories
            .Select(d => Path.Combine(d, "profile.ps1"))
            .Concat(state.Profiles.Select(p => p.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var profiles = new List<ProfileChange>();
        foreach (var path in candidates)
        {
            var (writePath, isSymlink, exists) = ProfileFileTransaction.Resolve(path);

            // Perfis sem o marcador nunca foram editados: não são decodificados, então UTF-16 ou ANSI não bloqueiam a remoção.
            if (!exists || !ContainsMarker(writePath))
            {
                continue;
            }

            var content = ProfileFileTransaction.ReadText(writePath);
            var recorded = state.Profiles.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
            var edit = ProfileBlockEditor.Remove(content, path, recorded?.LeadingNewline ?? false);
            if (edit.Changed)
            {
                var delete = recorded?.Created == true && edit.Content.Length == 0 && !isSymlink;
                profiles.Add(new ProfileChange(recorded?.Path ?? path, writePath, isSymlink, true, edit.Content, false, false, content, delete));
            }
        }

        var files = File.Exists(EnvScript) ? new List<OwnedFileChange> { new(EnvScript, null) } : [];
        return new EnvironmentPlan(null, profiles, files, [], Skipped: false);
    }

    public Task<EnvironmentApplyResult> ExecuteAsync(EnvironmentPlan plan, IDiagnosticSink diagnostics, CancellationToken cancellationToken) =>
        _transaction.ExecuteAsync(plan, diagnostics, cancellationToken);

    public string? ConfiguredRoot() => File.Exists(EnvScript) ? _transaction.LoadState().DotnetRoot : null;

    /// <summary>
    /// Abre um terminal novo do host configurado (o pwsh, se houver) carregando os perfis e informa para onde
    /// <c>dotnet</c> resolve; nulo quando nenhum perfil utilizável carrega o bloco.
    /// </summary>
    public Task<LoginShellResult?> VerifyAsync(string root, CancellationToken cancellationToken)
    {
        var host = Hosts
            .Where(h => h.RunsProfile && HasBlock(h.Profile))
            .OrderBy(h => h.Name == "pwsh" ? 0 : 1)
            .FirstOrDefault();
        return host is null ? Task.FromResult<LoginShellResult?>(null) : probe.VerifyAsync(host, root, cancellationToken);
    }

    /// <summary>Indica se o perfil existe e contém o bloco do DotnetScope.</summary>
    public static bool HasBlock(string profile)
    {
        var (writePath, _, exists) = ProfileFileTransaction.Resolve(profile);
        return exists && ContainsMarker(writePath) && ProfileBlockEditor.Find(ProfileFileTransaction.ReadText(writePath), profile) is not null;
    }

    /// <summary>Busca o marcador ASCII nos bytes, sem decodificar: o bloco só existe em perfis que o DotnetScope gravou em UTF-8.</summary>
    private static bool ContainsMarker(string path) =>
        File.ReadAllBytes(path).AsSpan().IndexOf(Encoding.ASCII.GetBytes(EnvironmentScripts.BeginMarker)) >= 0;

    /// <summary>
    /// <c>DOTNET_ROOT</c> de terceiros: a variável do usuário no registro (somente leitura) e atribuições
    /// <c>$env:DOTNET_ROOT</c> nos perfis do PowerShell fora do bloco.
    /// </summary>
    private IEnumerable<ThirdPartyAssignment> FindThirdParty()
    {
        if (Store.GetUser(DotnetRootName) is { } registry)
        {
            var expanded = Store.Expand(registry.Value);
            yield return new ThirdPartyAssignment(
                $@"{Store.Location}\{DotnetRootName}", 0, registry.Value, expanded.Length == 0 ? null : ThirdPartyEnvironment.NormalizeWindows(expanded));
        }

        foreach (var directory in probe.ProfileDirectories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*profile.ps1").Order(StringComparer.OrdinalIgnoreCase))
            {
                // Perfis de outros hosts são só lidos: ilegíveis são ignorados, não tratados como conflito.
                string content;
                try
                {
                    // Detecta BOM UTF-16 ou UTF-8 (comuns nos perfis do Windows PowerShell 5.1); sem BOM, lê como UTF-8.
                    content = File.ReadAllText(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var assignment in ThirdPartyEnvironment.FindPowerShellAssignments(file, content))
                {
                    yield return assignment;
                }
            }
        }
    }

    /// <summary>Backups distinguem os hosts: <c>WindowsPowerShell.profile.ps1</c> e <c>PowerShell.profile.ps1</c>.</summary>
    private static string BackupName(string profile) =>
        Path.GetFileName(Path.GetDirectoryName(profile)) is { Length: > 0 } host ? $"{host}.{Path.GetFileName(profile)}" : Path.GetFileName(profile);
}
