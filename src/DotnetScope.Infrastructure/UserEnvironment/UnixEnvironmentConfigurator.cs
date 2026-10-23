using System.Text;
using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;

namespace DotnetScope.Infrastructure.UserEnvironment;

/// <summary>
/// Configura <c>DOTNET_ROOT</c> e <c>PATH</c> nos perfis do usuário (Linux e macOS): escreve o script
/// <c>env.sh</c> (e o do fish) e um bloco delimitado nos perfis, com backup, gravação atômica e remoção exata.
/// </summary>
public sealed class UnixEnvironmentConfigurator(EnvironmentLayout layout) : IUserEnvironmentConfigurator
{
    private readonly ProfileFileTransaction _transaction = new(layout.StateFile, layout.LockFile, layout.BackupDirectory, Path.GetFileName);

    public EnvironmentLayout Layout { get; } = layout;

    public string Home => Layout.Home;

    public string? SessionScript => Layout.EnvScript;

    /// <summary>
    /// Planeja a configuração sem gravar nada. Conflitos com a política <see cref="EnvConflictPolicy.Fail"/> lançam
    /// <see cref="ExitCodes.Conflict"/>; com <see cref="EnvConflictPolicy.Skip"/>, o plano não altera arquivos.
    /// </summary>
    public EnvironmentPlan PlanApply(EnvironmentTarget target, EnvConflictPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(target);
        var root = ThirdPartyEnvironment.Normalize(target.DotnetRoot);
        var conflicts = FindThirdParty()
            .Where(a => !string.Equals(a.ResolvedValue, root, StringComparison.Ordinal))
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
            return new EnvironmentPlan(target, [], [], conflicts, Skipped: true);
        }

        var files = new List<OwnedFileChange>();
        ProfileFileTransaction.AddIfDifferent(files, Layout.EnvScript, EnvironmentScripts.RenderPosix(target));
        if (Layout.FishAvailable)
        {
            ProfileFileTransaction.AddIfDifferent(files, Layout.FishScript, EnvironmentScripts.RenderFish(target));
        }

        var block = EnvironmentScripts.ProfileBlock(EnvironmentScripts.ScriptExpression(Layout.EnvScript, Layout.Home));
        var profiles = new List<ProfileChange>();
        foreach (var (path, creatable) in Layout.Profiles)
        {
            var (writePath, isSymlink, exists) = ProfileFileTransaction.Resolve(path);
            if (!exists && !creatable)
            {
                continue;
            }

            var content = exists ? ProfileFileTransaction.ReadText(writePath) : string.Empty;
            var hadBlock = ProfileBlockEditor.Find(content, path) is not null;
            var edit = ProfileBlockEditor.Upsert(content, block, path, moveToEnd: conflicts.Count > 0);
            if (edit.Changed)
            {
                profiles.Add(new ProfileChange(path, writePath, isSymlink, exists, edit.Content, !hadBlock, edit.AddedLeadingNewline, exists ? content : null));
            }
        }

        return new EnvironmentPlan(target, profiles, files, conflicts, Skipped: false);
    }

    /// <summary>Planeja a remoção do bloco de todos os perfis e dos scripts próprios.</summary>
    public EnvironmentPlan PlanRemove()
    {
        var state = _transaction.LoadState();
        var profiles = new List<ProfileChange>();
        foreach (var (path, _) in Layout.Profiles)
        {
            var (writePath, isSymlink, exists) = ProfileFileTransaction.Resolve(path);
            if (!exists)
            {
                continue;
            }

            var content = ProfileFileTransaction.ReadText(writePath);
            var recorded = state.Profiles.FirstOrDefault(p => p.Path == path);
            var edit = ProfileBlockEditor.Remove(content, path, recorded?.LeadingNewline ?? false);
            if (edit.Changed)
            {
                var delete = recorded?.Created == true && edit.Content.Length == 0 && !isSymlink;
                profiles.Add(new ProfileChange(path, writePath, isSymlink, true, edit.Content, false, false, content, delete));
            }
        }

        var files = new[] { Layout.EnvScript, Layout.FishScript }
            .Where(File.Exists)
            .Select(path => new OwnedFileChange(path, null))
            .ToList();
        return new EnvironmentPlan(null, profiles, files, [], Skipped: false);
    }

    public Task<EnvironmentApplyResult> ExecuteAsync(EnvironmentPlan plan, IDiagnosticSink diagnostics, CancellationToken cancellationToken) =>
        _transaction.ExecuteAsync(plan, diagnostics, cancellationToken);

    /// <summary>Raiz configurada pelo último <c>env apply</c>, ou nula se o ambiente não está configurado.</summary>
    public string? ConfiguredRoot()
    {
        if (!File.Exists(Layout.EnvScript))
        {
            return null;
        }

        if (_transaction.LoadState().DotnetRoot is { } root)
        {
            return root;
        }

        // Estados gravados antes do campo DotnetRoot: lê a atribuição gerada no env.sh.
        const string prefix = "export DOTNET_ROOT='";
        var line = File.ReadLines(Layout.EnvScript).FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal) && l.EndsWith('\''));
        return line?[prefix.Length..^1].Replace("'\\''", "'", StringComparison.Ordinal);
    }

    private IEnumerable<ThirdPartyAssignment> FindThirdParty()
    {
        foreach (var file in Layout.ThirdPartyFiles.Distinct(StringComparer.Ordinal))
        {
            // Arquivos de terceiros são só lidos: links quebrados ou ilegíveis são ignorados, não tratados como conflito.
            string content;
            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                content = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var assignment in ThirdPartyEnvironment.FindAssignments(file, content, Layout.Home))
            {
                yield return assignment;
            }
        }
    }
}
