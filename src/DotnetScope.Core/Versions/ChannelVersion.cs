using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace DotnetScope.Core.Versions;

/// <summary>Canal de releases .NET no formato <c>major.minor</c> (por exemplo, 10.0).</summary>
public readonly record struct ChannelVersion(int Major, int Minor) : IComparable<ChannelVersion>
{
    public static bool TryParse([NotNullWhen(true)] string? text, out ChannelVersion channel)
    {
        channel = default;
        var parts = text?.Split('.');
        if (parts is not { Length: 2 }
            || !DotnetVersion.TryParseNumber(parts[0], out var major)
            || !DotnetVersion.TryParseNumber(parts[1], out var minor))
        {
            return false;
        }

        channel = new ChannelVersion(major, minor);
        return true;
    }

    public int CompareTo(ChannelVersion other)
    {
        var result = Major.CompareTo(other.Major);
        return result != 0 ? result : Minor.CompareTo(other.Minor);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");

    public static bool operator <(ChannelVersion left, ChannelVersion right) => left.CompareTo(right) < 0;

    public static bool operator <=(ChannelVersion left, ChannelVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >(ChannelVersion left, ChannelVersion right) => left.CompareTo(right) > 0;

    public static bool operator >=(ChannelVersion left, ChannelVersion right) => left.CompareTo(right) >= 0;
}
