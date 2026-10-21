using System.Globalization;
using System.Text;
using System.Text.Json;
using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Infrastructure.Installation;

namespace DotnetScope.Infrastructure.UserEnvironment;

/// <summary>
/// Configura <c>DOTNET_ROOT</c> e <c>PATH</c> nos perfis do usuário (Linux e macOS): escreve o script
/// <c>env.sh</c> (e o do fish) e um bloco delimitado nos perfis, com backup, gravação atômica e remoção exata.
/// </summary>
public sealed class UnixEnvironmentConfigurator(EnvironmentLayout layout) : IUserEnvironmentConfigurator
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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
        AddIfDifferent(files, Layout.EnvScript, EnvironmentScripts.RenderPosix(target));
        if (Layout.FishAvailable)
        {
            AddIfDifferent(files, Layout.FishScript, EnvironmentScripts.RenderFish(target));
        }

        var block = EnvironmentScripts.ProfileBlock(EnvironmentScripts.ScriptExpression(Layout.EnvScript, Layout.Home));
        var profiles = new List<ProfileChange>();
        foreach (var (path, creatable) in Layout.Profiles)
        {
            var (writePath, isSymlink, exists) = Resolve(path);
            if (!exists && !creatable)
            {
                continue;
            }

            var content = exists ? ReadText(writePath) : string.Empty;
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
        var state = LoadState();
        var profiles = new List<ProfileChange>();
        foreach (var (path, _) in Layout.Profiles)
        {
            var (writePath, isSymlink, exists) = Resolve(path);
            if (!exists)
            {
                continue;
            }

            var content = ReadText(writePath);
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

    public async Task<EnvironmentApplyResult> ExecuteAsync(EnvironmentPlan plan, IDiagnosticSink diagnostics, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.HasChanges)
        {
            return new EnvironmentApplyResult([], []);
        }

        CreatePrivateDirectory(Path.GetDirectoryName(Layout.LockFile)!);
        using var _ = await InstallLock.AcquireAsync(Layout.LockFile, TimeSpan.FromSeconds(30), diagnostics, cancellationToken)
            .ConfigureAwait(false);

        // Sob o lock, confirma que nenhum perfil mudou desde o plano; caso contrário nada é gravado.
        foreach (var profile in plan.Profiles)
        {
            var current = File.Exists(profile.WritePath) ? ReadText(profile.WritePath) : null;
            if (!string.Equals(current, profile.OriginalContent, StringComparison.Ordinal))
            {
                throw new DotnetScopeException(
                    ExitCodes.Conflict,
                    $"{profile.Path} foi alterado durante a operação; nada foi gravado. Execute o comando novamente.");
            }
        }

        var changed = new List<string>();
        var backups = new List<string>();
        foreach (var file in plan.Files)
        {
            if (file.Content is null)
            {
                File.Delete(file.Path);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                WriteAtomic(file.Path, file.Content, existingMode: null);
            }

            changed.Add(file.Path);
        }

        var state = LoadState();
        if (plan.Target is not null)
        {
            state = state with { DotnetRoot = plan.Target.DotnetRoot };
        }

        foreach (var profile in plan.Profiles)
        {
            if (profile.IsSymlink)
            {
                diagnostics.Warning($"{profile.Path} é um link simbólico; o alvo {profile.WritePath} foi editado.");
            }

            if (profile.Exists)
            {
                backups.Add(Backup(profile.WritePath, Path.GetFileName(profile.Path)));
            }

            if (profile.Delete)
            {
                File.Delete(profile.WritePath);
            }
            else
            {
                var mode = profile.Exists && !OperatingSystem.IsWindows() ? File.GetUnixFileMode(profile.WritePath) : (UnixFileMode?)null;
                WriteAtomic(profile.WritePath, profile.NewContent, mode);
            }

            changed.Add(profile.Path);
            var previous = state.Profiles.FirstOrDefault(p => p.Path == profile.Path);
            state.Profiles.RemoveAll(p => p.Path == profile.Path);
            if (plan.Target is not null)
            {
                // Bloco novo registra criação e quebra de linha; bloco atualizado no lugar mantém o registro anterior.
                var entry = profile.BlockInserted
                    ? new ProfileState(profile.Path, Created: !profile.Exists, profile.AddedLeadingNewline)
                    : previous;
                if (entry is not null)
                {
                    state.Profiles.Add(entry);
                }
            }

            // Salvo a cada perfil para que uma falha posterior não perca o registro do que já foi alterado.
            SaveState(state);
        }

        SaveState(plan.Target is null ? new EnvironmentState(1, []) : state);
        return new EnvironmentApplyResult(changed, backups);
    }

    /// <summary>Raiz configurada pelo último <c>env apply</c>, ou nula se o ambiente não está configurado.</summary>
    public string? ConfiguredRoot()
    {
        if (!File.Exists(Layout.EnvScript))
        {
            return null;
        }

        if (LoadState().DotnetRoot is { } root)
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

    private static void AddIfDifferent(List<OwnedFileChange> files, string path, string content)
    {
        if (!File.Exists(path) || File.ReadAllText(path) != content)
        {
            files.Add(new OwnedFileChange(path, content));
        }
    }

    /// <summary>Resolve links simbólicos; um link quebrado é tratado como conflito.</summary>
    private static (string WritePath, bool IsSymlink, bool Exists) Resolve(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is null)
        {
            return (path, false, info.Exists);
        }

        var target = info.ResolveLinkTarget(returnFinalTarget: true);
        if (target is null || !target.Exists || target is DirectoryInfo)
        {
            throw new DotnetScopeException(ExitCodes.Conflict, $"{path} é um link simbólico quebrado ou não aponta para um arquivo.");
        }

        return (target.FullName, true, true);
    }

    /// <summary>Lê o perfil como UTF-8 estrito para garantir que a regravação preserve os bytes fora do bloco.</summary>
    private static string ReadText(string path)
    {
        try
        {
            return Utf8.GetString(File.ReadAllBytes(path));
        }
        catch (DecoderFallbackException exception)
        {
            throw new DotnetScopeException(ExitCodes.Conflict, $"{path} não está em UTF-8; ele não será editado.", exception);
        }
    }

    private static void WriteAtomic(string path, string content, UnixFileMode? existingMode)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.dotnetscope.{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = existingMode
                    ?? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
            }

            using (var stream = new FileStream(temporary, options))
            {
                stream.Write(Utf8.GetBytes(content));
                stream.Flush(flushToDisk: true);
            }

            if (existingMode is { } mode && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, mode);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private string Backup(string source, string name)
    {
        CreatePrivateDirectory(Layout.BackupDirectory);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var target = Path.Combine(Layout.BackupDirectory, $"{name}.{stamp}");
        for (var i = 1; File.Exists(target); i++)
        {
            target = Path.Combine(Layout.BackupDirectory, $"{name}.{stamp}.{i}");
        }

        File.Copy(source, target);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return target;
    }

    private EnvironmentState LoadState()
    {
        if (!File.Exists(Layout.StateFile))
        {
            return new EnvironmentState(1, []);
        }

        try
        {
            var state = JsonSerializer.Deserialize(File.ReadAllBytes(Layout.StateFile), EnvironmentJsonContext.Default.EnvironmentState);
            return state is { SchemaVersion: 1 } ? state with { Profiles = state.Profiles ?? [] } : new EnvironmentState(1, []);
        }
        catch (JsonException)
        {
            return new EnvironmentState(1, []);
        }
    }

    private void SaveState(EnvironmentState state)
    {
        CreatePrivateDirectory(Path.GetDirectoryName(Layout.StateFile)!);
        WriteAtomic(
            Layout.StateFile,
            JsonSerializer.Serialize(state, EnvironmentJsonContext.Default.EnvironmentState),
            OperatingSystem.IsWindows() ? null : UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void CreatePrivateDirectory(string path)
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
