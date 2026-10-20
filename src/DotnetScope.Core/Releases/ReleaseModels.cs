using DotnetScope.Core.Versions;

namespace DotnetScope.Core.Releases;

public enum SupportPhase
{
    Unknown,
    Preview,
    GoLive,
    Active,
    Maintenance,
    Eol,
}

/// <summary>Canal listado no índice oficial de releases.</summary>
public sealed record ReleaseChannel(
    ChannelVersion Channel,
    ReleaseTrack Track,
    SupportPhase Phase,
    string? LatestRelease,
    DotnetVersion? LatestRuntime,
    DotnetVersion? LatestSdk,
    DateOnly? EolDate,
    Uri ReleasesUrl)
{
    public bool IsPrereleasePhase => Phase is SupportPhase.Preview or SupportPhase.GoLive;

    public bool IsSupported => Phase is SupportPhase.Active or SupportPhase.Maintenance;
}

/// <summary>Releases de um canal.</summary>
public sealed record ChannelReleases(ReleaseChannel Channel, IReadOnlyList<ChannelRelease> Releases);

/// <summary>Release de um canal, com os componentes publicados nela.</summary>
public sealed record ChannelRelease(
    string ReleaseVersion,
    DateOnly? ReleaseDate,
    bool Security,
    IReadOnlyList<ComponentRelease> Components);

/// <summary>Versão de um componente publicada em uma release.</summary>
public sealed record ComponentRelease(ComponentKind Component, DotnetVersion Version, IReadOnlyList<ReleaseFile> Files);

/// <summary>
/// Arquivo oficial de um componente, com hash SHA-512 em hexadecimal minúsculo quando disponível.
/// <paramref name="IsTrusted"/> indica se a URL pertence à allowlist de origens.
/// </summary>
public sealed record ReleaseFile(string Name, string Rid, Uri Url, string? Sha512, bool IsTrusted);

/// <summary>Fonte do metadata oficial de releases.</summary>
public interface IReleaseMetadataSource
{
    Task<IReadOnlyList<ReleaseChannel>> GetChannelsAsync(CancellationToken cancellationToken);

    Task<ChannelReleases> GetChannelReleasesAsync(ReleaseChannel channel, CancellationToken cancellationToken);
}
