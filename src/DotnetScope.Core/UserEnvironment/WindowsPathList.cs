namespace DotnetScope.Core.UserEnvironment;

/// <summary>
/// Edição da variável <c>Path</c> do Windows preservando o valor original: entradas são separadas por <c>;</c>,
/// vazias são mantidas e a comparação ignora maiúsculas, barra final, aspas e variáveis (<c>%USERPROFILE%</c>).
/// </summary>
public static class WindowsPathList
{
    /// <summary>Divide o valor sem descartar entradas vazias, para que a junção reproduza o original.</summary>
    public static IReadOnlyList<string> Split(string? value) =>
        string.IsNullOrEmpty(value) ? [] : value.Split(';');

    /// <summary>Forma canônica para comparação: expandida, sem aspas e sem barra final, em minúsculas invariantes.</summary>
    public static string Normalize(string entry, Func<string, string> expand)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(expand);
        var value = expand(entry.Trim()).Trim().Trim('"').Replace('/', '\\');
        while (value.Length > 3 && value.EndsWith('\\'))
        {
            value = value[..^1];
        }

        return value.ToUpperInvariant();
    }

    public static bool Contains(string? value, string directory, Func<string, string> expand)
    {
        var wanted = Normalize(directory, expand);
        return Split(value).Any(entry => entry.Trim().Length > 0 && Normalize(entry, expand) == wanted);
    }

    /// <summary>Primeiro diretório da lista (já expandida) que contém <paramref name="fileName"/>.</summary>
    public static string? FindFirst(IEnumerable<string> directories, string fileName, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(directories);
        ArgumentNullException.ThrowIfNull(fileExists);
        foreach (var directory in directories)
        {
            var clean = directory.Trim().Trim('"');
            if (clean.Length == 0)
            {
                continue;
            }

            var candidate = clean.TrimEnd('\\') + "\\" + fileName;
            if (fileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Remove as entradas <paramref name="owned"/> que não estão em <paramref name="keep"/> e acrescenta no início as
    /// entradas de <paramref name="keep"/> ausentes. Retorna o novo valor e as entradas efetivamente acrescentadas.
    /// </summary>
    public static (string Value, IReadOnlyList<string> Added) Apply(
        string? value,
        IReadOnlyList<string> keep,
        IReadOnlyCollection<string> owned,
        Func<string, string> expand)
    {
        ArgumentNullException.ThrowIfNull(keep);
        ArgumentNullException.ThrowIfNull(owned);
        var keepNormalized = keep.Select(k => Normalize(k, expand)).ToHashSet(StringComparer.Ordinal);
        var stale = owned.Select(o => Normalize(o, expand)).Where(o => !keepNormalized.Contains(o)).ToHashSet(StringComparer.Ordinal);
        var entries = Split(value).Where(entry => entry.Trim().Length == 0 || !stale.Contains(Normalize(entry, expand))).ToList();

        var added = new List<string>();
        foreach (var directory in keep)
        {
            var normalized = Normalize(directory, expand);
            if (!entries.Any(entry => entry.Trim().Length > 0 && Normalize(entry, expand) == normalized)
                && !added.Any(a => Normalize(a, expand) == normalized))
            {
                added.Add(directory);
            }
        }

        if (added.Count == 0 && entries.Count == Split(value).Count)
        {
            return (value ?? string.Empty, []);
        }

        var result = added.Concat(entries).ToList();
        return (string.Join(';', result), added);
    }

    /// <summary>Remove somente as entradas <paramref name="owned"/>; o restante do valor fica intacto.</summary>
    public static string Remove(string? value, IReadOnlyCollection<string> owned, Func<string, string> expand)
    {
        ArgumentNullException.ThrowIfNull(owned);
        var remove = owned.Select(o => Normalize(o, expand)).ToHashSet(StringComparer.Ordinal);
        var entries = Split(value).Where(entry => entry.Trim().Length == 0 || !remove.Contains(Normalize(entry, expand))).ToList();
        return string.Join(';', entries);
    }
}
