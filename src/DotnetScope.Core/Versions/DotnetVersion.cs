using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace DotnetScope.Core.Versions;

/// <summary>
/// Versão .NET no formato <c>major.minor.patch[-prerelease]</c>, ordenada pelas regras do SemVer 2.0.
/// </summary>
public sealed class DotnetVersion : IComparable<DotnetVersion>, IEquatable<DotnetVersion>
{
    private DotnetVersion(int major, int minor, int patch, string? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public string? Prerelease { get; }

    public bool IsPrerelease => Prerelease is not null;

    /// <summary>Feature band do SDK (por exemplo, 4 para 10.0.402).</summary>
    public int FeatureBand => Patch / 100;

    public ChannelVersion Channel => new(Major, Minor);

    public static DotnetVersion Parse(string text) =>
        TryParse(text, out var version)
            ? version
            : throw new FormatException($"Versão inválida: '{text}'.");

    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out DotnetVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var dash = text.IndexOf('-', StringComparison.Ordinal);
        var core = dash < 0 ? text : text[..dash];
        string? prerelease = dash < 0 ? null : text[(dash + 1)..];
        if (prerelease is not null && !IsValidPrerelease(prerelease))
        {
            return false;
        }

        var parts = core.Split('.');
        if (parts.Length != 3
            || !TryParseNumber(parts[0], out var major)
            || !TryParseNumber(parts[1], out var minor)
            || !TryParseNumber(parts[2], out var patch))
        {
            return false;
        }

        version = new DotnetVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(DotnetVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result == 0)
        {
            result = Minor.CompareTo(other.Minor);
        }

        if (result == 0)
        {
            result = Patch.CompareTo(other.Patch);
        }

        return result != 0 ? result : ComparePrerelease(Prerelease, other.Prerelease);
    }

    public bool Equals(DotnetVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is DotnetVersion other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Major, Minor, Patch, Prerelease is null ? 0 : StringComparer.Ordinal.GetHashCode(Prerelease));

    public override string ToString() =>
        Prerelease is null
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-{Prerelease}");

    public static bool operator ==(DotnetVersion? left, DotnetVersion? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(DotnetVersion? left, DotnetVersion? right) => !(left == right);

    public static bool operator <(DotnetVersion? left, DotnetVersion? right) =>
        left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(DotnetVersion? left, DotnetVersion? right) =>
        left is null || left.CompareTo(right) <= 0;

    public static bool operator >(DotnetVersion? left, DotnetVersion? right) =>
        left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(DotnetVersion? left, DotnetVersion? right) =>
        left is null ? right is null : left.CompareTo(right) >= 0;

    internal static bool TryParseNumber(string text, out int value)
    {
        value = 0;
        if (text.Length == 0 || text.Length > 9 || !text.All(char.IsAsciiDigit) || (text.Length > 1 && text[0] == '0'))
        {
            return false;
        }

        value = int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        return true;
    }

    private static bool IsValidPrerelease(string prerelease) =>
        prerelease.Split('.').All(identifier =>
            identifier.Length > 0 && identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));

    private static int ComparePrerelease(string? left, string? right)
    {
        if (left is null || right is null)
        {
            // Versão estável é maior que qualquer prerelease da mesma base.
            return left is null ? (right is null ? 0 : 1) : -1;
        }

        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var i = 0; i < Math.Min(leftParts.Length, rightParts.Length); i++)
        {
            var leftIsNumber = leftParts[i].All(char.IsAsciiDigit);
            var rightIsNumber = rightParts[i].All(char.IsAsciiDigit);
            int result;
            if (leftIsNumber && rightIsNumber)
            {
                result = leftParts[i].Length != rightParts[i].Length
                    ? leftParts[i].Length.CompareTo(rightParts[i].Length)
                    : string.CompareOrdinal(leftParts[i], rightParts[i]);
            }
            else if (leftIsNumber != rightIsNumber)
            {
                result = leftIsNumber ? -1 : 1;
            }
            else
            {
                result = string.CompareOrdinal(leftParts[i], rightParts[i]);
            }

            if (result != 0)
            {
                return result;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }
}
