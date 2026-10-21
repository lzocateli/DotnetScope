using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;

namespace DotnetScope.IntegrationTests;

/// <summary>Arquivos .tar.gz sintéticos no layout oficial, com um host "dotnet" em shell que lista a raiz.</summary>
internal static class SyntheticArchives
{
    private const UnixFileMode Executable = (UnixFileMode)0b111_101_101;
    private const UnixFileMode Regular = (UnixFileMode)0b110_100_100;

    public static string HostScript(string runtime, int exitCode = 0) =>
        $$"""
        #!/bin/sh
        # host {{runtime}}
        root=$(cd "$(dirname "$0")" && pwd)
        case "$1" in
          --list-sdks) for d in "$root"/sdk/*/; do [ -d "$d" ] && echo "$(basename "$d") [$root/sdk]"; done ;;
          --list-runtimes) for fw in "$root"/shared/*/; do for d in "$fw"*/; do [ -d "$d" ] && echo "$(basename "$fw") $(basename "$d") [$root/shared/$(basename "$fw")]"; done; done ;;
        esac
        exit {{exitCode}}
        """;

    public static byte[] Sdk(string sdk, string runtime, int hostExitCode = 0, bool omitSdkDirectory = false) =>
        Create(writer =>
        {
            Directory(writer, ".");
            File(writer, "./dotnet", HostScript(runtime, hostExitCode), Executable);
            File(writer, "./dnx", "#!/bin/sh\n", Executable);
            File(writer, "./LICENSE.txt", "licença sintética\n");
            File(writer, $"./host/fxr/{runtime}/libhostfxr.so", $"fxr {runtime}", Executable);
            File(writer, $"./shared/Microsoft.NETCore.App/{runtime}/System.Runtime.dll", $"runtime {runtime}");
            if (!omitSdkDirectory)
            {
                File(writer, $"./sdk/{sdk}/dotnet.dll", $"sdk {sdk}");
                File(writer, $"./sdk/{sdk}/AppHostTemplate/apphost", "apphost", (UnixFileMode)0b111_100_100);
            }

            File(writer, $"./packs/Microsoft.NETCore.App.Ref/{runtime}/data/FrameworkList.xml", "<x/>");
            File(writer, $"./templates/{runtime}/microsoft.dotnet.common.projecttemplates.nupkg", "nupkg");
            File(writer, "./sdk-manifests/10.0.100/microsoft.net.sdk.android/36.1.2/WorkloadManifest.json", "{}");
        });

    public static byte[] Runtime(string runtime) =>
        Create(writer =>
        {
            Directory(writer, ".");
            File(writer, "./dotnet", HostScript(runtime), Executable);
            File(writer, "./LICENSE.txt", "licença sintética\n");
            File(writer, $"./host/fxr/{runtime}/libhostfxr.so", $"fxr {runtime}", Executable);
            File(writer, $"./shared/Microsoft.NETCore.App/{runtime}/System.Runtime.dll", $"runtime {runtime}");
        });

    public static byte[] Create(Action<TarWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            write(writer);
        }

        return buffer.ToArray();
    }

    public static void Directory(TarWriter writer, string name) =>
        writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, name) { Mode = Executable });

    public static void File(TarWriter writer, string name, string content, UnixFileMode mode = Regular) =>
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            Mode = mode,
            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
        });

    public static string Sha512(byte[] content) => Convert.ToHexStringLower(SHA512.HashData(content));
}

/// <summary>Fonte de metadata em memória cujos arquivos apontam para o servidor local.</summary>
internal sealed class SyntheticMetadataSource : IReleaseMetadataSource
{
    private readonly ReleaseChannel _channel;
    private readonly List<ChannelRelease> _releases = [];

    public SyntheticMetadataSource()
    {
        _channel = new ReleaseChannel(
            new ChannelVersion(10, 0),
            ReleaseTrack.Lts,
            SupportPhase.Active,
            "10.0.13",
            DotnetVersion.Parse("10.0.13"),
            DotnetVersion.Parse("10.0.402"),
            new DateOnly(2028, 11, 14),
            new Uri("https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json"));
    }

    public void Add(string releaseVersion, ComponentKind component, string version, Uri url, string sha512, string fileName)
    {
        var file = new ReleaseFile(fileName, "linux-x64", url, sha512, IsTrusted: true);
        var componentRelease = new ComponentRelease(component, DotnetVersion.Parse(version), [file]);
        var index = _releases.FindIndex(r => r.ReleaseVersion == releaseVersion);
        if (index < 0)
        {
            _releases.Add(new ChannelRelease(releaseVersion, new DateOnly(2026, 10, 13), true, [componentRelease]));
        }
        else
        {
            _releases[index] = _releases[index] with { Components = [.. _releases[index].Components, componentRelease] };
        }
    }

    public Task<IReadOnlyList<ReleaseChannel>> GetChannelsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReleaseChannel>>([_channel]);

    public Task<ChannelReleases> GetChannelReleasesAsync(ReleaseChannel channel, CancellationToken cancellationToken) =>
        Task.FromResult(new ChannelReleases(_channel, _releases));
}
