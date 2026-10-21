using DotnetScope.Cli.Output;
using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.Cli.Commands;

/// <summary>Resultado da configuração de ambiente, usado por <c>install</c> e <c>env</c>.</summary>
internal sealed record EnvironmentOutcome(
    EnvironmentTarget Target,
    string Status,
    string? Script,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> Backups,
    IReadOnlyList<ThirdPartyAssignment> Conflicts,
    LoginShellResult? LoginShell)
{
    public bool LoginShellMatches =>
        LoginShell?.Matches
        ?? (LoginShell?.ResolvedDotnet is { } dotnet
            && string.Equals(Path.GetDirectoryName(dotnet), ThirdPartyEnvironment.Normalize(Target.DotnetRoot), StringComparison.Ordinal));

    public EnvironmentJson ToJson() => new(
        Target.DotnetRoot,
        [Target.DotnetRoot, Target.ToolsDirectory],
        Status,
        Script,
        ChangedFiles,
        Backups,
        Conflicts.Select(c => new ConflictJson(c.File, c.Line, c.RawValue)).ToList(),
        LoginShell is null ? null : new LoginShellJson(LoginShell.Shell, LoginShell.ResolvedDotnet, LoginShell.DotnetRoot, LoginShellMatches));
}

/// <summary>Planejamento, confirmação, aplicação e verificação da configuração de ambiente (perfis ou registro).</summary>
internal static class EnvironmentSetup
{
    public static EnvironmentTarget CreateTarget(string root, string home, bool telemetryOptOut) =>
        new(root, Path.Combine(home, ".dotnet", "tools"), telemetryOptOut);

    /// <summary>Planeja e confirma (links simbólicos exigem confirmação); lança antes de qualquer gravação.</summary>
    public static EnvironmentPlan Plan(
        CliServices services,
        IUserEnvironmentConfigurator configurator,
        EnvironmentTarget target,
        EnvConflictPolicy policy,
        bool yes,
        bool dryRun)
    {
        var plan = configurator.PlanApply(target, policy);
        if (plan.RequiresConfirmation && !yes && !dryRun)
        {
            var links = string.Join(", ", plan.Profiles.Where(p => p.IsSymlink).Select(p => $"{p.Path} -> {p.WritePath}"));
            if (!services.Prompt.IsInteractive)
            {
                throw new DotnetScopeException(
                    ExitCodes.Usage,
                    $"Perfis que são links simbólicos serão editados no alvo ({links}); confirme com --yes.");
            }

            if (!services.Prompt.Confirm($"Editar o alvo dos links simbólicos ({links})?"))
            {
                throw new DotnetScopeException(ExitCodes.Canceled, "Configuração de ambiente cancelada pelo usuário.");
            }
        }

        return plan;
    }

    public static async Task<EnvironmentOutcome> ApplyAsync(
        CliServices services,
        IUserEnvironmentConfigurator configurator,
        EnvironmentPlan plan,
        IDiagnosticSink diagnostics,
        CancellationToken cancellationToken)
    {
        var target = plan.Target!;
        if (plan.Skipped)
        {
            foreach (var conflict in plan.Conflicts)
            {
                diagnostics.Warning($"DOTNET_ROOT definido em {conflict.Location} ({conflict.RawValue}); ambiente não alterado (--env-conflict skip).");
            }

            return new EnvironmentOutcome(target, "skipped", null, [], [], plan.Conflicts, null);
        }

        var result = await configurator.ExecuteAsync(plan, diagnostics, CancellationToken.None).ConfigureAwait(false);
        if (configurator is WindowsEnvironmentConfigurator windows)
        {
            var (verification, shadowed) = windows.VerifyNewProcess(target.DotnetRoot);
            if (shadowed)
            {
                diagnostics.Warning(
                    $"o Path da máquina tem precedência sobre o do usuário e contém {verification.ResolvedDotnet}; em um terminal novo, "
                    + $"dotnet resolve para ele, não para {target.DotnetRoot}. Remova-o do Path da máquina (exige administrador).");
            }
            else if (verification.Matches != true)
            {
                diagnostics.Warning($"em um terminal novo, dotnet resolve para {verification.ResolvedDotnet ?? "nada"}; verifique o Path do usuário.");
            }

            return new EnvironmentOutcome(
                target, result.ChangedFiles.Count > 0 ? "applied" : "unchanged", null, result.ChangedFiles, result.Backups, plan.Conflicts, verification);
        }

        foreach (var system in new[] { "/usr/bin/dotnet", "/usr/local/bin/dotnet" }.Where(File.Exists))
        {
            diagnostics.Warning($"há um dotnet do sistema em {system}; os shells configurados priorizam {target.DotnetRoot}.");
        }

        LoginShellResult? probe = null;
        try
        {
            probe = await services.ProbeLoginShell(services.LoginShell(), configurator.Home, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A verificação é informativa: qualquer falha vira aviso sem desfazer a configuração.
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            diagnostics.Warning($"não foi possível verificar um shell de login novo: {exception.Message}");
        }

        var outcome = new EnvironmentOutcome(
            target,
            result.ChangedFiles.Count > 0 ? "applied" : "unchanged",
            configurator.SessionScript,
            result.ChangedFiles,
            result.Backups,
            plan.Conflicts,
            probe);
        if (probe is not null && !outcome.LoginShellMatches)
        {
            diagnostics.Warning(
                $"em um shell de login novo ({probe.Shell}), dotnet resolve para {probe.ResolvedDotnet ?? "nada"}; verifique o PATH dos seus perfis.");
        }

        return outcome;
    }

    public static async Task WriteTextAsync(TextWriter output, EnvironmentOutcome outcome, string home, bool windows)
    {
        switch (outcome.Status)
        {
            case "applied":
                await output.WriteLineAsync($"Ambiente configurado: {string.Join(", ", outcome.ChangedFiles.Select(f => Display(f, home)))}.")
                    .ConfigureAwait(false);
                if (outcome.Backups.Count > 0)
                {
                    await output.WriteLineAsync($"Backups: {string.Join(", ", outcome.Backups.Select(f => Display(f, home)))}.").ConfigureAwait(false);
                }

                break;
            case "unchanged":
                await output.WriteLineAsync(windows
                    ? "Ambiente já configurado; nenhuma variável foi alterada."
                    : "Ambiente já configurado; nenhum perfil foi alterado.").ConfigureAwait(false);
                break;
            default:
                await output.WriteLineAsync("Ambiente não alterado.").ConfigureAwait(false);
                break;
        }

        if (outcome.Script is not null)
        {
            await output.WriteLineAsync($"Para usar nesta sessão: . {ShellQuoting.Posix(outcome.Script)}").ConfigureAwait(false);
        }
        else if (windows && outcome.Status is "applied" or "unchanged")
        {
            await output.WriteLineAsync(
                "Terminais novos já recebem o ambiente. Para usar nesta sessão: dotnetscope env show | Out-String | Invoke-Expression")
                .ConfigureAwait(false);
        }
        else
        {
            await WriteManualInstructionsAsync(output, outcome.Target, windows).ConfigureAwait(false);
        }
    }

    public static Task WriteManualInstructionsAsync(TextWriter output, EnvironmentTarget target, bool windows) =>
        output.WriteLineAsync(windows
            ? $"""
              Para usar nesta sessão (PowerShell):
                $env:DOTNET_ROOT = {ShellQuoting.PowerShell(target.DotnetRoot)}
                $env:PATH = {ShellQuoting.PowerShell(target.DotnetRoot + ";" + target.ToolsDirectory + ";")} + $env:PATH
              Para configurar terminais novos: dotnetscope env apply
              """
            : $"""
              Para usar nesta sessão:
                export DOTNET_ROOT={ShellQuoting.Posix(target.DotnetRoot)}
                export PATH={ShellQuoting.Posix(target.DotnetRoot)}:{ShellQuoting.Posix(target.ToolsDirectory)}:"$PATH"
              """);

    private static string Display(string path, string home) =>
        path.StartsWith(home.TrimEnd('/') + "/", StringComparison.Ordinal) ? "~" + path[home.TrimEnd('/').Length..] : path;
}
