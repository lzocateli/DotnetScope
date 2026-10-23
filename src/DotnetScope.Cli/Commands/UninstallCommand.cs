using System.CommandLine;
using System.Text.Json;
using DotnetScope.Cli.Output;
using DotnetScope.Core;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.SystemScope;
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
        var scope = EnvCommand.ScopeOption();
        var output = new Option<string>("--output") { Description = "Formato de saída: text ou json", DefaultValueFactory = _ => "text" };
        output.AcceptOnlyFromAmong("text", "json");
        var command = new Command("uninstall", "Remove um SDK ou runtime instalado pelo dotnetscope, preservando o que outras instalações usam")
        {
            component, version, scope, installDir, force, keepEnv, dryRun, allowRoot, output,
        };

        command.SetAction((parseResult, cancellationToken) => CliApplication.ExecuteAsync(
            services,
            parseResult.GetValue(CliApplication.VerboseOption),
            async (diagnostics, ct) =>
            {
                var request = ParseExact(parseResult.GetValue(component)!, parseResult.GetValue(version));
                var home = services.HomeDirectory();
                string root;
                InstallationManifestStore manifest;
                IReadOnlyList<IUserEnvironmentConfigurator> configurators;
                if (parseResult.GetValue(scope) == "global" && GlobalScope.UsesOfficialInstaller(services.DetectRid()))
                {
                    if (parseResult.GetValue(installDir) is not null)
                    {
                        throw new DotnetScopeException(ExitCodes.Usage, "No Windows e no macOS, o escopo global não aceita --install-dir: o instalador oficial define a raiz.");
                    }

                    return await UninstallOfficialAsync(
                        services, request.Component, request.Version, parseResult.GetValue(dryRun), parseResult.GetValue(output) == "json", diagnostics, ct)
                        .ConfigureAwait(false);
                }

                if (parseResult.GetValue(scope) == "global")
                {
                    var (layout, globalRoot) = GlobalScope.Prepare(services, services.DetectRid(), parseResult.GetValue(installDir));
                    GlobalScope.EnsureTrustedRoot(services, globalRoot);
                    GlobalScope.EnsureTrustedState(services, layout);
                    root = globalRoot;
                    manifest = new InstallationManifestStore(layout.ManifestFile);
                    configurators = [services.CreateSystemEnvironment(layout)];
                    // Pacotes apt não têm raiz escolhida pelo usuário: só entram quando --install-dir não foi informado.
                    if (parseResult.GetValue(installDir) is null
                        && await FindAptRecordAsync(services, manifest, request.Component, request.Version, ct).ConfigureAwait(false) is { } aptRecord)
                    {
                        return await RemoveAptAsync(services, manifest, aptRecord, parseResult.GetValue(dryRun), parseResult.GetValue(output) == "json", diagnostics, ct)
                            .ConfigureAwait(false);
                    }
                }
                else
                {
                    if (services.IsRoot() && !parseResult.GetValue(allowRoot))
                    {
                        throw new DotnetScopeException(ExitCodes.Privilege, "O escopo user não deve ser executado como root; use --allow-root.");
                    }

                    root = InstallLocations.ValidateUserRoot(parseResult.GetValue(installDir) ?? InstallLocations.DefaultUserRoot(home), home);
                    manifest = services.CreateManifestStore();
                    configurators = services.CreateEnvironmentConfigurators(home);
                }

                var dry = parseResult.GetValue(dryRun);
                var plan = await new ArchiveUninstaller(manifest, diagnostics)
                    .UninstallAsync(root, request.Component, request.Version, parseResult.GetValue(force), dry, ct)
                    .ConfigureAwait(false);

                var environment = "unchanged";
                if (!dry && !parseResult.GetValue(keepEnv) && !manifest.Read().Any(r => InstallLocations.SameRoot(r.Root, root)))
                {
                    // Seção 4.3, regra 10: sem instalações gerenciadas na raiz, o bloco de ambiente que aponta para ela sai.
                    // No Windows, o registro e os perfis do PowerShell são verificados separadamente.
                    foreach (var candidate in configurators)
                    {
                        if (candidate.ConfiguredRoot() is { } configured && InstallLocations.SameRoot(configured, root))
                        {
                            await candidate.ExecuteAsync(candidate.PlanRemove(), diagnostics, ct).ConfigureAwait(false);
                            environment = "removed";
                        }
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

    /// <summary>
    /// Instalador oficial (D-F7-4): no Windows, remove somente instalações registradas pelo dotnetscope, executando o
    /// mesmo instalador verificado com <c>/uninstall</c>; no macOS, o <c>.pkg</c> não tem desinstalador e a CLI só orienta.
    /// </summary>
    private static async Task<int> UninstallOfficialAsync(
        CliServices services, ComponentKind component, DotnetVersion version, bool dryRun, bool json, IDiagnosticSink diagnostics, CancellationToken cancellationToken)
    {
        var rid = services.DetectRid();
        var windows = rid.StartsWith("win-", StringComparison.Ordinal);
        var layout = GlobalScope.PrepareInstaller(services, rid);
        var manifest = new InstallationManifestStore(layout.ManifestFile);
        var record = manifest.Read().FirstOrDefault(r =>
            r.Component == component && r.Version == version.ToString()
            && r.FileName.EndsWith(windows ? ".exe" : ".pkg", StringComparison.OrdinalIgnoreCase))
            ?? throw new DotnetScopeException(
                ExitCodes.NotFound,
                windows
                    ? $"{component.ToName()} {version} não foi instalado pelo dotnetscope no escopo global; use Aplicativos instalados do Windows para outras instalações."
                    : $"{component.ToName()} {version} não foi instalado pelo dotnetscope no escopo global.");

        if (!windows)
        {
            var folder = component == ComponentKind.Sdk ? $"sdk/{version}" : $"shared/<framework>/{version}";
            throw new DotnetScopeException(
                ExitCodes.Usage,
                $"O instalador .pkg da Microsoft não tem desinstalador; o dotnetscope não remove {component.ToName()} {version} automaticamente. "
                + $"Remova {record.Root}/{folder} manualmente ou use a ferramenta dotnet-core-uninstall "
                + "(https://learn.microsoft.com/dotnet/core/install/remove-runtime-sdk-versions?pivots=os-macos).");
        }

        if (!dryRun)
        {
            await services.UninstallWindowsSetup(layout, record, diagnostics, cancellationToken).ConfigureAwait(false);
        }

        if (json)
        {
            var document = new UninstallJson(
                dryRun ? "planned" : "removed", component.ToName(), version.ToString(), record.Root, "installer", [record.FileName], [], "unchanged");
            await services.Output.WriteLineAsync(JsonSerializer.Serialize(document, F4JsonContext.Default.UninstallJson)).ConfigureAwait(false);
        }
        else
        {
            await services.Output.WriteLineAsync(dryRun
                ? $"Plano: remover {component.ToName()} {version} com {record.FileName} /uninstall."
                : $"Removido: {component.ToName()} {version} pelo instalador oficial ({record.FileName} /uninstall).").ConfigureAwait(false);
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// Registro apt do componente cuja versão instalada hoje (o apt pode ter atualizado o pacote desde a instalação)
    /// é a pedida; um registro cujo pacote já não está instalado também casa pela versão registrada.
    /// </summary>
    private static async Task<Core.Installation.InstallationRecord?> FindAptRecordAsync(
        CliServices services, InstallationManifestStore manifest, ComponentKind component, DotnetVersion version, CancellationToken cancellationToken)
    {
        foreach (var record in manifest.Read().Where(r => r.Component == component))
        {
            if (AptPackages.PackageOf(record) is not { } package)
            {
                continue;
            }

            var current = await AptPackages.InstalledVersionAsync(services.CommandRunner, package, cancellationToken).ConfigureAwait(false);
            var effective = current is null ? record.Version : AptPackages.UpstreamVersion(current);
            if (effective == version.ToString())
            {
                return record;
            }
        }

        return null;
    }

    /// <summary>Instalação feita pela estratégia apt: remove o pacote registrado com apt-get e o registro do manifesto.</summary>
    private static async Task<int> RemoveAptAsync(
        CliServices services,
        InstallationManifestStore manifest,
        Core.Installation.InstallationRecord record,
        bool dryRun,
        bool json,
        IDiagnosticSink diagnostics,
        CancellationToken cancellationToken)
    {
        var package = AptPackages.PackageOf(record)!;
        if (!dryRun)
        {
            if (await AptPackages.InstalledVersionAsync(services.CommandRunner, package, cancellationToken).ConfigureAwait(false) is not null)
            {
                await AptPackages.RemoveAsync(services.CommandRunner, package, cancellationToken).ConfigureAwait(false);
            }

            await manifest.UpdateAsync(
                records => records.Where(r => AptPackages.PackageOf(r) != package).ToList(),
                diagnostics,
                CancellationToken.None).ConfigureAwait(false);
        }

        if (json)
        {
            var document = new UninstallJson(
                dryRun ? "planned" : "removed", record.Component.ToName(), record.Version, record.Root, "apt", [package], [], "unchanged");
            await services.Output.WriteLineAsync(JsonSerializer.Serialize(document, F4JsonContext.Default.UninstallJson)).ConfigureAwait(false);
        }
        else
        {
            await services.Output.WriteLineAsync(dryRun
                ? $"Plano: remover o pacote {package} (apt-get remove)."
                : $"Removido: pacote {package} (apt-get remove); dependências instaladas pelo apt permanecem.").ConfigureAwait(false);
        }

        return ExitCodes.Success;
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
