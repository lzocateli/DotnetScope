using System.Text.RegularExpressions;

namespace DotnetScope.Core.UserEnvironment;

/// <summary>
/// Encontra atribuições de <c>DOTNET_ROOT</c> fora do bloco do DotnetScope (por exemplo, blocos do Ansible ou de
/// pacotes da distribuição). Arquivos de terceiros são apenas lidos.
/// </summary>
public static partial class ThirdPartyEnvironment
{
    public static IReadOnlyList<ThirdPartyAssignment> FindAssignments(string file, string content, string home)
    {
        ArgumentNullException.ThrowIfNull(content);
        var ownBlock = ProfileBlockEditor.Find(content, file);
        var result = new List<ThirdPartyAssignment>();
        var number = 0;
        foreach (var (text, start, _) in ProfileBlockEditor.SplitLines(content))
        {
            number++;
            if (ownBlock is { } block && start >= block.Start && start < block.End)
            {
                continue;
            }

            var match = PosixAssignment().Match(text);
            if (!match.Success)
            {
                match = FishAssignment().Match(text);
            }

            if (match.Success)
            {
                var raw = match.Groups["value"].Value.Trim();
                result.Add(new ThirdPartyAssignment(file, number, raw, Resolve(raw, home)));
            }
        }

        return result;
    }

    /// <summary>Normaliza um caminho para comparação (sem barra final).</summary>
    public static string Normalize(string path) => path.Length > 1 ? path.TrimEnd('/') : path;

    /// <summary>Avalia o valor quando ele é literal ou usa somente <c>$HOME</c>, <c>${HOME}</c> ou <c>~</c>.</summary>
    internal static string? Resolve(string raw, string home)
    {
        var value = raw;
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        if (comment >= 0)
        {
            value = value[..comment].TrimEnd();
        }

        value = value.TrimEnd(';');
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            var literal = value[1..^1];
            return literal.Contains('\'', StringComparison.Ordinal) ? null : Normalize(literal);
        }

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        value = value.Replace("${HOME}", home, StringComparison.Ordinal).Replace("$HOME", home, StringComparison.Ordinal);
        if (value.StartsWith('~'))
        {
            value = home + value[1..];
        }

        return value.Length == 0 || value.IndexOfAny(['$', '`', '"', '\'', '\\', ' ', ';', '(', ')']) >= 0
            ? null
            : Normalize(value);
    }

    [GeneratedRegex(@"^\s*(?:export\s+)?DOTNET_ROOT\s*=\s*(?<value>\S.*)$")]
    private static partial Regex PosixAssignment();

    [GeneratedRegex(@"^\s*set\s+(?:-[a-zA-Z]+\s+)*DOTNET_ROOT\s+(?<value>\S.*)$")]
    private static partial Regex FishAssignment();
}
