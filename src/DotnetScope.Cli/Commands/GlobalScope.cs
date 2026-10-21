using DotnetScope.Core;
using DotnetScope.Infrastructure.SystemScope;

namespace DotnetScope.Cli.Commands;

/// <summary>Pré-condições comuns do escopo global (modelo de ameaças, R05 e R17).</summary>
internal static class GlobalScope
{
    public static (GlobalLayout Layout, string Root) Prepare(CliServices services, string rid, string? installDir, bool requireRoot = true)
    {
        if (!rid.StartsWith("linux-", StringComparison.Ordinal))
        {
            throw new DotnetScopeException(
                ExitCodes.Usage,
                "O escopo global está disponível somente no Linux nesta versão; Windows e macOS chegam nas próximas etapas da F7.");
        }

        // A elevação é verificada antes de qualquer efeito; o dotnetscope nunca chama sudo.
        if (requireRoot && !services.IsRoot())
        {
            throw new DotnetScopeException(ExitCodes.Privilege, "O escopo global exige root; execute com sudo. O dotnetscope não se autoeleva.");
        }

        var layout = services.CreateGlobalLayout(rid);
        return (layout, GlobalLocations.ValidateRoot(installDir ?? layout.Root));
    }

    /// <summary>
    /// D-F7-2: no escopo global vale somente o repositório de certificados do sistema. Uma CA indicada pelo ambiente
    /// de quem elevou permitiria interceptar o metadata e os artefatos instalados como root.
    /// </summary>
    public static void IgnoreUserCertificates(CliServices services, IDiagnosticSink diagnostics)
    {
        foreach (var name in new[] { "SSL_CERT_FILE", "SSL_CERT_DIR" })
        {
            if (!string.IsNullOrEmpty(services.GetEnvironmentVariable(name)))
            {
                services.UnsetEnvironmentVariable(name);
                diagnostics.Warning($"{name} é ignorada no escopo global; instale a CA corporativa no repositório de certificados do sistema.");
            }
        }
    }

    /// <summary>R13: diretório de estado e seus arquivos validados antes de qualquer leitura ou escrita do manifesto.</summary>
    public static void EnsureTrustedState(CliServices services, GlobalLayout layout)
    {
        GlobalLocations.EnsureTrusted(layout.StateDirectory, services.StatPath, "o estado do dotnetscope");
        foreach (var file in new[] { "manifest.json", "manifest.lock", "env-state.json", "env.lock" })
        {
            GlobalLocations.EnsureTrustedFile(Path.Combine(layout.StateDirectory, file), services.StatPath);
        }
    }

    public static void EnsureTrustedRoot(CliServices services, string root) =>
        GlobalLocations.EnsureTrusted(root, services.StatPath, "a instalação");
}
