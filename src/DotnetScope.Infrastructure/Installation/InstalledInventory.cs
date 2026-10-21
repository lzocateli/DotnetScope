using System.Diagnostics;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;

namespace DotnetScope.Infrastructure.Installation;

public enum InstallOrigin
{
    DotnetScope,
    External,
    Package,
}

/// <summary>Componente encontrado em uma raiz .NET.</summary>
/// <param name="ManagedBy">Instalação do DotnetScope que trouxe o componente, quando ele não tem registro próprio.</param>
/// <param name="Present">Falso para registros do manifesto cujos arquivos não existem mais.</param>
public sealed record InstalledComponent(
    ComponentKind Component,
    DotnetVersion Version,
    string Root,
    InstallOrigin Origin,
    string? ManagedBy,
    bool Present);

/// <summary>Inventário das instalações .NET do usuário e do sistema, com a origem de cada componente.</summary>
public static class InstalledInventory
{
    private static readonly (ComponentKind Component, string Directory)[] Layout =
    [
        (ComponentKind.Sdk, "sdk"),
        (ComponentKind.Runtime, "shared/Microsoft.NETCore.App"),
        (ComponentKind.AspNetCore, "shared/Microsoft.AspNetCore.App"),
        (ComponentKind.WindowsDesktop, "shared/Microsoft.WindowsDesktop.App"),
    ];

    /// <summary>Raízes do sistema verificadas além da raiz do usuário e das registradas no manifesto.</summary>
    public static IReadOnlyList<string> SystemRoots() =>
        OperatingSystem.IsWindows()
            ? []
            : ["/usr/lib/dotnet", "/usr/share/dotnet", "/usr/local/share/dotnet", "/usr/lib64/dotnet", "/opt/dotnet"];

    public static IReadOnlyList<InstalledComponent> Scan(
        IEnumerable<string> roots,
        IReadOnlyList<InstallationRecord> records,
        string home,
        Func<string, bool> isPackageManaged)
    {
        ArgumentNullException.ThrowIfNull(records);
        var result = new List<InstalledComponent>();
        foreach (var root in roots.Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r))).Distinct(StringComparer.Ordinal))
        {
            var rootRecords = records.Where(r => string.Equals(r.Root, root, StringComparison.Ordinal)).ToList();
            var external = IsUnder(root, home) || !File.Exists(Path.Combine(root, "dotnet")) || !isPackageManaged(root)
                ? InstallOrigin.External
                : InstallOrigin.Package;
            foreach (var (component, directory) in Layout)
            {
                var path = Path.Combine(root, directory);
                if (!Directory.Exists(path))
                {
                    continue;
                }

                foreach (var entry in Directory.EnumerateDirectories(path))
                {
                    if (!DotnetVersion.TryParse(Path.GetFileName(entry), out var version))
                    {
                        continue;
                    }

                    var primary = DotnetLayout.PrimaryPath(component, version);
                    var own = rootRecords.FirstOrDefault(r => r.Component == component && r.Version == version.ToString());
                    var carrier = own is null
                        ? rootRecords.FirstOrDefault(r => r.AddedPaths.Contains(primary, StringComparer.Ordinal))
                        : null;
                    var origin = own is not null || carrier is not null ? InstallOrigin.DotnetScope : external;
                    result.Add(new InstalledComponent(
                        component,
                        version,
                        root,
                        origin,
                        carrier is null ? null : $"{carrier.Component.ToName()} {carrier.Version}",
                        Present: true));
                }
            }

            foreach (var record in rootRecords)
            {
                if (DotnetVersion.TryParse(record.Version, out var version)
                    && !result.Any(c => c.Root == root && c.Component == record.Component && c.Version == version))
                {
                    result.Add(new InstalledComponent(record.Component, version, root, InstallOrigin.DotnetScope, null, Present: false));
                }
            }
        }

        return result
            .OrderBy(c => c.Root, StringComparer.Ordinal)
            .ThenBy(c => c.Component)
            .ThenByDescending(c => c.Version)
            .ToList();
    }

    /// <summary>Consulta dpkg ou rpm para saber se o host da raiz pertence a um pacote do sistema.</summary>
    public static bool IsPackageManaged(string root)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        var host = Path.Combine(root, "dotnet");
        foreach (var (tool, argument) in new[] { ("/usr/bin/dpkg-query", "-S"), ("/usr/bin/rpm", "-qf") })
        {
            if (!File.Exists(tool))
            {
                continue;
            }

            var startInfo = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            startInfo.ArgumentList.Add(argument);
            startInfo.ArgumentList.Add(host);
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                continue;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (process.WaitForExit(TimeSpan.FromSeconds(10)) && process.ExitCode == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnder(string path, string home)
    {
        var prefix = home.TrimEnd('/') + "/";
        return path.StartsWith(prefix, StringComparison.Ordinal);
    }
}
