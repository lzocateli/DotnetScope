namespace DotnetScope.Infrastructure.SystemScope;

/// <summary>
/// Caminhos fixos do escopo global no Linux. Nunca derivam de <c>HOME</c> nem de <c>XDG_*</c>, que vêm do ambiente
/// de quem elevou (modelo de ameaças, R07 e R08).
/// </summary>
/// <param name="Root">Raiz padrão da instalação por arquivo.</param>
/// <param name="StateDirectory">Manifesto, estado de ambiente, backups e lock.</param>
/// <param name="CacheDirectory">Cache de metadata do escopo global.</param>
/// <param name="ProfileScript">Script carregado pelos shells de login de todos os usuários.</param>
/// <param name="InstallLocationFile">Arquivo que o host .NET lê para localizar o runtime global.</param>
/// <param name="SystemFiles">Arquivos de terceiros onde <c>DOTNET_ROOT</c> é procurado.</param>
/// <param name="SystemDotnetLinks">Executáveis <c>dotnet</c> no PATH padrão do sistema.</param>
public sealed record GlobalLayout(
    string Root,
    string StateDirectory,
    string CacheDirectory,
    string ProfileScript,
    string InstallLocationFile,
    IReadOnlyList<string> SystemFiles,
    IReadOnlyList<string> SystemDotnetLinks)
{
    public string ManifestFile => Path.Combine(StateDirectory, "manifest.json");

    public string MetadataCacheDirectory => Path.Combine(CacheDirectory, "metadata");

    public static GlobalLayout CreateDefault(string rid)
    {
        ArgumentNullException.ThrowIfNull(rid);
        var architecture = rid[(rid.LastIndexOf('-') + 1)..];
        var system = new List<string> { "/etc/environment", "/etc/profile" };
        if (Directory.Exists("/etc/profile.d"))
        {
            system.AddRange(Directory.EnumerateFiles("/etc/profile.d", "*.sh").Order(StringComparer.Ordinal));
        }

        return new GlobalLayout(
            "/usr/local/share/dotnet",
            "/var/lib/dotnetscope",
            "/var/cache/dotnetscope",
            "/etc/profile.d/dotnetscope.sh",
            $"/etc/dotnet/install_location_{architecture}",
            system,
            ["/usr/bin/dotnet", "/usr/local/bin/dotnet"]);
    }
}
