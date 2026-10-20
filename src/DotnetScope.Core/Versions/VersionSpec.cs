using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace DotnetScope.Core.Versions;

/// <summary>
/// Especificação de versão informada pelo usuário: exata, canal, feature band do SDK, <c>lts</c> ou <c>sts</c>.
/// </summary>
public abstract record VersionSpec
{
    public static VersionSpec Parse(string text) =>
        TryParse(text, out var spec)
            ? spec
            : throw new DotnetScopeException(
                ExitCodes.Usage,
                $"Versão inválida: '{text}'. Use, por exemplo, 10.0.400, 10.0, 10.0.4xx, lts ou sts.");

    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out VersionSpec? spec)
    {
        spec = null;
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (string.Equals(value, "lts", StringComparison.OrdinalIgnoreCase))
        {
            spec = new SupportTrackSpec(ReleaseTrack.Lts);
        }
        else if (string.Equals(value, "sts", StringComparison.OrdinalIgnoreCase))
        {
            spec = new SupportTrackSpec(ReleaseTrack.Sts);
        }
        else if (ChannelVersion.TryParse(value, out var channel))
        {
            spec = new ChannelSpec(channel);
        }
        else if (TryParseFeatureBand(value, out var band))
        {
            spec = band;
        }
        else if (DotnetVersion.TryParse(value, out var exact))
        {
            spec = new ExactVersionSpec(exact);
        }

        return spec is not null;
    }

    private static bool TryParseFeatureBand(string value, [NotNullWhen(true)] out FeatureBandSpec? band)
    {
        band = null;
        var parts = value.Split('.');
        if (parts.Length != 3
            || parts[2].Length != 3
            || !parts[2].EndsWith("xx", StringComparison.OrdinalIgnoreCase)
            || !char.IsAsciiDigit(parts[2][0])
            || parts[2][0] == '0'
            || !ChannelVersion.TryParse($"{parts[0]}.{parts[1]}", out var channel))
        {
            return false;
        }

        band = new FeatureBandSpec(channel, parts[2][0] - '0');
        return true;
    }
}

/// <summary>Versão exata, por exemplo 10.0.400 ou 11.0.100-rc.2.26504.105.</summary>
public sealed record ExactVersionSpec(DotnetVersion Version) : VersionSpec
{
    public override string ToString() => Version.ToString();
}

/// <summary>Último patch de um canal, por exemplo 10.0.</summary>
public sealed record ChannelSpec(ChannelVersion Channel) : VersionSpec
{
    public override string ToString() => Channel.ToString();
}

/// <summary>Último patch de uma feature band do SDK, por exemplo 10.0.4xx.</summary>
public sealed record FeatureBandSpec(ChannelVersion Channel, int Band) : VersionSpec
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Channel}.{Band}xx");
}

/// <summary>Canal mais recente do tipo de suporte indicado.</summary>
public sealed record SupportTrackSpec(ReleaseTrack Track) : VersionSpec
{
    public override string ToString() => Track == ReleaseTrack.Lts ? "lts" : "sts";
}

public enum ReleaseTrack
{
    Unknown,
    Lts,
    Sts,
}
