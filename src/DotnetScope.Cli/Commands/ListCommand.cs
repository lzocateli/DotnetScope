using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using DotnetScope.Cli.Output;
using DotnetScope.Core;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Resolution;
using DotnetScope.Core.Versions;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.Metadata;

namespace DotnetScope.Cli.Commands;

internal static class ListCommand
{
    public static Command Build(CliServices services)
    {
        var available = new Option<string?>("--available")
        {
            Description = "Lista canais disponíveis ou, com um valor (10.0, 10.0.4xx, 10.0.400, lts, sts ou SDK-10.0), as versões que correspondem e a que seria resolvida",
            Arity = ArgumentArity.ZeroOrOne,
            HelpName = "versão",
        };
        var component = new Option<string?>("--component", "-c")
        {
            Description = "Componente: sdk (padrão), runtime, aspnetcore ou windowsdesktop",
        };
        component.AcceptOnlyFromAmong([.. ComponentKindNames.All]);
        var installed = new Option<bool>("--installed") { Description = "Lista os componentes instalados e sua origem (dotnetscope, external ou package)" };
        var installDir = new Option<string?>("--install-dir") { Description = "Raiz do usuário em --installed (padrão: ~/.dotnet)", HelpName = "diretório" };
        var prerelease = new Option<bool>("--prerelease") { Description = "Inclui versões e canais preview/RC" };
        var offline = new Option<bool>("--offline") { Description = "Usa somente o metadata em cache" };
        var output = new Option<string>("--output")
        {
            Description = "Formato de saída: text ou json",
            DefaultValueFactory = _ => "text",
        };
        output.AcceptOnlyFromAmong("text", "json");

        var command = new Command("list", "Lista canais e versões do metadata oficial de releases .NET")
        {
            available, installed, installDir, component, prerelease, offline, output,
        };

        command.SetAction((parseResult, cancellationToken) => CliApplication.ExecuteAsync(
            services,
            parseResult.GetValue(CliApplication.VerboseOption),
            async (diagnostics, ct) =>
            {
                var wantsInstalled = parseResult.GetValue(installed);
                if ((parseResult.GetResult(available) is null) == !wantsInstalled)
                {
                    throw new DotnetScopeException(ExitCodes.Usage, "Informe exatamente uma opção: --available ou --installed.");
                }

                if (wantsInstalled)
                {
                    return await ListInstalledAsync(services, parseResult.GetValue(installDir), parseResult.GetValue(output) == "json")
                        .ConfigureAwait(false);
                }

                var source = services.CreateMetadataSource(
                    new MetadataOptions(parseResult.GetValue(offline)),
                    diagnostics);
                try
                {
                    var json = parseResult.GetValue(output) == "json";
                    var allowPrerelease = parseResult.GetValue(prerelease);
                    var value = parseResult.GetValue(available);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        if (parseResult.GetValue(component) is not null)
                        {
                            throw new DotnetScopeException(ExitCodes.Usage, "--component exige uma versão em --available.");
                        }

                        return await ListChannelsAsync(services, source, allowPrerelease, json, ct).ConfigureAwait(false);
                    }

                    ComponentKind? kind = ComponentKindNames.TryParse(parseResult.GetValue(component), out var parsed) ? parsed : null;
                    var request = ComponentRequest.Parse(kind, value);
                    var options = new ResolveOptions(services.DetectRid(), allowPrerelease);
                    return await ListVersionsAsync(services, diagnostics, source, request, options, json, ct).ConfigureAwait(false);
                }
                finally
                {
                    (source as IDisposable)?.Dispose();
                }
            },
            cancellationToken));

        return command;
    }

    private static async Task<int> ListInstalledAsync(CliServices services, string? installDir, bool json)
    {
        EnvCommand.EnsureSupported(services);
        var home = services.HomeDirectory();
        var userRoot = InstallLocations.ValidateUserRoot(installDir ?? InstallLocations.DefaultUserRoot(home), home);
        var records = services.CreateManifestStore().Read();
        var roots = new[] { userRoot }.Concat(records.Select(r => r.Root)).Concat(services.SystemRoots()).Where(Directory.Exists);
        var items = InstalledInventory.Scan(roots, records, home, services.IsPackageManaged);

        if (json)
        {
            var document = new InstalledJson(items.Select(i => new InstalledItemJson(
                i.Component.ToName(), i.Version.ToString(), OriginName(i.Origin), i.ManagedBy, i.Root, i.Present)).ToList());
            await services.Output.WriteLineAsync(JsonSerializer.Serialize(document, F4JsonContext.Default.InstalledJson)).ConfigureAwait(false);
            return ExitCodes.Success;
        }

        if (items.Count == 0)
        {
            await services.Output.WriteLineAsync("Nenhum SDK ou runtime .NET encontrado.").ConfigureAwait(false);
            return ExitCodes.Success;
        }

        var table = new TextTable("Componente", "Versão", "Origem", "Raiz", "Observação");
        foreach (var item in items)
        {
            table.Add(
                item.Component.ToName(),
                item.Version.ToString(),
                OriginName(item.Origin),
                item.Root,
                !item.Present ? "registrado, mas ausente (dotnetscope doctor)" : item.ManagedBy is null ? "-" : $"instalado com {item.ManagedBy}");
        }

        await table.WriteAsync(services.Output).ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static string OriginName(InstallOrigin origin) => origin switch
    {
        InstallOrigin.DotnetScope => "dotnetscope",
        InstallOrigin.Package => "package",
        _ => "external",
    };

    private static async Task<int> ListChannelsAsync(
        CliServices services,
        IReleaseMetadataSource source,
        bool allowPrerelease,
        bool json,
        CancellationToken cancellationToken)
    {
        var channels = (await source.GetChannelsAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => allowPrerelease || !c.IsPrereleasePhase)
            .ToList();

        if (json)
        {
            var document = new ChannelsJson(channels.Select(ToJson).ToList());
            await services.Output.WriteLineAsync(JsonSerializer.Serialize(document, CliJsonContext.Default.ChannelsJson))
                .ConfigureAwait(false);
            return ExitCodes.Success;
        }

        var table = new TextTable("Canal", "Tipo", "Fase", "Última release", "Último runtime", "Último SDK", "Fim do suporte");
        foreach (var channel in channels)
        {
            table.Add(
                channel.Channel.ToString(),
                TrackName(channel.Track),
                PhaseName(channel.Phase),
                channel.LatestRelease ?? "-",
                channel.LatestRuntime?.ToString() ?? "-",
                channel.LatestSdk?.ToString() ?? "-",
                FormatDate(channel.EolDate) ?? "-");
        }

        await table.WriteAsync(services.Output).ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static async Task<int> ListVersionsAsync(
        CliServices services,
        IDiagnosticSink diagnostics,
        IReleaseMetadataSource source,
        ComponentRequest request,
        ResolveOptions options,
        bool json,
        CancellationToken cancellationToken)
    {
        var resolver = new VersionResolver(source);
        var query = await resolver.QueryAsync(request, options, cancellationToken).ConfigureAwait(false);
        ResolvedArtifact? resolved = null;
        DotnetScopeException? resolutionError = null;
        try
        {
            resolved = query.Resolve();
        }
        catch (DotnetScopeException exception)
        {
            resolutionError = exception;
        }

        if (query.Channel.Phase == SupportPhase.Eol)
        {
            diagnostics.Warning(
                $"o canal {query.Channel.Channel} está fora de suporte desde {FormatDate(query.Channel.EolDate) ?? "data desconhecida"}.");
        }

        if (json)
        {
            var document = new VersionsJson(
                request.Component.ToName(),
                request.Version.ToString(),
                options.Rid,
                ToJson(query.Channel),
                query.Candidates.Select(c => new VersionJson(
                    c.Version.ToString(),
                    c.ReleaseVersion,
                    FormatDate(c.ReleaseDate),
                    c.Security,
                    c.Archive is { IsTrusted: true })).ToList(),
                resolved is null
                    ? null
                    : new ResolvedJson(resolved.Version.ToString(), resolved.File.Name, resolved.File.Url.ToString(), resolved.Sha512));
            await services.Output.WriteLineAsync(JsonSerializer.Serialize(document, CliJsonContext.Default.VersionsJson))
                .ConfigureAwait(false);
        }
        else
        {
            await services.Output.WriteLineAsync(
                $"{request} — canal {query.Channel.Channel} ({TrackName(query.Channel.Track)}, {PhaseName(query.Channel.Phase)}), RID {options.Rid}")
                .ConfigureAwait(false);
            var table = new TextTable(" ", "Versão", "Release", "Data", "Segurança", "Arquivo");
            foreach (var candidate in query.Candidates)
            {
                table.Add(
                    resolved is not null && candidate.Version == resolved.Version ? "*" : " ",
                    candidate.Version.ToString(),
                    candidate.ReleaseVersion,
                    FormatDate(candidate.ReleaseDate) ?? "-",
                    candidate.Security ? "sim" : "não",
                    candidate.Archive switch
                    {
                        null => "indisponível",
                        { IsTrusted: false } => "origem recusada",
                        _ => "disponível",
                    });
            }

            await table.WriteAsync(services.Output).ConfigureAwait(false);
            if (resolved is not null)
            {
                await services.Output.WriteLineAsync(
                    $"Resolvida: {resolved.Component.ToName()} {resolved.Version} ({resolved.File.Name})").ConfigureAwait(false);
            }
        }

        if (resolutionError is not null)
        {
            throw resolutionError;
        }

        return ExitCodes.Success;
    }

    private static ChannelJson ToJson(ReleaseChannel channel) => new(
        channel.Channel.ToString(),
        TrackName(channel.Track),
        PhaseName(channel.Phase),
        channel.LatestRelease,
        channel.LatestRuntime?.ToString(),
        channel.LatestSdk?.ToString(),
        FormatDate(channel.EolDate));

    private static string TrackName(ReleaseTrack track) => track switch
    {
        ReleaseTrack.Lts => "lts",
        ReleaseTrack.Sts => "sts",
        _ => "unknown",
    };

    private static string PhaseName(SupportPhase phase) => phase switch
    {
        SupportPhase.Preview => "preview",
        SupportPhase.GoLive => "go-live",
        SupportPhase.Active => "active",
        SupportPhase.Maintenance => "maintenance",
        SupportPhase.Eol => "eol",
        _ => "unknown",
    };

    private static string? FormatDate(DateOnly? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
