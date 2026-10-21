using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;

namespace DotnetScope.Core.Installation;

/// <summary>
/// Layout de uma raiz .NET. Cada diretório de primeiro nível tem uma profundidade de "folha": o item naquela
/// profundidade pertence a uma versão específica e é a unidade indivisível de instalação, compartilhamento e remoção.
/// </summary>
public static class DotnetLayout
{
    /// <summary>Quantidade de segmentos do caminho relativo que formam uma folha sob o diretório informado.</summary>
    public static int LeafSegments(string topLevel) => topLevel switch
    {
        "sdk" or "templates" => 2,
        "shared" or "host" or "packs" or "metadata" => 3,
        "sdk-manifests" => 4,
        _ => 1,
    };

    /// <summary>Caminho relativo que prova a presença do componente na raiz.</summary>
    public static string PrimaryPath(ComponentKind component, DotnetVersion version) => component switch
    {
        ComponentKind.Sdk => $"sdk/{version}",
        ComponentKind.Runtime => $"shared/Microsoft.NETCore.App/{version}",
        ComponentKind.AspNetCore => $"shared/Microsoft.AspNetCore.App/{version}",
        ComponentKind.WindowsDesktop => $"shared/Microsoft.WindowsDesktop.App/{version}",
        _ => throw new ArgumentOutOfRangeException(nameof(component), component, null),
    };

    public static string HostExecutable(bool windows) => windows ? "dotnet.exe" : "dotnet";
}
