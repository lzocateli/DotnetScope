using System.CommandLine;
using System.Text.Json;
using DotnetScope.Cli.Output;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Resolution;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Core.Versions;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.Metadata;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.Cli.Commands;

internal static class InstallCommand
{
    public static Command Build(CliServices services)
    {
        var component = new Argument<string?>("componente")
        {
            Description = "sdk, runtime, aspnetcore ou windowsdesktop; ou a forma compacta SDK-10.0.400",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var version = new Argument<string?>("versão")
        {
            Description = "10.0.400, 10.0, 10.0.4xx, lts ou sts",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var scope = new Option<string>("--scope")
        {
            Description = "Escopo da instalação: user ou global",
            DefaultValueFactory = _ => "user",
        };
        scope.AcceptOnlyFromAmong("user", "global");
        var installDir = new Option<string?>("--install-dir")
        {
            Description = "Raiz da instalação (padrão: ~/.dotnet)",
            HelpName = "diretório",
        };
        var prerelease = new Option<bool>("--prerelease") { Description = "Permite versões preview/RC" };
        var offline = new Option<bool>("--offline") { Description = "Usa somente o metadata em cache para resolver a versão" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Mostra o plano sem baixar nem alterar arquivos" };
        var yes = new Option<bool>("--yes", "-y") { Description = "Confirma o plano sem perguntar" };
        var allowRoot = new Option<bool>("--allow-root") { Description = "Permite o escopo user executando como root" };
        var configureEnv = new Option<bool>("--configure-env")
        {
            Description = "Configura DOTNET_ROOT e PATH nos perfis do usuário (true|false)",
            DefaultValueFactory = _ => true,
        };
        var envConflict = EnvCommand.ConflictOption();
        var telemetry = EnvCommand.TelemetryOption();
        var archive = new Option<string?>("--archive")
        {
            Description = "Instala a partir de um arquivo .tar.gz local, sem consultar o metadata (exige versão exata e --sha512)",
            HelpName = "arquivo",
        };
        var sha512 = new Option<string?>("--sha512") { Description = "SHA-512 esperado do arquivo de --archive", HelpName = "hash" };
        var fromGlobalJson = new Option<string?>("--from-global-json")
        {
            Description = "Instala o SDK exigido por um global.json (caminho opcional; padrão: procura a partir do diretório atual)",
            Arity = ArgumentArity.ZeroOrOne,
            HelpName = "caminho",
        };
        var output = new Option<string>("--output")
        {
            Description = "Formato de saída: text ou json",
            DefaultValueFactory = _ => "text",
        };
        output.AcceptOnlyFromAmong("text", "json");

        var command = new Command("install", "Instala um SDK ou runtime .NET a partir do arquivo oficial verificado por SHA-512")
        {
            component, version, scope, installDir, prerelease, offline, dryRun, yes, allowRoot, configureEnv, envConflict, telemetry,
            archive, sha512, fromGlobalJson, output,
        };

        command.SetAction((parseResult, cancellationToken) => CliApplication.ExecuteAsync(
            services,
            parseResult.GetValue(CliApplication.VerboseOption),
            (diagnostics, ct) => RunAsync(
                services,
                diagnostics,
                new InstallOptions(
                    parseResult.GetValue(component),
                    parseResult.GetValue(version),
                    parseResult.GetValue(scope) == "global" ? InstallScope.Global : InstallScope.User,
                    parseResult.GetValue(installDir),
                    parseResult.GetValue(prerelease),
                    parseResult.GetValue(offline),
                    parseResult.GetValue(dryRun),
                    parseResult.GetValue(yes),
                    parseResult.GetValue(allowRoot),
                    parseResult.GetValue(output) == "json",
                    parseResult.GetValue(configureEnv),
                    EnvCommand.ParsePolicy(parseResult.GetValue(envConflict)),
                    parseResult.GetValue(telemetry),
                    parseResult.GetValue(archive),
                    parseResult.GetValue(sha512),
                    parseResult.GetResult(fromGlobalJson) is not null,
                    parseResult.GetValue(fromGlobalJson)),
                ct),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliServices services,
        IDiagnosticSink diagnostics,
        InstallOptions options,
        CancellationToken cancellationToken)
    {
        if (options.Scope == InstallScope.Global)
        {
            throw new DotnetScopeException(ExitCodes.Usage, "O escopo global ainda não está disponível nesta versão; use --scope user.");
        }

        var rid = services.DetectRid();
        if (rid.StartsWith("win-", StringComparison.Ordinal))
        {
            throw new DotnetScopeException(ExitCodes.NotFound, "A instalação no Windows ainda não está disponível nesta versão.");
        }

        ValidateSourceOptions(options);
        if (services.IsRoot() && !options.AllowRoot)
        {
            throw new DotnetScopeException(
                ExitCodes.Privilege,
                "O escopo user não deve ser executado como root; use um usuário comum ou --allow-root.");
        }

        var home = services.HomeDirectory();
        var root = InstallLocations.ValidateUserRoot(options.InstallDir ?? InstallLocations.DefaultUserRoot(home), home);
        var (artifact, requestLabel, needsConfirmation) = await ResolveAsync(services, diagnostics, options, rid, cancellationToken)
            .ConfigureAwait(false);

        if (artifact.Channel?.Phase == SupportPhase.Eol)
        {
            diagnostics.Warning($"o canal {artifact.Channel.Channel} está fora de suporte e não recebe correções de segurança.");
        }

        var installed = InstallLocations.IsInstalled(root, artifact.Component, artifact.Version);

        // O ambiente é planejado antes de baixar: conflito de DOTNET_ROOT falha sem alterar nada.
        var target = EnvironmentSetup.CreateTarget(root, home, options.TelemetryOptOut);
        UnixEnvironmentConfigurator? configurator = null;
        EnvironmentPlan? environmentPlan = null;
        if (options.ConfigureEnv)
        {
            configurator = new UnixEnvironmentConfigurator(services.CreateEnvironmentLayout(home));
            environmentPlan = EnvironmentSetup.Plan(services, configurator, target, options.EnvConflict, options.Yes, options.DryRun);
        }

        if (options.DryRun)
        {
            var status = installed ? "alreadyInstalled" : "planned";
            var planned = environmentPlan is null
                ? Disabled(target)
                : new EnvironmentOutcome(
                    target,
                    environmentPlan.Skipped ? "skipped" : environmentPlan.HasChanges ? "planned" : "unchanged",
                    configurator!.Layout.EnvScript,
                    environmentPlan.Profiles.Select(p => p.Path).Concat(environmentPlan.Files.Select(f => f.Path)).ToList(),
                    [],
                    environmentPlan.Conflicts,
                    null);
            await WriteAsync(services, options, requestLabel, artifact, root, home, status, null, [], planned).ConfigureAwait(false);
            return ExitCodes.Success;
        }

        var missing = services.FindMissingDependencies();
        foreach (var dependency in missing)
        {
            diagnostics.Warning($"dependência nativa não encontrada: {dependency}.");
        }

        if (!installed && needsConfirmation)
        {
            Confirm(services, options, requestLabel, artifact, root);
        }

        IProgress<DownloadProgress>? progress = !options.Json && services.Prompt.IsInteractive
            ? new ConsoleDownloadProgress(services.Error)
            : null;
        using var installer = services.CreateInstaller(diagnostics, progress);
        var result = await installer.InstallAsync(artifact, root, options.Scope, cancellationToken).ConfigureAwait(false);
        var statusName = result.Status == InstallStatus.Installed ? "installed" : "alreadyInstalled";
        EnvironmentOutcome environment;
        if (configurator is null)
        {
            environment = Disabled(target);
        }
        else if (!result.Validation.Succeeded)
        {
            environment = new EnvironmentOutcome(target, "notApplied", null, [], [], [], null);
        }
        else
        {
            // Replaneja para não gravar sobre perfis alterados durante o download.
            var fresh = configurator.PlanApply(target, options.EnvConflict);
            environment = await EnvironmentSetup.ApplyAsync(services, configurator, fresh, diagnostics, cancellationToken).ConfigureAwait(false);
        }

        await WriteAsync(services, options, requestLabel, artifact, root, home, statusName, result, missing, environment).ConfigureAwait(false);

        if (!result.Validation.Succeeded)
        {
            var hint = missing.Count > 0 ? $" Dependências ausentes: {string.Join("; ", missing)}." : string.Empty;
            throw new DotnetScopeException(
                missing.Count > 0 ? ExitCodes.MissingDependency : ExitCodes.UnexpectedError,
                $"{artifact.Component.ToName()} {artifact.Version} foi gravado em {root}, mas a validação falhou: {result.Validation.Detail}.{hint}");
        }

        return ExitCodes.Success;
    }

    private static void ValidateSourceOptions(InstallOptions options)
    {
        if (options.FromGlobalJson && options.Archive is not null)
        {
            throw new DotnetScopeException(ExitCodes.Usage, "--from-global-json e --archive não podem ser usados juntos.");
        }

        if (options.Sha512 is not null && options.Archive is null)
        {
            throw new DotnetScopeException(ExitCodes.Usage, "--sha512 só é usado com --archive.");
        }

        if (options.Archive is not null && (options.Sha512 is not { Length: 128 } hash || !hash.All(char.IsAsciiHexDigit)))
        {
            throw new DotnetScopeException(ExitCodes.Usage, "--archive exige --sha512 com 128 caracteres hexadecimais.");
        }

        if (options.FromGlobalJson && (options.Version is not null || (options.Component is not null && options.Component != "sdk")))
        {
            throw new DotnetScopeException(ExitCodes.Usage, "--from-global-json instala somente o SDK; não informe versão nem outro componente.");
        }
    }

    private static async Task<(ResolvedArtifact Artifact, string Label, bool NeedsConfirmation)> ResolveAsync(
        CliServices services,
        IDiagnosticSink diagnostics,
        InstallOptions options,
        string rid,
        CancellationToken cancellationToken)
    {
        if (options.Archive is not null)
        {
            var request = ParseRequest(options.Component, options.Version);
            if (request.Version is not ExactVersionSpec exact)
            {
                throw new DotnetScopeException(ExitCodes.Usage, "--archive exige a versão exata do componente contido no arquivo.");
            }

            var path = Path.GetFullPath(options.Archive, services.CurrentDirectory());
            if (!File.Exists(path))
            {
                throw new DotnetScopeException(ExitCodes.NotFound, $"Arquivo não encontrado: {path}.");
            }

            if (!path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                throw new DotnetScopeException(ExitCodes.Usage, $"--archive aceita somente .tar.gz nesta plataforma: {path}.");
            }

            var file = new ReleaseFile(Path.GetFileName(path), rid, new Uri(path), options.Sha512!.ToLowerInvariant(), IsTrusted: true);
            return (new ResolvedArtifact(request.Component, exact.Version, null, exact.Version.ToString(), rid, file), $"{request} (arquivo local)", false);
        }

        var source = services.CreateMetadataSource(new MetadataOptions(options.Offline), diagnostics);
        try
        {
            var resolver = new VersionResolver(source);
            var resolveOptions = new ResolveOptions(rid, options.AllowPrerelease);
            if (options.FromGlobalJson)
            {
                var sdk = GlobalJsonReader.Read(options.GlobalJsonPath, services.CurrentDirectory());
                diagnostics.Verbose($"global.json: {sdk.Path} (version {sdk.Version}, rollForward {RollForwardPolicies.ToName(sdk.RollForward)})");
                var effective = sdk with { AllowPrerelease = sdk.AllowPrerelease || options.AllowPrerelease };
                var artifact = await resolver.ResolveGlobalJsonAsync(effective, resolveOptions with { AllowPrerelease = effective.AllowPrerelease }, cancellationToken)
                    .ConfigureAwait(false);
                var label = $"global.json {sdk.Version?.ToString() ?? "(sem versão)"} ({RollForwardPolicies.ToName(sdk.RollForward)})";
                return (artifact, label, artifact.Version != sdk.Version);
            }

            var parsed = ParseRequest(options.Component, options.Version);
            var resolved = await resolver.ResolveAsync(parsed, resolveOptions, cancellationToken).ConfigureAwait(false);
            return (resolved, parsed.Version.ToString(), parsed.Version is not ExactVersionSpec);
        }
        finally
        {
            (source as IDisposable)?.Dispose();
        }
    }

    private static ComponentRequest ParseRequest(string? component, string? version)
    {
        if (component is null)
        {
            throw new DotnetScopeException(
                ExitCodes.Usage,
                "Informe componente e versão (por exemplo, sdk 10.0.400), a forma compacta SDK-10.0.400 ou --from-global-json.");
        }

        if (version is null)
        {
            var dash = component.IndexOf('-', StringComparison.Ordinal);
            if (dash <= 0 || !ComponentKindNames.TryParse(component[..dash], out _))
            {
                throw new DotnetScopeException(
                    ExitCodes.Usage,
                    $"Informe componente e versão (por exemplo, sdk 10.0.400) ou a forma compacta SDK-10.0.400; recebido: '{component}'.");
            }

            return ComponentRequest.Parse(null, component);
        }

        if (!ComponentKindNames.TryParse(component, out var kind))
        {
            throw new DotnetScopeException(
                ExitCodes.Usage,
                $"Componente inválido: '{component}'. Use {string.Join(", ", ComponentKindNames.All)}.");
        }

        return ComponentRequest.Parse(kind, version);
    }

    private static void Confirm(
        CliServices services,
        InstallOptions options,
        string request,
        ResolvedArtifact artifact,
        string root)
    {
        if (options.Yes)
        {
            return;
        }

        var question = $"{request} resolve para {artifact.Component.ToName()} {artifact.Version}. Instalar em {root}?";
        if (!services.Prompt.IsInteractive)
        {
            throw new DotnetScopeException(
                ExitCodes.Usage,
                $"{request} resolve para {artifact.Version}; confirme com --yes ou informe a versão exata.");
        }

        if (!services.Prompt.Confirm(question))
        {
            throw new DotnetScopeException(ExitCodes.Canceled, "Instalação cancelada pelo usuário.");
        }
    }

    private static async Task WriteAsync(
        CliServices services,
        InstallOptions options,
        string request,
        ResolvedArtifact artifact,
        string root,
        string home,
        string status,
        InstallResult? result,
        IReadOnlyList<string> missing,
        EnvironmentOutcome environment)
    {
        if (options.Json)
        {
            var document = new InstallJson(
                status,
                artifact.Component.ToName(),
                request,
                artifact.Version.ToString(),
                artifact.Rid,
                options.Scope == InstallScope.Global ? "global" : "user",
                root,
                new ArtifactJson(artifact.File.Name, artifact.File.Url.ToString(), artifact.Sha512),
                result?.AddedPaths ?? [],
                result?.SharedPaths ?? [],
                result?.HostReplaced ?? false,
                result is null ? null : new ValidationJson(result.Validation.Succeeded, result.Validation.Detail),
                missing,
                environment.ToJson());
            await services.Output.WriteLineAsync(JsonSerializer.Serialize(document, InstallJsonContext.Default.InstallJson))
                .ConfigureAwait(false);
            return;
        }

        var what = $"{artifact.Component.ToName()} {artifact.Version}";
        var line = status switch
        {
            "planned" => $"Plano: instalar {what} em {root} a partir de {artifact.File.Url} (SHA-512 {artifact.Sha512[..16]}…).",
            "alreadyInstalled" => $"{what} já está instalado em {root}.",
            _ => $"Instalado: {what} em {root} ({artifact.File.Name}, SHA-512 verificado).",
        };
        await services.Output.WriteLineAsync(line).ConfigureAwait(false);
        if (result is not null && result.Validation.Succeeded)
        {
            await services.Output.WriteLineAsync($"Validação: {result.Validation.Detail}.").ConfigureAwait(false);
        }

        if (options.DryRun)
        {
            var environmentLine = environment.Status switch
            {
                "planned" => $"Ambiente: seriam alterados {string.Join(", ", environment.ChangedFiles)}.",
                "unchanged" => "Ambiente: já configurado.",
                "skipped" => "Ambiente: não seria alterado (--env-conflict skip).",
                _ => "Ambiente: não seria alterado (--configure-env false).",
            };
            await services.Output.WriteLineAsync(environmentLine).ConfigureAwait(false);
            return;
        }

        if (environment.Status == "disabled")
        {
            await services.Output.WriteLineAsync("Ambiente não alterado (--configure-env false).").ConfigureAwait(false);
            await EnvironmentSetup.WriteManualInstructionsAsync(services.Output, environment.Target).ConfigureAwait(false);
        }
        else if (environment.Status != "notApplied")
        {
            await EnvironmentSetup.WriteTextAsync(services.Output, environment, home).ConfigureAwait(false);
        }
    }

    private static EnvironmentOutcome Disabled(EnvironmentTarget target) => new(target, "disabled", null, [], [], [], null);

    private sealed record InstallOptions(
        string? Component,
        string? Version,
        InstallScope Scope,
        string? InstallDir,
        bool AllowPrerelease,
        bool Offline,
        bool DryRun,
        bool Yes,
        bool AllowRoot,
        bool Json,
        bool ConfigureEnv,
        EnvConflictPolicy EnvConflict,
        bool TelemetryOptOut,
        string? Archive,
        string? Sha512,
        bool FromGlobalJson,
        string? GlobalJsonPath);
}
