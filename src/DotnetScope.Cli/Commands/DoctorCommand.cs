using System.CommandLine;
using System.Text.Json;
using DotnetScope.Cli.Output;
using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.Cli.Commands;

internal static class DoctorCommand
{
    public static Command Build(CliServices services)
    {
        var repair = new Option<bool>("--repair") { Description = "Desfaz operações interrompidas, remove staging remanescente e registros órfãos" };
        var installDir = new Option<string?>("--install-dir") { Description = "Raiz adicional a verificar", HelpName = "diretório" };
        var output = new Option<string>("--output") { Description = "Formato de saída: text ou json", DefaultValueFactory = _ => "text" };
        output.AcceptOnlyFromAmong("text", "json");
        var command = new Command("doctor", "Verifica instalações, manifesto e ambiente; --repair corrige o que for seguro")
        {
            repair, installDir, output,
        };

        command.SetAction((parseResult, cancellationToken) => CliApplication.ExecuteAsync(
            services,
            parseResult.GetValue(CliApplication.VerboseOption),
            async (diagnostics, ct) =>
            {
                var home = services.HomeDirectory();
                var manifest = services.CreateManifestStore();
                var roots = new List<string> { InstallLocations.DefaultUserRoot(home) };
                if (parseResult.GetValue(installDir) is { } extra)
                {
                    roots.Add(InstallLocations.ValidateUserRoot(extra, home));
                }

                roots.AddRange(manifest.Read().Select(r => r.Root));
                var findings = (await new InstallationDoctor(manifest, diagnostics)
                    .CheckAsync(roots, parseResult.GetValue(repair), ct)
                    .ConfigureAwait(false)).ToList();
                findings.AddRange(await CheckEnvironmentAsync(services, home, ct).ConfigureAwait(false));
                findings.AddRange(services.FindMissingDependencies()
                    .Select(d => new DoctorFinding("native-dependency", FindingSeverity.Warning, $"Dependência nativa não encontrada: {d}.", false, false)));

                var healthy = findings.All(f => f.Severity != FindingSeverity.Error || f.Repaired);
                if (parseResult.GetValue(output) == "json")
                {
                    var document = new DoctorJson(
                        healthy,
                        findings.Select(f => new FindingJson(f.Code, f.Severity.ToString().ToLowerInvariant(), f.Message, f.Repairable, f.Repaired)).ToList());
                    await services.Output.WriteLineAsync(JsonSerializer.Serialize(document, F4JsonContext.Default.DoctorJson)).ConfigureAwait(false);
                }
                else if (findings.Count == 0)
                {
                    await services.Output.WriteLineAsync("Nenhum problema encontrado.").ConfigureAwait(false);
                }
                else
                {
                    foreach (var finding in findings)
                    {
                        var label = finding.Severity switch
                        {
                            FindingSeverity.Error => "erro",
                            FindingSeverity.Warning => "aviso",
                            _ => "info",
                        };
                        var suffix = finding.Repaired ? " (corrigido)" : finding.Repairable ? " (corrigível com --repair)" : string.Empty;
                        await services.Output.WriteLineAsync($"{label} [{finding.Code}] {finding.Message}{suffix}").ConfigureAwait(false);
                    }
                }

                return healthy ? ExitCodes.Success : ExitCodes.Conflict;
            },
            cancellationToken));
        return command;
    }

    private static async Task<IEnumerable<DoctorFinding>> CheckEnvironmentAsync(CliServices services, string home, CancellationToken cancellationToken)
    {
        var findings = new List<DoctorFinding>();
        if (services.IsWindowsTarget)
        {
            return CheckWindowsEnvironment(services.CreateWindowsEnvironment(home));
        }

        var configurator = new UnixEnvironmentConfigurator(services.CreateEnvironmentLayout(home));
        var root = configurator.ConfiguredRoot();
        var blocks = configurator.PlanRemove().Profiles.Count;
        if (root is null)
        {
            if (blocks > 0)
            {
                findings.Add(new DoctorFinding(
                    "env-script-missing",
                    FindingSeverity.Warning,
                    "Perfis carregam o bloco do dotnetscope, mas o env.sh não existe; execute dotnetscope env apply ou env remove.",
                    false,
                    false));
            }

            return findings;
        }

        if (!File.Exists(Path.Combine(root, "dotnet")))
        {
            findings.Add(new DoctorFinding("env-root-missing", FindingSeverity.Warning, $"DOTNET_ROOT aponta para {root}, que não tem dotnet.", false, false));
            return findings;
        }

        if (blocks == 0)
        {
            findings.Add(new DoctorFinding(
                "env-profile-missing",
                FindingSeverity.Warning,
                "O env.sh existe, mas nenhum perfil o carrega; execute dotnetscope env apply.",
                false,
                false));
        }

        var probe = await services.ProbeLoginShell(services.LoginShell(), home, cancellationToken).ConfigureAwait(false);
        if (probe is not null && Path.GetDirectoryName(probe.ResolvedDotnet ?? string.Empty) != root)
        {
            findings.Add(new DoctorFinding(
                "login-shell",
                FindingSeverity.Warning,
                $"Em um shell de login novo ({probe.Shell}), dotnet resolve para {probe.ResolvedDotnet ?? "nada"} em vez de {root}.",
                false,
                false));
        }

        return findings;
    }

    private static List<DoctorFinding> CheckWindowsEnvironment(WindowsEnvironmentConfigurator configurator)
    {
        var findings = new List<DoctorFinding>();
        if (configurator.ConfiguredRoot() is not { } root)
        {
            return findings;
        }

        if (!File.Exists(Path.Combine(root, "dotnet.exe")))
        {
            findings.Add(new DoctorFinding("env-root-missing", FindingSeverity.Warning, $"DOTNET_ROOT aponta para {root}, que não tem dotnet.exe.", false, false));
            return findings;
        }

        if (configurator.PlanApply(new EnvironmentTarget(root, Path.Combine(configurator.Home, ".dotnet", "tools"), false), EnvConflictPolicy.Override)
            .ChangedVariables.Any(v => v.Name != "DOTNET_CLI_TELEMETRY_OPTOUT"))
        {
            findings.Add(new DoctorFinding(
                "env-registry-changed",
                FindingSeverity.Warning,
                $"DOTNET_ROOT ou Path em {configurator.Store.Location} não correspondem mais à configuração do dotnetscope; execute dotnetscope env apply.",
                false,
                false));
        }

        var (verification, shadowed) = configurator.VerifyNewProcess(root);
        if (verification.Matches != true)
        {
            findings.Add(new DoctorFinding(
                "new-terminal",
                FindingSeverity.Warning,
                shadowed
                    ? $"Em um terminal novo, dotnet resolve para {verification.ResolvedDotnet}, que vem do Path da máquina e tem precedência sobre {root}."
                    : $"Em um terminal novo, dotnet resolve para {verification.ResolvedDotnet ?? "nada"} em vez de {root}.",
                false,
                false));
        }

        return findings;
    }
}
