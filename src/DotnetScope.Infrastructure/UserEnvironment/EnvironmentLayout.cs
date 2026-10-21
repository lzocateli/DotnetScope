namespace DotnetScope.Infrastructure.UserEnvironment;

/// <summary>Caminhos usados pela configuração de ambiente do usuário em Linux e macOS.</summary>
/// <param name="BashLoginWithoutProfile">
/// macOS: o shell de login é bash e não há <c>~/.bash_profile</c> nem <c>~/.bash_login</c>; nesse caso o bash de login lê
/// somente <c>~/.profile</c>, que passa a ser criado.
/// </param>
public sealed record EnvironmentLayout(
    string Home,
    string ConfigDirectory,
    string StateDirectory,
    bool IsMacOS,
    bool FishAvailable,
    IReadOnlyList<string> SystemFiles,
    bool BashLoginWithoutProfile = false)
{
    public string EnvScript => Path.Combine(ConfigDirectory, "dotnetscope", "env.sh");

    public string FishScript => Path.Combine(ConfigDirectory, "fish", "conf.d", "dotnetscope.fish");

    public string BackupDirectory => Path.Combine(StateDirectory, "dotnetscope", "backups");

    public string StateFile => Path.Combine(StateDirectory, "dotnetscope", "env-state.json");

    public string LockFile => Path.Combine(StateDirectory, "dotnetscope", "env.lock");

    /// <summary>
    /// Perfis candidatos e se podem ser criados quando ausentes: ~/.profile no Linux; ~/.zprofile no macOS, e também
    /// ~/.profile quando o shell de login é bash sem ~/.bash_profile nem ~/.bash_login.
    /// </summary>
    public IReadOnlyList<(string Path, bool Creatable)> Profiles =>
    [
        (Path.Combine(Home, ".profile"), !IsMacOS || BashLoginWithoutProfile),
        (Path.Combine(Home, ".bash_profile"), false),
        (Path.Combine(Home, ".bash_login"), false),
        (Path.Combine(Home, ".bashrc"), false),
        (Path.Combine(Home, ".zprofile"), IsMacOS),
        (Path.Combine(Home, ".zshrc"), false),
    ];

    /// <summary>Arquivos que podem conter <c>DOTNET_ROOT</c> de terceiros além dos perfis.</summary>
    public IEnumerable<string> ThirdPartyFiles =>
        Profiles.Select(p => p.Path)
            .Append(Path.Combine(Home, ".zshenv"))
            .Append(Path.Combine(ConfigDirectory, "fish", "config.fish"))
            .Concat(SystemFiles);

    public static EnvironmentLayout CreateDefault(string home) =>
        Create(home, Environment.GetEnvironmentVariable, OperatingSystem.IsMacOS(), Environment.GetEnvironmentVariable("PATH"));

    internal static EnvironmentLayout Create(string home, Func<string, string?> environment, bool isMacOS, string? path)
    {
        var config = Absolute(environment("XDG_CONFIG_HOME")) ?? Path.Combine(home, ".config");
        var state = Absolute(environment("XDG_STATE_HOME")) ?? Path.Combine(home, ".local", "state");
        var fish = Directory.Exists(Path.Combine(config, "fish"))
            || (path ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries).Any(d => File.Exists(Path.Combine(d, "fish")));
        var system = new List<string> { "/etc/environment", "/etc/profile" };
        if (Directory.Exists("/etc/profile.d"))
        {
            system.AddRange(Directory.EnumerateFiles("/etc/profile.d", "*.sh").Order(StringComparer.Ordinal));
        }

        var bashLogin = isMacOS
            && Path.GetFileName(environment("SHELL") ?? string.Empty) == "bash"
            && !File.Exists(Path.Combine(home, ".bash_profile"))
            && !File.Exists(Path.Combine(home, ".bash_login"));
        return new EnvironmentLayout(home, config, state, isMacOS, fish, system, bashLogin);
    }

    private static string? Absolute(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) ? path : null;
}
