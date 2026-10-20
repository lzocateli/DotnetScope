using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;

namespace DotnetScope.Core.Resolution;

/// <summary>Plataforma alvo e política de prerelease usadas na resolução.</summary>
public sealed record ResolveOptions(string Rid, bool AllowPrerelease);

/// <summary>Versão candidata de um componente, com o arquivo compactado oficial para o RID quando existir.</summary>
public sealed record VersionCandidate(
    DotnetVersion Version,
    string ReleaseVersion,
    DateOnly? ReleaseDate,
    bool Security,
    ReleaseFile? Archive);

/// <summary>Artefato escolhido para instalação, sempre com hash SHA-512.</summary>
public sealed record ResolvedArtifact(
    ComponentKind Component,
    DotnetVersion Version,
    ReleaseChannel Channel,
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
                $"{VersionResolver.ExpectedArchiveName(Request.Component, Options.Rid)} com SHA-512.{hint}");
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

        var matching = releases.Releases
            .SelectMany(release => release.Components
                .Where(component => component.Component == request.Component && Matches(request.Version, component.Version))
                .Select(component => new VersionCandidate(
                    component.Version,
                    release.ReleaseVersion,
                    release.ReleaseDate,
                    release.Security,
                    FindArchive(component, options.Rid))))
            .GroupBy(candidate => candidate.Version)
            .Select(group => group.First())
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

    internal static bool IsWindowsRid(string rid) => rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase);

    internal static string ExpectedArchiveName(ComponentKind component, string rid) =>
        component.ArchivePrefix() + rid + (IsWindowsRid(rid) ? ".zip" : ".tar.gz");

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

    private static ReleaseFile? FindArchive(ComponentRelease component, string rid)
    {
        var name = ExpectedArchiveName(component.Component, rid);
        return component.Files.FirstOrDefault(file =>
            file.Sha512 is not null
            && string.Equals(file.Rid, rid, StringComparison.OrdinalIgnoreCase)
            && string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
