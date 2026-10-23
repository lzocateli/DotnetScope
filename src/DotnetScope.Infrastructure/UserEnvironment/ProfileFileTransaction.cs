using System.Globalization;
using System.Text;
using System.Text.Json;
using DotnetScope.Core;
using DotnetScope.Infrastructure.Installation;

namespace DotnetScope.Infrastructure.UserEnvironment;

/// <summary>
/// Gravação dos perfis de shell e dos scripts próprios de um plano: sob lock, confere que nenhum perfil mudou desde o
/// plano, faz backup, grava atomicamente e registra o estado que permite a remoção byte a byte. Compartilhada pela
/// configuração por perfis do Linux e do macOS e pela dos perfis do PowerShell no Windows.
/// </summary>
/// <param name="backupName">Nome do backup de um perfil (sem a data), derivado do caminho do perfil.</param>
internal sealed class ProfileFileTransaction(string stateFile, string lockFile, string backupDirectory, Func<string, string> backupName)
{
    internal static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public string StateFile { get; } = stateFile;

    public async Task<EnvironmentApplyResult> ExecuteAsync(EnvironmentPlan plan, IDiagnosticSink diagnostics, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.HasChanges)
        {
            return new EnvironmentApplyResult([], []);
        }

        CreatePrivateDirectory(Path.GetDirectoryName(lockFile)!);
        using var _ = await InstallLock.AcquireAsync(lockFile, TimeSpan.FromSeconds(30), diagnostics, cancellationToken)
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
                backups.Add(Backup(profile.WritePath, backupName(profile.Path)));
            }

            if (profile.Delete)
            {
                File.Delete(profile.WritePath);
            }
            else
            {
                var mode = profile.Exists && !OperatingSystem.IsWindows() ? File.GetUnixFileMode(profile.WritePath) : (UnixFileMode?)null;
                Directory.CreateDirectory(Path.GetDirectoryName(profile.WritePath)!);
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

    public EnvironmentState LoadState()
    {
        if (!File.Exists(StateFile))
        {
            return new EnvironmentState(1, []);
        }

        try
        {
            var state = JsonSerializer.Deserialize(File.ReadAllBytes(StateFile), EnvironmentJsonContext.Default.EnvironmentState);
            return state is { SchemaVersion: 1 } ? state with { Profiles = state.Profiles ?? [] } : new EnvironmentState(1, []);
        }
        catch (JsonException)
        {
            return new EnvironmentState(1, []);
        }
    }

    /// <summary>Acrescenta o arquivo próprio ao plano quando seus bytes diferem do conteúdo (UTF-8, BOM incluído se houver).</summary>
    public static void AddIfDifferent(List<OwnedFileChange> files, string path, string content)
    {
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(Utf8.GetBytes(content)))
        {
            files.Add(new OwnedFileChange(path, content));
        }
    }

    /// <summary>Resolve links simbólicos; um link quebrado é tratado como conflito.</summary>
    public static (string WritePath, bool IsSymlink, bool Exists) Resolve(string path)
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
    public static string ReadText(string path)
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

    public static void CreatePrivateDirectory(string path)
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

    private void SaveState(EnvironmentState state)
    {
        CreatePrivateDirectory(Path.GetDirectoryName(StateFile)!);
        WriteAtomic(
            StateFile,
            JsonSerializer.Serialize(state, EnvironmentJsonContext.Default.EnvironmentState),
            OperatingSystem.IsWindows() ? null : UnixFileMode.UserRead | UnixFileMode.UserWrite);
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
        CreatePrivateDirectory(backupDirectory);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var target = Path.Combine(backupDirectory, $"{name}.{stamp}");
        for (var i = 1; File.Exists(target); i++)
        {
            target = Path.Combine(backupDirectory, $"{name}.{stamp}.{i}");
        }

        File.Copy(source, target);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return target;
    }
}
