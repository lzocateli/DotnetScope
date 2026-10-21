using System.Text.Json;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Versions;

namespace DotnetScope.Infrastructure.Installation;

/// <summary>
/// Move as folhas de uma árvore extraída para a raiz .NET sem substituir nada existente. Só o executável <c>dotnet</c>
/// é atualizado, e apenas quando o <c>host/fxr</c> do arquivo é mais novo que todos os da raiz. Cada movimento é
/// registrado em um journal; qualquer falha ou cancelamento desfaz os movimentos em ordem inversa.
/// </summary>
internal sealed class LayoutMerger
{
    private readonly string _source;
    private readonly string _root;
    private readonly string _backupDirectory;
    private readonly string _journalPath;
    private readonly string _hostName;
    private readonly List<string> _added = [];
    private readonly List<string> _createdContainers = [];
    private string? _hostBackup;

    private readonly string? _component;
    private readonly string? _version;

    public LayoutMerger(string source, string root, string stagingDirectory, bool windows, string? component = null, string? version = null)
    {
        _component = component;
        _version = version;
        _source = Path.GetFullPath(source);
        _root = Path.GetFullPath(root);
        _backupDirectory = Path.Combine(stagingDirectory, "backup");
        _journalPath = Path.Combine(stagingDirectory, "journal.json");
        _hostName = DotnetLayout.HostExecutable(windows);
    }

    /// <summary>Teste: invocado antes de cada movimento para simular falhas no meio da mesclagem.</summary>
    internal Action<string>? BeforeMove { get; set; }

    public MergeResult Merge(CancellationToken cancellationToken)
    {
        var leaves = EnumerateLeaves(_source, string.Empty).ToList();
        var replaceHost = ShouldReplaceHost();
        ValidateConflicts(leaves);

        var shared = new List<string>();
        try
        {
            foreach (var leaf in leaves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.Combine(_root, leaf);
                if (EntryExists(target))
                {
                    if (leaf == _hostName && replaceHost)
                    {
                        ReplaceHost(target);
                    }
                    else
                    {
                        shared.Add(leaf);
                    }

                    continue;
                }

                EnsureContainers(leaf);
                BeforeMove?.Invoke(leaf);
                var source = Path.Combine(_source, leaf);
                if (Directory.Exists(source))
                {
                    Directory.Move(source, target);
                }
                else
                {
                    File.Move(source, target, overwrite: false);
                }

                _added.Add(leaf);
                WriteJournal();
            }
        }
        catch
        {
            Rollback();
            throw;
        }

        return new MergeResult(leaves, _added.ToList(), shared, _hostBackup is not null);
    }

    /// <summary>Desfaz os movimentos já realizados; usado em falhas da mesclagem e do registro no manifesto.</summary>
    public void Rollback()
    {
        for (var i = _added.Count - 1; i >= 0; i--)
        {
            var target = Path.Combine(_root, _added[i]);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
            }
        }

        _added.Clear();
        if (_hostBackup is not null)
        {
            File.Move(_hostBackup, Path.Combine(_root, _hostName), overwrite: true);
            _hostBackup = null;
        }

        for (var i = _createdContainers.Count - 1; i >= 0; i--)
        {
            var container = _createdContainers[i];
            if (Directory.Exists(container) && !Directory.EnumerateFileSystemEntries(container).Any())
            {
                Directory.Delete(container);
            }
        }

        _createdContainers.Clear();
        WriteJournal();
    }

    internal static bool EntryExists(string path)
    {
        var info = new FileInfo(path);
        return info.Exists || Directory.Exists(path) || info.LinkTarget is not null;
    }

    private static IEnumerable<string> EnumerateLeaves(string directory, string relative)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(entry);
            var path = relative.Length == 0 ? name : $"{relative}/{name}";
            var segments = path.Split('/');
            if (Directory.Exists(entry) && segments.Length < DotnetLayout.LeafSegments(segments[0]))
            {
                foreach (var leaf in EnumerateLeaves(entry, path))
                {
                    yield return leaf;
                }
            }
            else
            {
                yield return path;
            }
        }
    }

    private void ValidateConflicts(IEnumerable<string> leaves)
    {
        foreach (var leaf in leaves)
        {
            var segments = leaf.Split('/');
            for (var i = 1; i < segments.Length; i++)
            {
                var container = Path.Combine(_root, string.Join('/', segments[..i]));
                if (EntryExists(container) && !Directory.Exists(container))
                {
                    throw new DotnetScopeException(
                        ExitCodes.Conflict,
                        $"Conflito na raiz: {container} existe, mas não é um diretório.");
                }
            }

            var target = Path.Combine(_root, leaf);
            if (EntryExists(target) && Directory.Exists(Path.Combine(_source, leaf)) != Directory.Exists(target))
            {
                throw new DotnetScopeException(
                    ExitCodes.Conflict,
                    $"Conflito na raiz: {target} existe com tipo diferente do arquivo de instalação.");
            }
        }
    }

    private void EnsureContainers(string leaf)
    {
        var segments = leaf.Split('/');
        for (var i = 1; i < segments.Length; i++)
        {
            var container = Path.Combine(_root, string.Join('/', segments[..i]));
            if (!Directory.Exists(container))
            {
                Directory.CreateDirectory(container);
                _createdContainers.Add(container);
            }
        }
    }

    private bool ShouldReplaceHost()
    {
        var incoming = MaxHostVersion(_source);
        if (incoming is null || !File.Exists(Path.Combine(_root, _hostName)))
        {
            return false;
        }

        var existing = MaxHostVersion(_root);
        return existing is not null && incoming > existing;
    }

    private static DotnetVersion? MaxHostVersion(string root)
    {
        var fxr = Path.Combine(root, "host", "fxr");
        return Directory.Exists(fxr)
            ? Directory.EnumerateDirectories(fxr)
                .Select(d => DotnetVersion.TryParse(Path.GetFileName(d), out var v) ? v : null)
                .OfType<DotnetVersion>()
                .DefaultIfEmpty()
                .Max()
            : null;
    }

    private void ReplaceHost(string target)
    {
        Directory.CreateDirectory(_backupDirectory);
        var backup = Path.Combine(_backupDirectory, _hostName);
        File.Move(target, backup, overwrite: false);
        _hostBackup = backup;
        WriteJournal();
        BeforeMove?.Invoke(_hostName);
        File.Move(Path.Combine(_source, _hostName), target, overwrite: false);
    }

    private void WriteJournal()
    {
        var journal = new MergeJournal("install", _root, _added.ToList(), _hostBackup, _component, _version);
        var temporary = _journalPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(journal, InstallationJsonContext.Default.MergeJournal));
        File.Move(temporary, _journalPath, overwrite: true);
    }
}

internal sealed record MergeResult(
    IReadOnlyList<string> Leaves,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Shared,
    bool HostReplaced);

/// <summary>
/// Journal mantido no staging para recuperação pelo <c>doctor</c>. Em instalações, <see cref="Added"/> são as folhas
/// já movidas para a raiz; em remoções, as folhas já movidas da raiz para a lixeira do staging.
/// </summary>
/// <remarks><see cref="Component"/> e <see cref="Version"/> identificam a operação para o doctor saber se ela foi concluída.</remarks>
internal sealed record MergeJournal(
    string Operation,
    string Root,
    IReadOnlyList<string> Added,
    string? HostBackup,
    string? Component = null,
    string? Version = null);
