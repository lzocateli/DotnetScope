using System.Diagnostics.CodeAnalysis;

namespace DotnetScope.Core.Releases;

public enum ComponentKind
{
    Sdk,
    Runtime,
    AspNetCore,
    WindowsDesktop,
}

public static class ComponentKindNames
{
    public static IReadOnlyList<string> All { get; } = ["sdk", "runtime", "aspnetcore", "windowsdesktop"];

    public static string ToName(this ComponentKind kind) => kind switch
    {
        ComponentKind.Sdk => "sdk",
        ComponentKind.Runtime => "runtime",
        ComponentKind.AspNetCore => "aspnetcore",
        ComponentKind.WindowsDesktop => "windowsdesktop",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static bool TryParse([NotNullWhen(true)] string? text, out ComponentKind kind)
    {
        switch (text?.Trim().ToUpperInvariant())
        {
            case "SDK":
                kind = ComponentKind.Sdk;
                return true;
            case "RUNTIME":
                kind = ComponentKind.Runtime;
                return true;
            case "ASPNETCORE":
                kind = ComponentKind.AspNetCore;
                return true;
            case "WINDOWSDESKTOP":
                kind = ComponentKind.WindowsDesktop;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    /// <summary>Prefixo do nome do arquivo oficial do componente, por exemplo <c>dotnet-sdk-</c>.</summary>
    internal static string ArchivePrefix(this ComponentKind kind) => kind switch
    {
        ComponentKind.Sdk => "dotnet-sdk-",
        ComponentKind.Runtime => "dotnet-runtime-",
        ComponentKind.AspNetCore => "aspnetcore-runtime-",
        ComponentKind.WindowsDesktop => "windowsdesktop-runtime-",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
