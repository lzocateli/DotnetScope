using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;
using DotnetScope.Infrastructure.Installation;

namespace DotnetScope.UnitTests;

public sealed class UninstallPlanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));

    public UninstallPlanTests()
    {
        foreach (var leaf in new[] { "sdk/10.0.402", "sdk/10.0.113", "shared/Microsoft.NETCore.App/10.0.13", "host/fxr/10.0.13", "sdk/9.0.100" })
        {
            Directory.CreateDirectory(Path.Combine(_root, leaf));
        }

        File.WriteAllText(Path.Combine(_root, "dotnet"), "host");
        File.WriteAllText(Path.Combine(_root, "LICENSE.txt"), "licença externa");
    }

    [Fact]
    public void Plan_FolhasCompartilhadas_SaoMantidasETransferidas()
    {
        var first = Record("10.0.402", paths: ["dotnet", "LICENSE.txt", "host/fxr/10.0.13", "shared/Microsoft.NETCore.App/10.0.13", "sdk/10.0.402"],
            added: ["dotnet", "host/fxr/10.0.13", "shared/Microsoft.NETCore.App/10.0.13", "sdk/10.0.402"]);
        var second = Record("10.0.113", paths: ["dotnet", "LICENSE.txt", "host/fxr/10.0.13", "shared/Microsoft.NETCore.App/10.0.13", "sdk/10.0.113"],
            added: ["sdk/10.0.113"]);

        var plan = ArchiveUninstaller.Plan([first, second], _root, ComponentKind.Sdk, DotnetVersion.Parse("10.0.402"), force: false);

        Assert.Equal(["sdk/10.0.402"], plan.Remove);
        Assert.Equal(["dotnet", "host/fxr/10.0.13", "shared/Microsoft.NETCore.App/10.0.13"], plan.Keep.Select(k => k.Path));
        Assert.All(plan.Keep, k => Assert.Equal("sdk 10.0.113", k.UsedBy));
    }

    [Fact]
    public void Plan_UltimaInstalacao_RemoveTudoQueCriouMasNaoArquivosPreexistentes()
    {
        var only = Record("10.0.402", paths: ["dotnet", "LICENSE.txt", "host/fxr/10.0.13", "sdk/10.0.402"], added: ["dotnet", "host/fxr/10.0.13", "sdk/10.0.402"]);

        var plan = ArchiveUninstaller.Plan([only], _root, ComponentKind.Sdk, DotnetVersion.Parse("10.0.402"), force: false);

        Assert.Equal(["dotnet", "host/fxr/10.0.13", "sdk/10.0.402"], plan.Remove);
        Assert.DoesNotContain("LICENSE.txt", plan.Remove);
    }

    [Fact]
    public void Plan_ComponenteExterno_ExigeForce()
    {
        var exception = Assert.Throws<DotnetScopeException>(() =>
            ArchiveUninstaller.Plan([], _root, ComponentKind.Sdk, DotnetVersion.Parse("9.0.100"), force: false));
        var forced = ArchiveUninstaller.Plan([], _root, ComponentKind.Sdk, DotnetVersion.Parse("9.0.100"), force: true);

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
        Assert.True(forced.External);
        Assert.Equal(["sdk/9.0.100"], forced.Remove);
    }

    [Fact]
    public void Plan_ForceEmPastaDeInstalacaoGerenciada_Recusa()
    {
        var sdk = Record("10.0.402", ["shared/Microsoft.NETCore.App/10.0.13", "sdk/10.0.402"], ["shared/Microsoft.NETCore.App/10.0.13", "sdk/10.0.402"]);

        var exception = Assert.Throws<DotnetScopeException>(() =>
            ArchiveUninstaller.Plan([sdk], _root, ComponentKind.Runtime, DotnetVersion.Parse("10.0.13"), force: true));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
        Assert.Contains("sdk 10.0.402", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sdk/10.0.402", true)]
    [InlineData("dotnet", true)]
    [InlineData("shared/Microsoft.NETCore.App/10.0.13", true)]
    [InlineData("sdk-manifests/10.0.100/microsoft.net.sdk.android/36.1.2", true)]
    [InlineData("shared/x.txt", true)]
    [InlineData("../projetos", false)]
    [InlineData("/home/u/projetos", false)]
    [InlineData("sdk/../../x", false)]
    [InlineData("sdk/10.0.402/extra", false)]
    [InlineData("sdk//x", false)]
    [InlineData("sdk\\x", false)]
    [InlineData("", false)]
    public void IsValidLeaf_ConfinaCaminhosDoManifestoEJournal(string leaf, bool expected) =>
        Assert.Equal(expected, DotnetLayout.IsValidLeaf(leaf));

    [Fact]
    public void Plan_NaoInstalado_NaoEncontrado()
    {
        var exception = Assert.Throws<DotnetScopeException>(() =>
            ArchiveUninstaller.Plan([], _root, ComponentKind.Runtime, DotnetVersion.Parse("8.0.1"), force: true));

        Assert.Equal(ExitCodes.NotFound, exception.ExitCode);
    }

    [Fact]
    public void Plan_RegistroDeOutraRaiz_NaoInterfere()
    {
        var other = Record("10.0.402", paths: ["sdk/10.0.402"], added: ["sdk/10.0.402"]) with { Root = "/outra/raiz" };

        var exception = Assert.Throws<DotnetScopeException>(() =>
            ArchiveUninstaller.Plan([other], _root, ComponentKind.Sdk, DotnetVersion.Parse("10.0.402"), force: false));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
    }

    [Fact]
    public void Inventory_ClassificaOrigens()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Cenário Linux simulado (home e host Unix).");
        var system = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(system, "shared/Microsoft.NETCore.App/8.0.10"));
        File.WriteAllText(Path.Combine(system, "dotnet"), "host");
        try
        {
            var records = new[]
            {
                Record("10.0.402", ["shared/Microsoft.NETCore.App/10.0.13", "sdk/10.0.402"], ["shared/Microsoft.NETCore.App/10.0.13", "sdk/10.0.402"]),
                Record("10.0.300", ["sdk/10.0.300"], ["sdk/10.0.300"]),
            };

            var items = InstalledInventory.Scan([_root, system], records, home: "/home/ninguem", isPackageManaged: root => root == system);

            Assert.Equal("dotnetscope", Origin(items, ComponentKind.Sdk, "10.0.402"));
            Assert.Equal("dotnetscope", Origin(items, ComponentKind.Runtime, "10.0.13"));
            Assert.Equal("sdk 10.0.402", items.Single(i => i.Version.ToString() == "10.0.13").ManagedBy);
            Assert.Equal("external", Origin(items, ComponentKind.Sdk, "9.0.100"));
            Assert.Equal("package", Origin(items, ComponentKind.Runtime, "8.0.10"));
            Assert.False(items.Single(i => i.Version.ToString() == "10.0.300").Present);
        }
        finally
        {
            Directory.Delete(system, recursive: true);
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Origin(IReadOnlyList<InstalledComponent> items, ComponentKind component, string version) =>
        items.Single(i => i.Component == component && i.Version.ToString() == version).Origin switch
        {
            InstallOrigin.DotnetScope => "dotnetscope",
            InstallOrigin.Package => "package",
            _ => "external",
        };

    private InstallationRecord Record(string version, IReadOnlyList<string> paths, IReadOnlyList<string> added) =>
        new(ComponentKind.Sdk, version, "linux-x64", InstallScope.User, _root, "dotnet-sdk-linux-x64.tar.gz",
            new Uri("https://builds.dotnet.microsoft.com/x.tar.gz"), new string('a', 128), DateTimeOffset.UnixEpoch, paths, added, false);
}
