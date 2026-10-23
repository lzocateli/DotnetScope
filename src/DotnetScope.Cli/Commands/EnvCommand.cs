using System.CommandLine;
using System.Text.Json;
using DotnetScope.Cli.Output;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.Cli.Commands;

internal static class EnvCommand
{
    public static Command Build(CliServices services)
    {
        var command = new Command("env", "Mostra, aplica ou remove DOTNET_ROOT e PATH nos perfis do usuário (registro ou perfis do PowerShell no Windows)");
        command.Subcommands.Add(BuildShow(services));
        command.Subcommands.Add(BuildApply(services));
        command.Subcommands.Add(BuildRemove(services));
        return command;
    }

    private static Command BuildShow(CliServices services)
    {
        var shell = new Option<string?>("--shell")
        {
            Description = "posix (sh, bash, zsh), fish, pwsh (powershell) ou cmd; padrão: shell de $SHELL, ou pwsh no Windows",
        };
        shell.AcceptOnlyFromAmong("posix", "sh", "bash", "zsh", "fish", "pwsh", "powershell", "cmd");
        var installDir = InstallDirOption();
        var telemetry = TelemetryOption();
        var scope = ScopeOption();
        var command = new Command(
            "show",
            "Imprime o script de ambiente da sessão: eval \"$(dotnetscope env show)\" ou, no PowerShell, dotnetscope env show | Out-String | Invoke-Expression")
        {
            shell, installDir, telemetry, scope,
        };
        command.SetAction((parseResult, cancellationToken) => CliApplication.ExecuteAsync(
            services,
            parseResult.GetValue(CliApplication.VerboseOption),
            async (_, _) =>
            {
                if (parseResult.GetValue(scope) == "global")
                {
                    if (parseResult.GetValue(shell) is { } requested && requested is not ("posix" or "sh" or "bash" or "zsh"))
                    {
                        throw new DotnetScopeException(ExitCodes.Usage, "O escopo global gera somente o script POSIX de /etc/profile.d.");
                    }

                    var (_, globalRoot) = GlobalScope.Prepare(services, services.DetectRid(), parseResult.GetValue(installDir), requireRoot: false);
                    await services.Output.WriteAsync(EnvironmentScripts.RenderSystemProfile(globalRoot, parseResult.GetValue(telemetry))).ConfigureAwait(false);
                    return ExitCodes.Success;
                }

                var (target, _) = ResolveTarget(services, parseResult.GetValue(installDir), parseResult.GetValue(telemetry), requireHost: false);
                var kind = parseResult.GetValue(shell)
                    ?? (services.IsWindowsTarget ? "pwsh" : Path.GetFileName(services.LoginShell() ?? string.Empty) == "fish" ? "fish" : "posix");
                var script = kind switch
                {
                    "fish" => EnvironmentScripts.RenderFish(target),
                    "pwsh" or "powershell" => EnvironmentScripts.RenderPowerShell(target),
                    "cmd" => EnvironmentScripts.RenderCmd(target),
                    _ => EnvironmentScripts.RenderPosix(target),
                };
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
        var dryRun = new Option<bool>("--dry-run") { Description = "Mostra os perfis ou variáveis que seriam alterados sem gravar" };
        var output = OutputOption();
        var scope = ScopeOption();
        var windowsTarget = WindowsTargetOption();
        var command = new Command(
            "apply",
            "Configura DOTNET_ROOT e PATH nos perfis do usuário (bloco gerenciado) ou, no Windows, em HKCU\\Environment ou nos perfis do PowerShell; idempotente")
        {
            installDir, conflict, telemetry, yes, dryRun, output, scope, windowsTarget,
        };
        command.SetAction((parseResult, cancellationToken) => CliApplication.ExecuteAsync(
            services,
            parseResult.GetValue(CliApplication.VerboseOption),
            async (diagnostics, ct) =>
            {
                EnvironmentTarget target;
                string home;
                IUserEnvironmentConfigurator configurator;
                var global = parseResult.GetValue(scope) == "global";
                var windows = ParseWindowsTarget(services, parseResult.GetValue(windowsTarget), global);
                if (global)
                {
                    home = services.HomeDirectory();
                    var (layout, globalRoot) = GlobalScope.Prepare(services, services.DetectRid(), parseResult.GetValue(installDir));
                    GlobalScope.EnsureTrustedRoot(services, globalRoot);
                    GlobalScope.EnsureTrustedState(services, layout);
                    RequireHost(globalRoot);
                    target = EnvironmentSetup.CreateGlobalTarget(globalRoot, parseResult.GetValue(telemetry));
                    configurator = services.CreateSystemEnvironment(layout);
                }
                else
                {
                    (target, home) = ResolveTarget(services, parseResult.GetValue(installDir), parseResult.GetValue(telemetry), requireHost: true);
                    configurator = services.CreateEnvironmentConfigurator(home, windows);
                    EnvironmentSetup.WarnOtherWindowsTarget(services, home, windows, diagnostics);
                }

                var plan = EnvironmentSetup.Plan(
                    services,
                    configurator,
                    target,
                    ParsePolicy(parseResult.GetValue(conflict)),
                    parseResult.GetValue(yes),
                    parseResult.GetValue(dryRun),
                    diagnostics);
                EnvironmentOutcome outcome;
                if (parseResult.GetValue(dryRun))
                {
                    outcome = new EnvironmentOutcome(
                        target,
                        plan.Skipped ? "skipped" : plan.HasChanges ? "planned" : "unchanged",
                        configurator.SessionScript,
                        plan.ChangedTargets,
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
        var dryRun = new Option<bool>("--dry-run") { Description = "Mostra os perfis ou variáveis que seriam alterados sem gravar" };
        var output = OutputOption();
        var scope = ScopeOption();
        var command = new Command(
            "remove",
            "Remove o bloco do dotnetscope dos perfis (no Windows, também o que ele gravou no registro), preservando o restante")
        {
            dryRun, output, scope,
        };
        command.SetAction((parseResult, cancellationToken) => CliApplication.ExecuteAsync(
            services,
            parseResult.GetValue(CliApplication.VerboseOption),
            async (diagnostics, ct) =>
            {
                var home = services.HomeDirectory();
                var global = parseResult.GetValue(scope) == "global";
                IReadOnlyList<IUserEnvironmentConfigurator> configurators;
                var targetRoot = InstallLocations.DefaultUserRoot(home);
                if (global)
                {
                    var (layout, globalRoot) = GlobalScope.Prepare(services, services.DetectRid(), installDir: null);
                    GlobalScope.EnsureTrustedState(services, layout);
                    var system = services.CreateSystemEnvironment(layout);
                    targetRoot = system.ConfiguredRoot() ?? globalRoot;
                    configurators = [system];
                }
                else
                {
                    // No Windows, remove tanto o registro quanto os perfis do PowerShell, qualquer que tenha sido o destino.
                    configurators = services.CreateEnvironmentConfigurators(home);
                }

                var plans = configurators.Select(c => (Configurator: c, Plan: c.PlanRemove())).ToList();
                var changed = plans.SelectMany(p => p.Plan.ChangedTargets).ToList();
                var backups = new List<string>();
                if (!parseResult.GetValue(dryRun))
                {
                    foreach (var (configurator, plan) in plans)
                    {
                        var result = await configurator.ExecuteAsync(plan, diagnostics, ct).ConfigureAwait(false);
                        backups.AddRange(result.Backups);
                    }
                }

                var status = !plans.Any(p => p.Plan.HasChanges) ? "unchanged" : parseResult.GetValue(dryRun) ? "planned" : "removed";
                var target = global ? EnvironmentSetup.CreateGlobalTarget(targetRoot, false) : EnvironmentSetup.CreateTarget(targetRoot, home, false);
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

    internal static Option<string> ScopeOption()
    {
        var option = new Option<string>("--scope")
        {
            Description = "user (padrão) ou global (Linux, exige root)",
            DefaultValueFactory = _ => "user",
        };
        option.AcceptOnlyFromAmong("user", "global");
        return option;
    }

    /// <summary>Destino do ambiente no Windows; sem valor, o registro.</summary>
    internal static Option<string?> WindowsTargetOption()
    {
        var option = new Option<string?>("--windows-env-target")
        {
            Description = "Windows: onde gravar o ambiente do usuário, registry (padrão, HKCU\\Environment) ou profile (perfis do PowerShell, sem registro)",
        };
        option.AcceptOnlyFromAmong("registry", "profile");
        return option;
    }

    /// <summary>Valida <c>--windows-env-target</c>: só no Windows e no escopo user.</summary>
    internal static WindowsEnvironmentTarget ParseWindowsTarget(CliServices services, string? value, bool global)
    {
        if (value is null)
        {
            return WindowsEnvironmentTarget.Registry;
        }

        if (!services.IsWindowsTarget)
        {
            throw new DotnetScopeException(ExitCodes.Usage, "--windows-env-target só se aplica ao Windows.");
        }

        if (global)
        {
            throw new DotnetScopeException(ExitCodes.Usage, "--windows-env-target só se aplica ao escopo user; no global, o ambiente é do instalador oficial.");
        }

        return value == "profile" ? WindowsEnvironmentTarget.Profile : WindowsEnvironmentTarget.Registry;
    }

    internal static Option<bool> TelemetryOption() =>
        new("--telemetry-optout") { Description = "Inclui DOTNET_CLI_TELEMETRY_OPTOUT=1 no ambiente" };

    private static Option<string?> InstallDirOption() =>
        new("--install-dir") { Description = "Raiz .NET (padrão: ~/.dotnet; no Windows, %LOCALAPPDATA%\\Microsoft\\dotnet)", HelpName = "diretório" };

    private static Option<string> OutputOption()
    {
        var option = new Option<string>("--output") { Description = "Formato de saída: text ou json", DefaultValueFactory = _ => "text" };
        option.AcceptOnlyFromAmong("text", "json");
        return option;
    }

    private static (EnvironmentTarget Target, string Home) ResolveTarget(CliServices services, string? installDir, bool telemetry, bool requireHost)
    {
        var home = services.HomeDirectory();
        var root = InstallLocations.ValidateUserRoot(installDir ?? InstallLocations.DefaultUserRoot(home), home);
        if (requireHost)
        {
            RequireHost(root);
        }

        return (EnvironmentSetup.CreateTarget(root, home, telemetry), home);
    }

    private static void RequireHost(string root)
    {
        if (!File.Exists(Path.Combine(root, DotnetLayout.HostExecutable(OperatingSystem.IsWindows()))))
        {
            throw new DotnetScopeException(ExitCodes.NotFound, $"Não há dotnet em {root}; instale um componente antes ou informe --install-dir.");
        }
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

        await EnvironmentSetup.WriteTextAsync(services.Output, outcome, home, services.IsWindowsTarget).ConfigureAwait(false);
    }
}
