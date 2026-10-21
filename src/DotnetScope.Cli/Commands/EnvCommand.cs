using System.CommandLine;
using System.Text.Json;
using DotnetScope.Cli.Output;
using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.Cli.Commands;

internal static class EnvCommand
{
    public static Command Build(CliServices services)
    {
        var command = new Command("env", "Mostra, aplica ou remove DOTNET_ROOT e PATH nos perfis do usuário");
        command.Subcommands.Add(BuildShow(services));
        command.Subcommands.Add(BuildApply(services));
        command.Subcommands.Add(BuildRemove(services));
        return command;
    }

    private static Command BuildShow(CliServices services)
    {
        var shell = new Option<string?>("--shell") { Description = "posix (sh, bash, zsh) ou fish; padrão: shell de $SHELL" };
        shell.AcceptOnlyFromAmong("posix", "sh", "bash", "zsh", "fish");
        var installDir = InstallDirOption();
        var telemetry = TelemetryOption();
        var command = new Command("show", "Imprime o script de ambiente, por exemplo para eval \"$(dotnetscope env show)\"")
        {
            shell, installDir, telemetry,
        };
        command.SetAction((parseResult, cancellationToken) => CliApplication.ExecuteAsync(
            services,
            parseResult.GetValue(CliApplication.VerboseOption),
            async (_, _) =>
            {
                var (target, _) = ResolveTarget(services, parseResult.GetValue(installDir), parseResult.GetValue(telemetry), requireHost: false);
                var kind = parseResult.GetValue(shell) ?? (Path.GetFileName(services.LoginShell() ?? string.Empty) == "fish" ? "fish" : "posix");
                var script = kind == "fish" ? EnvironmentScripts.RenderFish(target) : EnvironmentScripts.RenderPosix(target);
                await services.Output.WriteAsync(script).ConfigureAwait(false);
                return ExitCodes.Success;
            },
            cancellationToken));
        return command;
    }

    private static Command BuildApply(CliServices services)
    {
        var installDir = InstallDirOption();
        var conflict = ConflictOption();
        var telemetry = TelemetryOption();
        var yes = new Option<bool>("--yes", "-y") { Description = "Confirma a edição de perfis que são links simbólicos" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Mostra os arquivos que seriam alterados sem gravar" };
        var output = OutputOption();
        var command = new Command("apply", "Configura DOTNET_ROOT e PATH nos perfis do usuário (bloco gerenciado, idempotente)")
        {
            installDir, conflict, telemetry, yes, dryRun, output,
        };
        command.SetAction((parseResult, cancellationToken) => CliApplication.ExecuteAsync(
            services,
            parseResult.GetValue(CliApplication.VerboseOption),
            async (diagnostics, ct) =>
            {
                var (target, home) = ResolveTarget(services, parseResult.GetValue(installDir), parseResult.GetValue(telemetry), requireHost: true);
                var configurator = new UnixEnvironmentConfigurator(services.CreateEnvironmentLayout(home));
                var plan = EnvironmentSetup.Plan(
                    services, configurator, target, ParsePolicy(parseResult.GetValue(conflict)), parseResult.GetValue(yes), parseResult.GetValue(dryRun));
                EnvironmentOutcome outcome;
                if (parseResult.GetValue(dryRun))
                {
                    outcome = new EnvironmentOutcome(
                        target,
                        plan.Skipped ? "skipped" : plan.HasChanges ? "planned" : "unchanged",
                        configurator.Layout.EnvScript,
                        plan.Profiles.Select(p => p.Path).Concat(plan.Files.Select(f => f.Path)).ToList(),
                        [],
                        plan.Conflicts,
                        null);
                }
                else
                {
                    outcome = await EnvironmentSetup.ApplyAsync(services, configurator, plan, diagnostics, ct).ConfigureAwait(false);
                }

                await WriteAsync(services, "apply", outcome, home, parseResult.GetValue(output) == "json").ConfigureAwait(false);
                return ExitCodes.Success;
            },
            cancellationToken));
        return command;
    }

    private static Command BuildRemove(CliServices services)
    {
        var dryRun = new Option<bool>("--dry-run") { Description = "Mostra os arquivos que seriam alterados sem gravar" };
        var output = OutputOption();
        var command = new Command("remove", "Remove o bloco do dotnetscope dos perfis e os scripts de ambiente, preservando o restante")
        {
            dryRun, output,
        };
        command.SetAction((parseResult, cancellationToken) => CliApplication.ExecuteAsync(
            services,
            parseResult.GetValue(CliApplication.VerboseOption),
            async (diagnostics, ct) =>
            {
                EnsureSupported(services);
                var home = services.HomeDirectory();
                var configurator = new UnixEnvironmentConfigurator(services.CreateEnvironmentLayout(home));
                var plan = configurator.PlanRemove();
                var changed = plan.Profiles.Select(p => p.Path).Concat(plan.Files.Select(f => f.Path)).ToList();
                IReadOnlyList<string> backups = [];
                if (!parseResult.GetValue(dryRun))
                {
                    var result = await configurator.ExecuteAsync(plan, diagnostics, ct).ConfigureAwait(false);
                    backups = result.Backups;
                }

                var status = !plan.HasChanges ? "unchanged" : parseResult.GetValue(dryRun) ? "planned" : "removed";
                var target = EnvironmentSetup.CreateTarget(InstallLocations.DefaultUserRoot(home), home, false);
                var outcome = new EnvironmentOutcome(target, status, null, changed, backups, [], null);
                if (parseResult.GetValue(output) == "json")
                {
                    await WriteAsync(services, "remove", outcome, home, json: true).ConfigureAwait(false);
                }
                else
                {
                    await services.Output.WriteLineAsync(status switch
                    {
                        "removed" => $"Configuração removida de: {string.Join(", ", changed)}. Abra um novo terminal para aplicar.",
                        "planned" => $"Seriam alterados: {string.Join(", ", changed)}.",
                        _ => "Nenhuma configuração do dotnetscope encontrada.",
                    }).ConfigureAwait(false);
                }

                return ExitCodes.Success;
            },
            cancellationToken));
        return command;
    }

    internal static EnvConflictPolicy ParsePolicy(string? value) => value switch
    {
        "override" => EnvConflictPolicy.Override,
        "skip" => EnvConflictPolicy.Skip,
        _ => EnvConflictPolicy.Fail,
    };

    internal static Option<string> ConflictOption()
    {
        var option = new Option<string>("--env-conflict")
        {
            Description = "Quando DOTNET_ROOT já tem outro valor: fail (padrão), override ou skip",
            DefaultValueFactory = _ => "fail",
        };
        option.AcceptOnlyFromAmong("fail", "override", "skip");
        return option;
    }

    internal static Option<bool> TelemetryOption() =>
        new("--telemetry-optout") { Description = "Inclui DOTNET_CLI_TELEMETRY_OPTOUT=1 no ambiente" };

    internal static void EnsureSupported(CliServices services)
    {
        if (services.DetectRid().StartsWith("win-", StringComparison.Ordinal))
        {
            throw new DotnetScopeException(ExitCodes.NotFound, "A configuração de ambiente no Windows ainda não está disponível nesta versão.");
        }
    }

    private static Option<string?> InstallDirOption() =>
        new("--install-dir") { Description = "Raiz .NET (padrão: ~/.dotnet)", HelpName = "diretório" };

    private static Option<string> OutputOption()
    {
        var option = new Option<string>("--output") { Description = "Formato de saída: text ou json", DefaultValueFactory = _ => "text" };
        option.AcceptOnlyFromAmong("text", "json");
        return option;
    }

    private static (EnvironmentTarget Target, string Home) ResolveTarget(CliServices services, string? installDir, bool telemetry, bool requireHost)
    {
        EnsureSupported(services);
        var home = services.HomeDirectory();
        var root = InstallLocations.ValidateUserRoot(installDir ?? InstallLocations.DefaultUserRoot(home), home);
        if (requireHost && !File.Exists(Path.Combine(root, "dotnet")))
        {
            throw new DotnetScopeException(ExitCodes.NotFound, $"Não há dotnet em {root}; instale um componente antes ou informe --install-dir.");
        }

        return (EnvironmentSetup.CreateTarget(root, home, telemetry), home);
    }

    private static async Task WriteAsync(CliServices services, string action, EnvironmentOutcome outcome, string home, bool json)
    {
        if (json)
        {
            await services.Output.WriteLineAsync(
                JsonSerializer.Serialize(new EnvCommandJson(action, outcome.ToJson()), EnvJsonContext.Default.EnvCommandJson)).ConfigureAwait(false);
            return;
        }

        if (outcome.Status == "planned")
        {
            await services.Output.WriteLineAsync($"Seriam alterados: {string.Join(", ", outcome.ChangedFiles)}.").ConfigureAwait(false);
            return;
        }

        await EnvironmentSetup.WriteTextAsync(services.Output, outcome, home).ConfigureAwait(false);
    }
}
