using System.Globalization;
using System.Text;
using System.Text.Json;
using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.Platform;
using DotnetScope.Infrastructure.SystemScope;

namespace DotnetScope.Infrastructure.UserEnvironment;

/// <summary>
/// Ambiente do escopo global no Linux: <c>/etc/profile.d/dotnetscope.sh</c> (todos os shells de login) e
/// <c>/etc/dotnet/install_location_&lt;arch&gt;</c> (host .NET). Os arquivos são do root com modo 0644, gravados de
/// forma atômica em diretórios confiáveis, com backup e estado em <c>/var/lib/dotnetscope</c>; a remoção restaura o
/// conteúdo anterior e não toca arquivos alterados depois por terceiros (modelo de ameaças, R05, R06 e R09).
/// </summary>
public sealed class SystemEnvironmentConfigurator(GlobalLayout layout, Func<string, UnixFileStatus?> stat)
    : IUserEnvironmentConfigurator
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    public GlobalLayout Layout { get; } = layout;

    /// <summary>Fixo: o escopo global nunca usa o home de quem elevou.</summary>
    public string Home => "/";

    public string? SessionScript => Layout.ProfileScript;

    private string StateFile => Path.Combine(Layout.StateDirectory, "env-state.json");

    private string LockFile => Path.Combine(Layout.StateDirectory, "env.lock");

    private string BackupDirectory => Path.Combine(Layout.StateDirectory, "backups");

    public EnvironmentPlan PlanApply(EnvironmentTarget target, EnvConflictPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(target);
        var root = ThirdPartyEnvironment.Normalize(target.DotnetRoot);
        var state = LoadState();
        EnsureNotLinks();

        var conflicts = FindConflicts(root, state);
        if (conflicts.Count > 0 && policy == EnvConflictPolicy.Fail)
        {
            var details = string.Join("; ", conflicts.Select(c => $"{c.Location} = {c.RawValue}"));
            throw new DotnetScopeException(
                ExitCodes.Conflict,
                $"O .NET global já é configurado com outro valor ({details}). Nada foi alterado; use --env-conflict override ou skip.");
        }

        if (conflicts.Count > 0 && policy == EnvConflictPolicy.Skip)
        {
            return new EnvironmentPlan(target, [], [], conflicts, Skipped: true);
        }

        var files = new List<OwnedFileChange>();
        AddIfDifferent(files, Layout.ProfileScript, EnvironmentScripts.RenderSystemProfile(root, target.TelemetryOptOut));
        AddIfDifferent(files, Layout.InstallLocationFile, root + "\n");
        return new EnvironmentPlan(target, [], files, conflicts, Skipped: false);
    }

    public EnvironmentPlan PlanRemove()
    {
        EnsureNotLinks();
        var files = new List<OwnedFileChange>();
        foreach (var file in LoadState().Files)
        {
            // Arquivo alterado por terceiros depois do DotnetScope não é tocado.
            var current = Read(file.Path);
            if (current == file.Written)
            {
                files.Add(new OwnedFileChange(file.Path, file.Previous, current, CheckOriginal: true));
            }
        }

        if (files.Count == 0 && File.Exists(StateFile))
        {
            files.Add(new OwnedFileChange(StateFile, null));
        }

        return new EnvironmentPlan(null, [], files, [], Skipped: false);
    }

    public async Task<EnvironmentApplyResult> ExecuteAsync(EnvironmentPlan plan, IDiagnosticSink diagnostics, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.HasChanges)
        {
            return new EnvironmentApplyResult([], []);
        }

        PrepareTrustedDirectory(Layout.StateDirectory, "o estado do dotnetscope");
        using var _ = await InstallLock.AcquireAsync(LockFile, TimeSpan.FromSeconds(30), diagnostics, cancellationToken).ConfigureAwait(false);

        // Sob o lock, confirma que nada mudou desde o plano e que nenhum alvo virou link simbólico.
        EnsureNotLinks();
        foreach (var file in plan.Files.Where(f => f.CheckOriginal))
        {
            if (Read(file.Path) != file.Original)
            {
                throw new DotnetScopeException(ExitCodes.Conflict, $"{file.Path} foi alterado durante a operação; nada foi gravado. Execute o comando novamente.");
            }
        }

        // Todos os diretórios de destino são validados antes de qualquer gravação, inclusive do estado.
        foreach (var change in plan.Files.Where(f => f.Path != StateFile))
        {
            PrepareTrustedDirectory(Path.GetDirectoryName(change.Path)!, change.Path);
        }

        var state = LoadState();
        if (plan.Target is not null)
        {
            // O estado é gravado antes dos arquivos: uma falha no meio nunca deixa arquivo gravado sem registro.
            var files = state.Files.ToList();
            foreach (var change in plan.Files)
            {
                var previous = files.FirstOrDefault(f => f.Path == change.Path && f.Written == change.Original);
                files.RemoveAll(f => f.Path == change.Path);
                files.Add(new SystemFileState(change.Path, change.Content!, previous is null ? change.Original : previous.Previous));
            }

            SaveState(new SystemEnvironmentState(1, plan.Target.DotnetRoot, files));
        }

        var changed = new List<string>();
        var backups = new List<string>();
        foreach (var change in plan.Files)
        {
            if (change.Path == StateFile)
            {
                continue;
            }

            if (File.Exists(change.Path))
            {
                backups.Add(Backup(change.Path));
            }

            if (change.Content is null)
            {
                File.Delete(change.Path);
            }
            else
            {
                WriteAtomic(change.Path, change.Content);
            }

            changed.Add(change.Path);
        }

        if (plan.Target is null && File.Exists(StateFile))
        {
            File.Delete(StateFile);
            changed.Add(StateFile);
        }

        return new EnvironmentApplyResult(changed, backups);
    }

    public string? ConfiguredRoot() => File.Exists(StateFile) ? LoadState().DotnetRoot : null;

    /// <summary>
    /// Carrega somente o script de <c>/etc/profile.d</c> com <c>/bin/sh</c> e ambiente fixo, sem perfis nem variáveis
    /// de quem elevou (modelo de ameaças, T06), e informa para onde <c>dotnet</c> resolve.
    /// </summary>
    public async Task<LoginShellResult?> VerifyAsync(string root, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!File.Exists("/bin/sh") || !File.Exists(Layout.ProfileScript))
        {
            return null;
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo("/bin/sh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = "/",
        };
        startInfo.Environment.Clear();
        startInfo.Environment["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";
        startInfo.Environment["HOME"] = "/";
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(". \"$1\" && command -v dotnet && printf '%s\\n' \"$DOTNET_ROOT\"");
        startInfo.ArgumentList.Add("dotnetscope-verify");
        startInfo.ArgumentList.Add(Layout.ProfileScript);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        _ = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        var lines = (await output.ConfigureAwait(false)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var dotnet = lines.Length > 0 ? lines[0] : null;
        var matches = dotnet is not null && Path.GetDirectoryName(dotnet) == ThirdPartyEnvironment.Normalize(root);
        return new LoginShellResult(Layout.ProfileScript, dotnet, lines.Length > 1 ? lines[1] : null, matches);
    }

    private List<ThirdPartyAssignment> FindConflicts(string root, SystemEnvironmentState state)
    {
        var conflicts = new List<ThirdPartyAssignment>();
        foreach (var file in Layout.SystemFiles.Where(f => f != Layout.ProfileScript).Distinct(StringComparer.Ordinal))
        {
            string content;
            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                content = Utf8.GetString(File.ReadAllBytes(file));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            // $HOME em arquivos do sistema muda por usuário: sem valor literal, conta como valor diferente.
            conflicts.AddRange(ThirdPartyEnvironment.FindAssignments(file, content, "/nonexistent")
                .Where(a => !string.Equals(a.ResolvedValue, root, StringComparison.Ordinal)));
        }

        var location = Read(Layout.InstallLocationFile);
        var ours = state.Files.Any(f => f.Path == Layout.InstallLocationFile && f.Written == location);
        if (location is not null && !ours)
        {
            var value = location.Split('\n')[0].Trim();
            if (ThirdPartyEnvironment.Normalize(value) != root)
            {
                conflicts.Add(new ThirdPartyAssignment(Layout.InstallLocationFile, 1, value, value));
            }
        }

        foreach (var link in Layout.SystemDotnetLinks.Where(File.Exists))
        {
            var target = new FileInfo(link).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? link;
            var directory = Path.GetDirectoryName(target);
            if (directory is not null && !string.Equals(ThirdPartyEnvironment.Normalize(directory), root, StringComparison.Ordinal))
            {
                conflicts.Add(new ThirdPartyAssignment(link, 0, target, directory));
            }
        }

        return conflicts;
    }

    private void EnsureNotLinks()
    {
        foreach (var path in new[] { Layout.ProfileScript, Layout.InstallLocationFile, StateFile })
        {
            if (new FileInfo(path).LinkTarget is not null)
            {
                throw new DotnetScopeException(ExitCodes.Conflict, $"{path} é um link simbólico; o escopo global não grava por links.");
            }
        }
    }

    private void PrepareTrustedDirectory(string directory, string purpose)
    {
        var parent = Path.GetDirectoryName(directory);
        if (parent is not null)
        {
            GlobalLocations.EnsureTrusted(parent, stat, purpose);
        }

        GlobalLocations.CreateDirectory(directory);
        GlobalLocations.EnsureTrusted(directory, stat, purpose);
    }

    private static void AddIfDifferent(List<OwnedFileChange> files, string path, string content)
    {
        var current = Read(path);
        if (current != content)
        {
            files.Add(new OwnedFileChange(path, content, current, CheckOriginal: true));
        }
    }

    private static string? Read(string path) => File.Exists(path) ? Utf8.GetString(File.ReadAllBytes(path)) : null;

    private static void WriteAtomic(string path, string content)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.dotnetscope.{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
            }

            using (var stream = new FileStream(temporary, options))
            {
                stream.Write(Utf8.GetBytes(content));
                stream.Flush(flushToDisk: true);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }

            // rename() substitui a entrada do diretório; um link no destino seria trocado, nunca seguido.
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

    private string Backup(string source)
    {
        PrepareTrustedDirectory(BackupDirectory, "os backups do dotnetscope");
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var target = Path.Combine(BackupDirectory, $"{Path.GetFileName(source)}.{stamp}");
        for (var i = 1; File.Exists(target); i++)
        {
            target = Path.Combine(BackupDirectory, $"{Path.GetFileName(source)}.{stamp}.{i}");
        }

        File.WriteAllBytes(target, File.ReadAllBytes(source));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return target;
    }

    private SystemEnvironmentState LoadState()
    {
        var empty = new SystemEnvironmentState(1, null, []);
        if (!File.Exists(StateFile))
        {
            return empty;
        }

        try
        {
            var state = JsonSerializer.Deserialize(File.ReadAllBytes(StateFile), EnvironmentJsonContext.Default.SystemEnvironmentState);
            return state is { SchemaVersion: 1 } ? state with { Files = state.Files ?? [] } : empty;
        }
        catch (JsonException)
        {
            return empty;
        }
    }

    private void SaveState(SystemEnvironmentState state)
    {
        WriteAtomic(StateFile, JsonSerializer.Serialize(state, EnvironmentJsonContext.Default.SystemEnvironmentState));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(StateFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
