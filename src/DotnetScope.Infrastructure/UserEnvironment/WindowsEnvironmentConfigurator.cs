using System.Globalization;
using System.Text.Json;
using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Infrastructure.Installation;

namespace DotnetScope.Infrastructure.UserEnvironment;

/// <summary>
/// Configura <c>DOTNET_ROOT</c>, <c>Path</c> e, opcionalmente, <c>DOTNET_CLI_TELEMETRY_OPTOUT</c> nas variáveis
/// persistentes do usuário (<c>HKCU\Environment</c>) e avisa o Explorer com <c>WM_SETTINGCHANGE</c>. O
/// <c>Path</c> recebe a raiz e as ferramentas no início, preservando o restante do valor e o tipo
/// (<c>REG_EXPAND_SZ</c>); a remoção retira somente o que o DotnetScope acrescentou e restaura valores anteriores.
/// </summary>
public sealed class WindowsEnvironmentConfigurator(IEnvironmentVariableStore store, string stateDirectory, string home)
    : IUserEnvironmentConfigurator
{
    internal const string DotnetRootName = "DOTNET_ROOT";
    internal const string PathName = "Path";
    internal const string TelemetryName = "DOTNET_CLI_TELEMETRY_OPTOUT";

    public string Home { get; } = home;

    public string? SessionScript => null;

    public IEnvironmentVariableStore Store { get; } = store;

    internal string StateFile => Path.Combine(stateDirectory, "env-state.json");

    private string LockFile => Path.Combine(stateDirectory, "env.lock");

    private string BackupDirectory => Path.Combine(stateDirectory, "backups");

    public static WindowsEnvironmentConfigurator CreateDefault(string home) =>
        OperatingSystem.IsWindows()
            ? new WindowsEnvironmentConfigurator(new RegistryEnvironmentStore(), DotnetScopePaths.GetDataDirectory(), home)
            : throw new PlatformNotSupportedException("O registro do Windows só está disponível no Windows.");

    public EnvironmentPlan PlanApply(EnvironmentTarget target, EnvConflictPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(target);
        var state = LoadState();
        var current = Store.GetUser(DotnetRootName);
        var ours = state.Variables.FirstOrDefault(v => v.Name == DotnetRootName) is { } saved
            && current is not null
            && current.Value == saved.Value;

        var conflicts = new List<ThirdPartyAssignment>();
        if (current is not null && !SamePath(current.Value, target.DotnetRoot) && !ours)
        {
            conflicts.Add(new ThirdPartyAssignment(Display(DotnetRootName), 0, current.Value, Store.Expand(current.Value)));
        }

        if (conflicts.Count > 0 && policy == EnvConflictPolicy.Fail)
        {
            throw new DotnetScopeException(
                ExitCodes.Conflict,
                $"DOTNET_ROOT já é definido com outro valor ({Display(DotnetRootName)} = {current!.Value}). Nada foi alterado; use --env-conflict override ou skip.");
        }

        if (conflicts.Count > 0 && policy == EnvConflictPolicy.Skip)
        {
            return new EnvironmentPlan(target, [], [], conflicts, Skipped: true);
        }

        var changes = new List<VariableChange>();
        if (current is null || !SamePath(current.Value, target.DotnetRoot))
        {
            changes.Add(Change(DotnetRootName, current, target.DotnetRoot, expandable: false));
        }

        var path = Store.GetUser(PathName);
        var expandablePath = path?.Expandable ?? true;
        var (newPath, added) = WindowsPathList.Apply(path?.Value, PathEntries(target, expandablePath), state.PathEntries, Store.Expand);
        if (path is null ? added.Count > 0 : newPath != path.Value)
        {
            changes.Add(Change(PathName, path, newPath, expandablePath));
        }

        var telemetry = Store.GetUser(TelemetryName);
        var savedTelemetry = state.Variables.FirstOrDefault(v => v.Name == TelemetryName);
        if (target.TelemetryOptOut)
        {
            if (telemetry?.Value != "1")
            {
                changes.Add(Change(TelemetryName, telemetry, "1", expandable: false));
            }
        }
        else if (savedTelemetry is not null && telemetry?.Value == savedTelemetry.Value)
        {
            // Sem --telemetry-optout, o valor definido antes pelo DotnetScope volta ao anterior (como no env.sh).
            changes.Add(Change(TelemetryName, telemetry, savedTelemetry.PreviousValue, savedTelemetry.PreviousExpandable));
        }

        return new EnvironmentPlan(target, [], [], conflicts, Skipped: false, changes);
    }

    public EnvironmentPlan PlanRemove()
    {
        var state = LoadState();
        var changes = new List<VariableChange>();
        var path = Store.GetUser(PathName);
        if (path is not null && state.PathEntries.Count > 0)
        {
            var remaining = WindowsPathList.Remove(path.Value, state.PathEntries, Store.Expand);
            if (remaining != path.Value)
            {
                changes.Add(Change(PathName, path, remaining.Length == 0 && state.PathCreated ? null : remaining, path.Expandable));
            }
        }

        foreach (var saved in state.Variables)
        {
            // Valores alterados pelo usuário depois do DotnetScope não são tocados.
            var current = Store.GetUser(saved.Name);
            if (current is not null && current.Value == saved.Value)
            {
                changes.Add(Change(saved.Name, current, saved.PreviousValue, saved.PreviousExpandable));
            }
        }

        var files = File.Exists(StateFile) ? new List<OwnedFileChange> { new(StateFile, null) } : [];
        return new EnvironmentPlan(null, [], files, [], Skipped: false, changes);
    }

    public async Task<EnvironmentApplyResult> ExecuteAsync(EnvironmentPlan plan, IDiagnosticSink diagnostics, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (!plan.HasChanges)
        {
            return new EnvironmentApplyResult([], []);
        }

        CreateDirectory(stateDirectory);
        using var _ = await InstallLock.AcquireAsync(LockFile, TimeSpan.FromSeconds(30), diagnostics, cancellationToken).ConfigureAwait(false);

        // Sob o lock, confirma que nenhuma variável mudou desde o plano; caso contrário nada é gravado.
        foreach (var change in plan.ChangedVariables)
        {
            var current = Store.GetUser(change.Name);
            if (current?.Value != change.OldValue || (current is not null && current.Expandable != change.OldExpandable))
            {
                throw new DotnetScopeException(
                    ExitCodes.Conflict,
                    $"{change.Display} foi alterado durante a operação; nada foi gravado. Execute o comando novamente.");
            }
        }

        var backups = new List<string>();
        if (plan.ChangedVariables.Count > 0)
        {
            backups.Add(Backup(plan.ChangedVariables));
        }

        var state = LoadState();
        if (plan.Target is not null)
        {
            // O estado é gravado antes do registro: uma falha no meio nunca deixa entradas acrescentadas sem dono.
            SaveState(Record(state, plan));
        }

        var changed = new List<string>();
        foreach (var change in plan.ChangedVariables)
        {
            if (change.NewValue is null)
            {
                Store.DeleteUser(change.Name);
            }
            else
            {
                Store.SetUser(change.Name, change.NewValue, change.NewExpandable);
            }

            changed.Add(change.Display);
        }

        if (plan.Target is null && File.Exists(StateFile))
        {
            File.Delete(StateFile);
            changed.Add(StateFile);
        }

        if (plan.ChangedVariables.Count > 0 && !Store.Broadcast())
        {
            diagnostics.Warning("o aviso WM_SETTINGCHANGE falhou; terminais abertos pelo Explorer podem exigir novo logon para ver a mudança.");
        }

        return new EnvironmentApplyResult(changed, backups);
    }

    public string? ConfiguredRoot() => File.Exists(StateFile) ? LoadState().DotnetRoot : null;

    /// <summary>
    /// Calcula o que um terminal novo receberia: <c>Path</c> da máquina seguido do <c>Path</c> do usuário, expandidos.
    /// Indica se <c>dotnet.exe</c> resolve para <paramref name="root"/> e se um <c>dotnet</c> da máquina tem precedência.
    /// </summary>
    public (LoginShellResult Result, bool ShadowedByMachine) VerifyNewProcess(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var machine = WindowsPathList.Split(Store.Expand(Store.GetMachine(PathName) ?? string.Empty));
        var user = WindowsPathList.Split(Store.Expand(Store.GetUser(PathName)?.Value ?? string.Empty));
        var resolved = WindowsPathList.FindFirst(machine.Concat(user), "dotnet.exe", Store.FileExists);
        var directory = resolved?[..resolved.LastIndexOf('\\')];
        var matches = directory is not null && SamePath(directory, root);
        var shadowed = !matches && directory is not null && machine.Any(m => m.Trim().Length > 0 && SamePath(m, directory));
        var dotnetRoot = Store.GetUser(DotnetRootName)?.Value ?? Store.GetMachine(DotnetRootName);
        return (new LoginShellResult("novo terminal (registro do Windows)", resolved, dotnetRoot is null ? null : Store.Expand(dotnetRoot), matches), shadowed);
    }

    private WindowsEnvironmentState Record(WindowsEnvironmentState state, EnvironmentPlan plan)
    {
        var target = plan.Target!;
        var variables = state.Variables.ToList();
        var pathEntries = state.PathEntries;
        var pathCreated = state.PathCreated;
        foreach (var change in plan.ChangedVariables)
        {
            if (change.Name == PathName)
            {
                var entries = PathEntries(target, change.NewExpandable);
                var (_, added) = WindowsPathList.Apply(change.OldValue, entries, state.PathEntries, Store.Expand);
                var keep = entries.Select(k => WindowsPathList.Normalize(k, Store.Expand)).ToHashSet(StringComparer.Ordinal);
                pathEntries = state.PathEntries.Where(e => keep.Contains(WindowsPathList.Normalize(e, Store.Expand))).Concat(added).ToList();
                pathCreated = pathCreated || (change.OldValue is null && state.PathEntries.Count == 0);
                continue;
            }

            // O valor anterior registrado só continua válido se o atual ainda é o que o DotnetScope gravou; se outra
            // ferramenta ou o usuário o trocou, esse novo valor é o que a remoção deve restaurar.
            var previous = variables.FirstOrDefault(v => v.Name == change.Name && v.Value == change.OldValue);
            variables.RemoveAll(v => v.Name == change.Name);
            var originalValue = previous is null ? change.OldValue : previous.PreviousValue;
            var originalExpandable = previous is null ? change.OldExpandable : previous.PreviousExpandable;
            if (change.NewValue is not null && change.NewValue != originalValue)
            {
                variables.Add(new SavedVariable(change.Name, change.NewValue, originalValue, originalExpandable));
            }
        }

        return new WindowsEnvironmentState(1, target.DotnetRoot, pathEntries, pathCreated, variables);
    }

    /// <summary>
    /// Entradas do Path: a raiz e as ferramentas. Com <c>REG_EXPAND_SZ</c>, as ferramentas padrão usam a mesma forma
    /// que o SDK grava na primeira execução (<c>%USERPROFILE%\.dotnet\tools</c>), para que ele não as duplique.
    /// </summary>
    private IReadOnlyList<string> PathEntries(EnvironmentTarget target, bool expandable)
    {
        const string sdkTools = @"%USERPROFILE%\.dotnet\tools";
        var tools = expandable && SamePath(sdkTools, target.ToolsDirectory) ? sdkTools : target.ToolsDirectory;
        return [target.DotnetRoot, tools];
    }

    private VariableChange Change(string name, EnvironmentVariableValue? current, string? value, bool expandable) =>
        new(name, Display(name), current?.Value, current?.Expandable ?? false, value, expandable);

    private string Display(string name) => $@"{Store.Location}\{name}";

    private bool SamePath(string left, string right) =>
        WindowsPathList.Normalize(left, Store.Expand) == WindowsPathList.Normalize(right, Store.Expand);

    private string Backup(IReadOnlyList<VariableChange> changes)
    {
        CreateDirectory(BackupDirectory);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var target = Path.Combine(BackupDirectory, $"environment.{stamp}.json");
        for (var i = 1; File.Exists(target); i++)
        {
            target = Path.Combine(BackupDirectory, $"environment.{stamp}.{i}.json");
        }

        var backup = new VariableBackup(Store.Location, changes.Select(c => new VariableBackupEntry(c.Name, c.OldValue, c.OldExpandable)).ToList());
        File.WriteAllText(target, JsonSerializer.Serialize(backup, EnvironmentJsonContext.Default.VariableBackup));
        return target;
    }

    private WindowsEnvironmentState LoadState()
    {
        var empty = new WindowsEnvironmentState(1, null, [], false, []);
        if (!File.Exists(StateFile))
        {
            return empty;
        }

        try
        {
            var state = JsonSerializer.Deserialize(File.ReadAllBytes(StateFile), EnvironmentJsonContext.Default.WindowsEnvironmentState);
            return state is { SchemaVersion: 1 }
                ? state with { PathEntries = state.PathEntries ?? [], Variables = state.Variables ?? [] }
                : empty;
        }
        catch (JsonException)
        {
            return empty;
        }
    }

    private void SaveState(WindowsEnvironmentState state)
    {
        CreateDirectory(stateDirectory);
        var temporary = StateFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, EnvironmentJsonContext.Default.WindowsEnvironmentState));
        File.Move(temporary, StateFile, overwrite: true);
    }

    private static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
