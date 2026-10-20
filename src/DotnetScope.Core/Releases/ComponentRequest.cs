using DotnetScope.Core.Versions;

namespace DotnetScope.Core.Releases;

/// <summary>Componente e versão solicitados, nas formas <c>sdk 10.0.400</c> ou compacta <c>SDK-10.0.400</c>.</summary>
public sealed record ComponentRequest(ComponentKind Component, VersionSpec Version)
{
    /// <summary>
    /// Interpreta a versão, aceitando a forma compacta <c>COMPONENTE-versão</c>. Se a forma compacta e
    /// <paramref name="component"/> forem informadas com valores diferentes, a solicitação é rejeitada.
    /// </summary>
    public static ComponentRequest Parse(ComponentKind? component, string versionText)
    {
        ArgumentNullException.ThrowIfNull(versionText);
        var text = versionText.Trim();
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0 && ComponentKindNames.TryParse(text[..dash], out var compactComponent))
        {
            if (component is not null && component != compactComponent)
            {
                throw new DotnetScopeException(
                    ExitCodes.Usage,
                    $"O componente '{component.Value.ToName()}' conflita com a forma compacta '{text}'.");
            }

            return Create(compactComponent, VersionSpec.Parse(text[(dash + 1)..]));
        }

        return Create(component ?? ComponentKind.Sdk, VersionSpec.Parse(text));
    }

    public static ComponentRequest Create(ComponentKind component, VersionSpec version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version is FeatureBandSpec && component != ComponentKind.Sdk)
        {
            throw new DotnetScopeException(
                ExitCodes.Usage,
                $"Feature band ({version}) só se aplica ao componente sdk.");
        }

        return new ComponentRequest(component, version);
    }

    public override string ToString() => $"{Component.ToName()} {Version}";
}
