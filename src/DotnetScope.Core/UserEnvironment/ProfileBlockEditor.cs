namespace DotnetScope.Core.UserEnvironment;

/// <summary>Resultado de uma edição de perfil.</summary>
/// <param name="AddedLeadingNewline">Uma quebra de linha foi inserida antes do bloco porque o arquivo não terminava com ela.</param>
public sealed record ProfileEdit(string Content, bool Changed, bool AddedLeadingNewline);

/// <summary>
/// Insere, atualiza e remove o bloco delimitado por <see cref="EnvironmentScripts.BeginMarker"/> e
/// <see cref="EnvironmentScripts.EndMarker"/>, sem tocar em nenhum byte fora dele.
/// </summary>
public static class ProfileBlockEditor
{
    /// <summary>Localiza o bloco: (início, fim exclusivo incluindo a quebra de linha final) ou nulo.</summary>
    public static (int Start, int End)? Find(string content, string file)
    {
        ArgumentNullException.ThrowIfNull(content);
        var lines = SplitLines(content);
        var begins = lines.Where(l => l.Text.TrimEnd('\r') == EnvironmentScripts.BeginMarker).ToList();
        var ends = lines.Where(l => l.Text.TrimEnd('\r') == EnvironmentScripts.EndMarker).ToList();
        if (begins.Count == 0 && ends.Count == 0)
        {
            return null;
        }

        if (begins.Count != 1 || ends.Count != 1 || ends[0].Start < begins[0].Start)
        {
            throw new DotnetScopeException(
                ExitCodes.Conflict,
                $"Bloco do dotnetscope malformado em {file} (marcadores ausentes, duplicados ou fora de ordem); corrija o arquivo manualmente.");
        }

        return (begins[0].Start, ends[0].End);
    }

    /// <summary>
    /// Garante o bloco com as linhas informadas. Bloco existente com o mesmo conteúdo não altera o arquivo; com
    /// conteúdo diferente é substituído no lugar, ou movido para o fim quando <paramref name="moveToEnd"/>.
    /// </summary>
    public static ProfileEdit Upsert(string content, IReadOnlyList<string> blockLines, string file, bool moveToEnd = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(blockLines);
        // O bloco usa sempre LF: com CRLF, "fi\r" não fecha o if e sh/bash/zsh falham ao carregar o perfil.
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var block = string.Join('\n', blockLines) + "\n";
        var existing = Find(content, file);
        if (existing is { } range)
        {
            var current = content[range.Start..range.End];
            var atEnd = range.End == content.Length;
            if (current == block && (!moveToEnd || atEnd))
            {
                return new ProfileEdit(content, Changed: false, AddedLeadingNewline: false);
            }

            if (!moveToEnd || atEnd)
            {
                return new ProfileEdit(content[..range.Start] + block + content[range.End..], Changed: true, AddedLeadingNewline: false);
            }

            content = content[..range.Start] + content[range.End..];
        }

        var needsNewline = content.Length > 0 && !content.EndsWith('\n');
        return new ProfileEdit(content + (needsNewline ? newline : string.Empty) + block, Changed: true, needsNewline);
    }

    /// <summary>Remove o bloco e, se ele foi inserido junto, a quebra de linha que o precedia.</summary>
    public static ProfileEdit Remove(string content, string file, bool removeLeadingNewline)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (Find(content, file) is not { } range)
        {
            return new ProfileEdit(content, Changed: false, AddedLeadingNewline: false);
        }

        var start = range.Start;
        if (removeLeadingNewline && range.End == content.Length && start > 0 && content[start - 1] == '\n')
        {
            start -= start > 1 && content[start - 2] == '\r' ? 2 : 1;
        }

        return new ProfileEdit(content[..start] + content[range.End..], Changed: true, AddedLeadingNewline: false);
    }

    internal static List<(string Text, int Start, int End)> SplitLines(string content)
    {
        var lines = new List<(string, int, int)>();
        var start = 0;
        while (start < content.Length)
        {
            var newline = content.IndexOf('\n', start);
            var end = newline < 0 ? content.Length : newline + 1;
            lines.Add((content[start..(newline < 0 ? content.Length : newline)], start, end));
            start = end;
        }

        return lines;
    }
}
