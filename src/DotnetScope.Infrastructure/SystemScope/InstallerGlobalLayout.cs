namespace DotnetScope.Infrastructure.SystemScope;

/// <summary>
/// Escopo global pelo instalador oficial (Windows <c>.exe</c> e macOS <c>.pkg</c>): a raiz é a do instalador e o
/// estado do dotnetscope (manifesto, cache e staging) fica em um diretório protegido do sistema.
/// </summary>
public sealed record InstallerGlobalLayout(string Root, string StateDirectory)
{
    public string ManifestFile => Path.Combine(StateDirectory, "manifest.json");

    public string MetadataCacheDirectory => Path.Combine(StateDirectory, "cache", "metadata");

    public string StagingDirectory => Path.Combine(StateDirectory, "staging");

    /// <summary>Windows: <c>%ProgramFiles%\dotnet</c> e <c>%ProgramData%\dotnetscope</c>; macOS: <c>/usr/local/share/dotnet</c> e <c>/var/db/dotnetscope</c>.</summary>
    public static InstallerGlobalLayout CreateDefault(string rid)
    {
        ArgumentNullException.ThrowIfNull(rid);
        return rid.StartsWith("win-", StringComparison.Ordinal)
            ? new(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "dotnetscope"))
            : new("/usr/local/share/dotnet", "/var/db/dotnetscope");
    }
}
