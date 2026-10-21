using System.Diagnostics.CodeAnalysis;
using DotnetScope.Core.Versions;

namespace DotnetScope.Core.Resolution;

/// <summary>Política <c>sdk.rollForward</c> do global.json.</summary>
public enum RollForwardPolicy
{
    Patch,
    Feature,
    Minor,
    Major,
    LatestPatch,
    LatestFeature,
    LatestMinor,
    LatestMajor,
    Disable,
}

/// <summary>Seção <c>sdk</c> de um global.json.</summary>
/// <param name="Version">Versão mínima pedida; nula quando o arquivo não define <c>sdk.version</c>.</param>
public sealed record GlobalJsonSdk(string Path, DotnetVersion? Version, RollForwardPolicy RollForward, bool AllowPrerelease);

public static class RollForwardPolicies
{
    public static bool TryParse([NotNullWhen(true)] string? value, out RollForwardPolicy policy)
    {
        policy = value switch
        {
            "patch" => RollForwardPolicy.Patch,
            "feature" => RollForwardPolicy.Feature,
            "minor" => RollForwardPolicy.Minor,
            "major" => RollForwardPolicy.Major,
            "latestPatch" => RollForwardPolicy.LatestPatch,
            "latestFeature" => RollForwardPolicy.LatestFeature,
            "latestMinor" => RollForwardPolicy.LatestMinor,
            "latestMajor" => RollForwardPolicy.LatestMajor,
            "disable" => RollForwardPolicy.Disable,
            _ => (RollForwardPolicy)(-1),
        };
        return Enum.IsDefined(policy);
    }

    public static string ToName(RollForwardPolicy policy) => policy switch
    {
        RollForwardPolicy.Patch => "patch",
        RollForwardPolicy.Feature => "feature",
        RollForwardPolicy.Minor => "minor",
        RollForwardPolicy.Major => "major",
        RollForwardPolicy.LatestPatch => "latestPatch",
        RollForwardPolicy.LatestFeature => "latestFeature",
        RollForwardPolicy.LatestMinor => "latestMinor",
        RollForwardPolicy.LatestMajor => "latestMajor",
        _ => "disable",
    };
}
