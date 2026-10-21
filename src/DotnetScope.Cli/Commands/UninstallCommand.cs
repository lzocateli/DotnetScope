using System.CommandLine;
using System.Text.Json;
using DotnetScope.Cli.Output;
using DotnetScope.Core;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.Cli.Commands;

internal static class UninstallCommand
{
    public static Command Build(CliServices services)
    {
        var component = new Argument<string>("componente") { Description = "sdk, runtime, aspnetcore ou windowsdesktop; ou SDK-10.0.400" };
        var version = new Argument<string?>("versão") { Description = "Versão exata instalada", Arity = ArgumentArity.ZeroOrOne };
        var installDir = new Option<string?>("--install-dir") { Description = "Raiz da instalação (padrão: ~/.dotnet; no Windows, %LOCALAPPDATA%\\Microsoft\\dotnet)", HelpName = "diretório" };
        var force = new Option<bool>("--force") { Description = "Remove a pasta do componente mesmo se instalada por outra ferramenta" };
        var keepEnv = new Option<bool>("--keep-env") { Description = "Mantém DOTNET_ROOT/PATH nos perfis mesmo sem instalações gerenciadas" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Mostra o que seria removido sem alterar nada" };
        var allowRoot = new Option<bool>("--allow-root") { Description = "Permite o escopo user executando como root" };
        var output = new Option<string>("--output") { Description = "Formato de saída: text ou json", DefaultValueFactory = _ => "text" };
        output.AcceptOnlyFromAmong("text", "json");
        var command = new Command("uninstall", "Remove um SDK ou runtime instalado pelo dotnetscope, preservando o que outras instalações usam")
        {
            component, version, installDir, force, keepEnv, dryRun, allowRoot, output,
        };

        command.SetAction((parseResult, cancellationToken) => CliApplication.ExecuteAsync(
            services,
            parseResult.GetValue(CliApplication.VerboseOption),
            async (diagnostics, ct) =>
            {
                if (services.IsRoot() && !parseResult.GetValue(allowRoot))
                {
                    throw new DotnetScopeException(ExitCodes.Privilege, "O escopo user não deve ser executado como root; use --allow-root.");
                }

                var request = ParseExact(parseResult.GetValue(component)!, parseResult.GetValue(version));
                var home = services.HomeDirectory();
                var root = InstallLocations.ValidateUserRoot(parseResult.GetValue(installDir) ?? InstallLocations.DefaultUserRoot(home), home);
                var manifest = services.CreateManifestStore();
                var dry = parseResult.GetValue(dryRun);
                var plan = await new ArchiveUninstaller(manifest, diagnostics)
                    .UninstallAsync(root, request.Component, request.Version, parseResult.GetValue(force), dry, ct)
                    .ConfigureAwait(false);

                var environment = "unchanged";
                if (!dry && !parseResult.GetValue(keepEnv) && !manifest.Read().Any(r => InstallLocations.SameRoot(r.Root, root)))
                {
                    // Seção 4.3, regra 10: sem instalações gerenciadas na raiz, o bloco de ambiente que aponta para ela sai.
                    var configurator = services.CreateEnvironmentConfigurator(home);
                    if (configurator.ConfiguredRoot() is { } configured && InstallLocations.SameRoot(configured, root))
                    {
                        await configurator.ExecuteAsync(configurator.PlanRemove(), diagnostics, ct).ConfigureAwait(false);
                        environment = "removed";
                    }
                }

                var status = dry ? "planned" : "removed";
                if (parseResult.GetValue(output) == "json")
                {
                    var document = new UninstallJson(
                        status,
                        request.Component.ToName(),
                        request.Version.ToString(),
                        root,
                        plan.External ? "external" : "dotnetscope",
                        plan.Remove,
                        plan.Keep.Select(k => new KeptPathJson(k.Path, k.UsedBy)).ToList(),
                        environment);
                    await services.Output.WriteLineAsync(JsonSerializer.Serialize(document, F4JsonContext.Default.UninstallJson)).ConfigureAwait(false);
                    return ExitCodes.Success;
                }

                var what = $"{request.Component.ToName()} {request.Version}";
                await services.Output.WriteLineAsync(dry
                    ? $"Plano: remover {what} de {root} ({plan.Remove.Count} pastas)."
                    : $"Removido: {what} de {root} ({plan.Remove.Count} pastas).").ConfigureAwait(false);
                foreach (var kept in plan.Keep)
                {
                    await services.Output.WriteLineAsync($"  mantido {kept.Path}: usado por {kept.UsedBy}").ConfigureAwait(false);
                }

                if (environment == "removed")
                {
                    await services.Output.WriteLineAsync("Nenhuma instalação gerenciada restou na raiz; DOTNET_ROOT/PATH foram removidos dos perfis.")
                        .ConfigureAwait(false);
                }

                return ExitCodes.Success;
            },
            cancellationToken));
        return command;
    }

    private static (ComponentKind Component, DotnetVersion Version) ParseExact(string component, string? version)
    {
        ComponentRequest request;
        if (version is null)
        {
            request = ComponentRequest.Parse(null, component);
        }
        else
        {
            request = ComponentKindNames.TryParse(component, out var kind)
                ? ComponentRequest.Parse(kind, version)
                : throw new DotnetScopeException(ExitCodes.Usage, $"Componente inválido: '{component}'.");
        }

        return request.Version is ExactVersionSpec exact
            ? (request.Component, exact.Version)
            : throw new DotnetScopeException(ExitCodes.Usage, $"uninstall exige a versão exata instalada; recebido: {request.Version}.");
    }
}
