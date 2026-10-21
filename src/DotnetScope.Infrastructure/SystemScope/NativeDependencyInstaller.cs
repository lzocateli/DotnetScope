using DotnetScope.Core;

namespace DotnetScope.Infrastructure.SystemScope;

/// <summary>
/// Instala as dependências nativas do .NET pelo gerenciador de pacotes da distribuição (escopo global, Linux).
/// Os nomes vêm de uma tabela fixa no código (modelo de ameaças, R19); para ICU e OpenSSL no apt, escolhe a primeira
/// alternativa da tabela com candidato nas fontes configuradas.
/// </summary>
public static class NativeDependencyInstaller
{
    private const string Dnf = "/usr/bin/dnf";
    private const string Apk = "/sbin/apk";

    private static readonly string[] AptFixed = ["ca-certificates", "libc6", "libgcc-s1", "libstdc++6", "zlib1g"];

    private static readonly string[][] AptAlternatives =
    [
        ["libicu78", "libicu77", "libicu76", "libicu75", "libicu74", "libicu73", "libicu72", "libicu71", "libicu70", "libicu69", "libicu67", "libicu66", "libicu63"],
        ["libssl3t64", "libssl3", "libssl1.1"],
    ];

    private static readonly string[] DnfPackages = ["ca-certificates", "glibc", "libgcc", "libstdc++", "zlib", "libicu", "openssl-libs"];

    private static readonly string[] ApkPackages = ["ca-certificates", "libgcc", "libstdc++", "zlib", "icu-libs", "libssl3"];

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(20);

    /// <summary>Instala as dependências e retorna o gerenciador e os pacotes usados.</summary>
    public static async Task<(string Manager, IReadOnlyList<string> Packages)> InstallAsync(ISystemCommandRunner runner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);
        if (AptPackages.IsAvailable(runner))
        {
            var packages = AptFixed.ToList();
            foreach (var alternatives in AptAlternatives)
            {
                foreach (var package in alternatives)
                {
                    if (await AptPackages.FindPackageVersionAsync(runner, package, cancellationToken).ConfigureAwait(false) is not null)
                    {
                        packages.Add(package);
                        break;
                    }
                }
            }

            await RunAsync(runner, AptPackages.AptGet, ["install", "-y", "--no-install-recommends", "--", .. packages], cancellationToken).ConfigureAwait(false);
            return ("apt", packages);
        }

        if (runner.Exists(Dnf))
        {
            await RunAsync(runner, Dnf, ["install", "-y", "--", .. DnfPackages], cancellationToken).ConfigureAwait(false);
            return ("dnf", DnfPackages);
        }

        if (runner.Exists(Apk))
        {
            await RunAsync(runner, Apk, ["add", "--no-cache", "--", .. ApkPackages], cancellationToken).ConfigureAwait(false);
            return ("apk", ApkPackages);
        }

        throw new DotnetScopeException(ExitCodes.MissingDependency, "Nenhum gerenciador de pacotes suportado (apt, dnf ou apk) foi encontrado.");
    }

    private static async Task RunAsync(ISystemCommandRunner runner, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(executable, arguments, Timeout, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var detail = result.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            throw new DotnetScopeException(ExitCodes.MissingDependency, $"{executable} terminou com código {result.ExitCode} ao instalar dependências: {detail}");
        }
    }
}
