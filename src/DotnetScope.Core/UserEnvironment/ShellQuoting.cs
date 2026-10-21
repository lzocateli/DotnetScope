namespace DotnetScope.Core.UserEnvironment;

/// <summary>Quoting que impede qualquer expansão: caminhos com espaço, aspas, <c>$</c> ou crase viram literais.</summary>
public static class ShellQuoting
{
    /// <summary>Aspas simples POSIX (sh, bash, zsh); aspas simples internas viram <c>'\''</c>.</summary>
    public static string Posix(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    /// <summary>Aspas simples do fish; dentro delas somente <c>\\</c> e <c>\'</c> são escapes.</summary>
    public static string Fish(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";
    }
}
