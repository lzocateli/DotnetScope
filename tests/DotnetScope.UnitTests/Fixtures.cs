using DotnetScope.Core.Releases;
using DotnetScope.Infrastructure.Http;
using DotnetScope.Infrastructure.Metadata;

namespace DotnetScope.UnitTests;

internal static class Fixtures
{
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "metadata");

    public static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(Directory, name));

    public static Uri IndexUrl { get; } = new("https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json");
}

/// <summary>Fonte de metadata em memória construída a partir das fixtures pelo mapper real.</summary>
internal sealed class FixtureMetadataSource : IReleaseMetadataSource
{
    public int ChannelRequests { get; private set; }

    public Task<IReadOnlyList<ReleaseChannel>> GetChannelsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(MetadataMapper.ParseIndex(Fixtures.Read("releases-index.json"), Fixtures.IndexUrl));

    public Task<ChannelReleases> GetChannelReleasesAsync(ReleaseChannel channel, CancellationToken cancellationToken)
    {
        ChannelRequests++;
        return Task.FromResult(MetadataMapper.ParseChannel(Fixtures.Read($"releases-{channel.Channel}.json"), channel, TrustedEndpoints.Official));
    }
}
