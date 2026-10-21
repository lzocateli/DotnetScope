using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;

namespace DotnetScope.Core.Resolution;

/// <summary>Plataforma alvo e política de prerelease usadas na resolução.</summary>
/// <param name="Installer">Resolve o instalador oficial (<c>.exe</c> no Windows, <c>.pkg</c> no macOS) em vez do arquivo compactado (escopo global).</param>
public sealed record ResolveOptions(string Rid, bool AllowPrerelease, bool Installer = false);

/// <summary>Versão candidata de um componente, com o arquivo compactado oficial para o RID quando existir.</summary>
public sealed record VersionCandidate(
    DotnetVersion Version,
    string ReleaseVersion,
    DateOnly? ReleaseDate,
    bool Security,
    ReleaseFile? Archive);

/// <summary>
/// Artefato escolhido para instalação, sempre com hash SHA-512. <see cref="Channel"/> é nulo em instalações offline
/// (<c>--archive</c>), que não consultam o metadata.
/// </summary>
public sealed record ResolvedArtifact(
    ComponentKind Component,
    DotnetVersion Version,
    ReleaseChannel? Channel,
    string ReleaseVersion,
    string Rid,
    ReleaseFile File)
{
    public string Sha512 => File.Sha512!;
}

/// <summary>Versões de um canal que correspondem à solicitação, da mais nova para a mais antiga.</summary>
public sealed record VersionQueryResult(
    ComponentRequest Request,
    ResolveOptions Options,
    ReleaseChannel Channel,
    IReadOnlyList<VersionCandidate> Candidates)
{
    /// <summary>
    /// Escolhe a versão mais nova que possui arquivo compactado com SHA-512 para o RID. Se a URL desse arquivo estiver
    /// fora da allowlist, a resolução falha em vez de recorrer a uma versão mais antiga.
    /// </summary>
    public ResolvedArtifact Resolve()
    {
        var candidate = Candidates.FirstOrDefault(c => c.Archive is not null);
        if (candidate is null)
        {
            var hint = Request.Component == ComponentKind.WindowsDesktop && !VersionResolver.IsWindowsRid(Options.Rid)
                ? " O componente windowsdesktop é publicado apenas para RIDs Windows."
                : string.Empty;
            throw new DotnetScopeException(
                ExitCodes.NotFound,
                $"Nenhuma versão de {Request} no canal {Channel.Channel} tem o arquivo " +
                $"{VersionResolver.ExpectedArchiveName(Request.Component, Options.Rid, Options.Installer)} com SHA-512.{hint}");
        }

        if (!candidate.Archive!.IsTrusted)
        {
            throw new DotnetScopeException(
                ExitCodes.Integrity,
                $"O metadata aponta {Request.Component.ToName()} {candidate.Version} para uma origem fora da allowlist: {candidate.Archive.Url}.");
        }

        return new ResolvedArtifact(
            Request.Component,
            candidate.Version,
            Channel,
            candidate.ReleaseVersion,
            Options.Rid,
            candidate.Archive!);
    }
}

/// <summary>Resolve especificações de versão contra o metadata oficial de releases.</summary>
public sealed class VersionResolver(IReleaseMetadataSource source)
{
    public async Task<ResolvedArtifact> ResolveAsync(
        ComponentRequest request,
        ResolveOptions options,
        CancellationToken cancellationToken) =>
        (await QueryAsync(request, options, cancellationToken).ConfigureAwait(false)).Resolve();

    public async Task<VersionQueryResult> QueryAsync(
        ComponentRequest request,
        ResolveOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (request.Version is ExactVersionSpec { Version.IsPrerelease: true } && !options.AllowPrerelease)
        {
            throw new DotnetScopeException(
                ExitCodes.Usage,
                $"A versão {request.Version} é prerelease; use --prerelease para permiti-la.");
        }

        var channels = await source.GetChannelsAsync(cancellationToken).ConfigureAwait(false);
        var channel = SelectChannel(request.Version, channels, options);
        var releases = await source.GetChannelReleasesAsync(channel, cancellationToken).ConfigureAwait(false);

        var matching = CollectCandidates(releases, request.Component, options.Rid, options.Installer)
            .Where(candidate => Matches(request.Version, candidate.Version))
            .ToList();

        var candidates = matching
            .Where(candidate => options.AllowPrerelease || !candidate.Version.IsPrerelease)
            .OrderByDescending(candidate => candidate.Version)
            .ToList();

        if (candidates.Count == 0)
        {
            var hint = matching.Count > 0 ? " Há apenas versões prerelease; use --prerelease para incluí-las." : string.Empty;
            throw new DotnetScopeException(
                ExitCodes.NotFound,
                $"Nenhuma versão de {request} foi encontrada no canal {channel.Channel}.{hint}");
        }

        return new VersionQueryResult(request, options, channel, candidates);
    }

    /// <summary>
    /// Resolve o SDK a instalar para um <c>global.json</c>, seguindo a semântica oficial de <c>rollForward</c>: as
    /// políticas <c>latest*</c> escolhem a maior versão permitida; as demais, a menor faixa que satisfaz o pedido.
    /// </summary>
    public async Task<ResolvedArtifact> ResolveGlobalJsonAsync(GlobalJsonSdk sdk, ResolveOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sdk);
        ArgumentNullException.ThrowIfNull(options);
        var minimum = sdk.Version ?? DotnetVersion.Parse("0.0.0");
        var policy = sdk.RollForward;
        var channels = (await source.GetChannelsAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => options.AllowPrerelease || !c.IsPrereleasePhase || c.Channel == minimum.Channel)
            .Where(c => policy switch
            {
                RollForwardPolicy.Patch or RollForwardPolicy.LatestPatch or RollForwardPolicy.Feature
                    or RollForwardPolicy.LatestFeature or RollForwardPolicy.Disable => c.Channel == minimum.Channel,
                RollForwardPolicy.Minor or RollForwardPolicy.LatestMinor => c.Channel.Major == minimum.Major && c.Channel >= minimum.Channel,
                _ => c.Channel >= minimum.Channel,
            });
        var ordered = policy is RollForwardPolicy.LatestMinor or RollForwardPolicy.LatestMajor
            ? channels.OrderByDescending(c => c.Channel)
            : channels.OrderBy(c => c.Channel);

        foreach (var channel in ordered)
        {
            var releases = await source.GetChannelReleasesAsync(channel, cancellationToken).ConfigureAwait(false);
            var candidates = CollectCandidates(releases, ComponentKind.Sdk, options.Rid, options.Installer)
                .Where(c => c.Archive is not null && c.Version >= minimum)
                .Where(c => options.AllowPrerelease || !c.Version.IsPrerelease || c.Version == sdk.Version)
                .ToList();
            var selected = SelectForRollForward(policy, minimum, channel.Channel, candidates);
            if (selected is not null)
            {
                return new VersionQueryResult(ComponentRequest.Create(ComponentKind.Sdk, new ExactVersionSpec(selected.Version)), options, channel, [selected])
                    .Resolve();
            }
        }

        throw new DotnetScopeException(
            ExitCodes.NotFound,
            $"Nenhum SDK com arquivo para {options.Rid} satisfaz {sdk.Path} (version {sdk.Version?.ToString() ?? "não definida"}, rollForward {RollForwardPolicies.ToName(policy)}).");
    }

    internal static VersionCandidate? SelectForRollForward(
        RollForwardPolicy policy,
        DotnetVersion minimum,
        ChannelVersion channel,
        IReadOnlyList<VersionCandidate> candidates)
    {
        static VersionCandidate? Max(IEnumerable<VersionCandidate> items) => items.MaxBy(c => c.Version);

        var sameChannel = channel == minimum.Channel;
        var inBand = candidates.Where(c => sameChannel && c.Version.FeatureBand == minimum.FeatureBand).ToList();
        var lowestBand = candidates.Count == 0
            ? []
            : candidates.Where(c => c.Version.FeatureBand == candidates.Min(x => x.Version.FeatureBand)).ToList();
        var higherBands = candidates.Where(c => sameChannel && c.Version.FeatureBand > minimum.FeatureBand).ToList();
        var nextBand = higherBands.Count == 0
            ? []
            : higherBands.Where(c => c.Version.FeatureBand == higherBands.Min(x => x.Version.FeatureBand)).ToList();

        return policy switch
        {
            RollForwardPolicy.Disable => candidates.FirstOrDefault(c => c.Version == minimum),
            RollForwardPolicy.Patch => candidates.FirstOrDefault(c => c.Version == minimum) ?? Max(inBand),
            RollForwardPolicy.LatestPatch => Max(inBand),
            RollForwardPolicy.LatestFeature or RollForwardPolicy.LatestMinor or RollForwardPolicy.LatestMajor => Max(candidates),
            _ when sameChannel => Max(inBand) ?? Max(nextBand),
            RollForwardPolicy.Minor or RollForwardPolicy.Major => Max(lowestBand),
            _ => null,
        };
    }

    private static List<VersionCandidate> CollectCandidates(ChannelReleases releases, ComponentKind component, string rid, bool installer) =>
        releases.Releases
            .SelectMany(release => release.Components
                .Where(c => c.Component == component)
                .Select(c => new VersionCandidate(c.Version, release.ReleaseVersion, release.ReleaseDate, release.Security, FindArchive(c, rid, installer))))
            .GroupBy(candidate => candidate.Version)
            .Select(group => group.First())
            .ToList();

    internal static bool IsWindowsRid(string rid) => rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase);

    internal static string ExpectedArchiveName(ComponentKind component, string rid, bool installer = false) =>
        component.ArchivePrefix() + rid + (installer ? (IsWindowsRid(rid) ? ".exe" : ".pkg") : IsWindowsRid(rid) ? ".zip" : ".tar.gz");

    private static ReleaseChannel SelectChannel(
        VersionSpec spec,
        IReadOnlyList<ReleaseChannel> channels,
        ResolveOptions options)
    {
        if (spec is SupportTrackSpec track)
        {
            return channels
                .Where(c => c.Track == track.Track && (c.IsSupported || (options.AllowPrerelease && c.IsPrereleasePhase)))
                .OrderByDescending(c => c.Channel)
                .FirstOrDefault()
                ?? throw new DotnetScopeException(
                    ExitCodes.NotFound,
                    $"Nenhum canal {track} suportado foi encontrado no metadata oficial.");
        }

        var wanted = spec switch
        {
            ExactVersionSpec exact => exact.Version.Channel,
            ChannelSpec channel => channel.Channel,
            FeatureBandSpec band => band.Channel,
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec, null),
        };

        return channels.FirstOrDefault(c => c.Channel == wanted)
            ?? throw new DotnetScopeException(
                ExitCodes.NotFound,
                $"O canal {wanted} não existe no metadata oficial de releases.");
    }

    private static bool Matches(VersionSpec spec, DotnetVersion version) => spec switch
    {
        ExactVersionSpec exact => version == exact.Version,
        FeatureBandSpec band => version.Channel == band.Channel && version.FeatureBand == band.Band,
        ChannelSpec channel => version.Channel == channel.Channel,
        SupportTrackSpec => true,
        _ => false,
    };

    private static ReleaseFile? FindArchive(ComponentRelease component, string rid, bool installer)
    {
        var name = ExpectedArchiveName(component.Component, rid, installer);
        return component.Files.FirstOrDefault(file =>
            file.Sha512 is not null
            && string.Equals(file.Rid, rid, StringComparison.OrdinalIgnoreCase)
            && string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
