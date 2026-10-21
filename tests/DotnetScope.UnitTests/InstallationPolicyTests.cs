using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Versions;
using DotnetScope.Infrastructure;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.Platform;

namespace DotnetScope.UnitTests;

public sealed class InstallationPolicyTests
{
    [Theory]
    [InlineData("sdk/10.0.402/dotnet.dll", null)]
    [InlineData("./sdk/10.0.402/", "sdk/10.0.402")]
    [InlineData("././dotnet", "dotnet")]
    public void NormalizeName_AceitaCaminhosRelativos(string name, string? expected) =>
        Assert.Equal(expected ?? name, SafeTarExtractor.NormalizeName(name));

    [Theory]
    [InlineData(".")]
    [InlineData("./")]
    [InlineData("././")]
    public void NormalizeName_RaizDoArquivo_EhIgnorada(string name) =>
        Assert.Null(SafeTarExtractor.NormalizeName(name));

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("../x")]
    [InlineData("./sdk/../../x")]
    [InlineData("sdk//x")]
    [InlineData("sdk/./x")]
    [InlineData("sdk\\x")]
    [InlineData("sdk\0x")]
    public void NormalizeName_RecusaCaminhosPerigosos(string name)
    {
        var exception = Assert.Throws<DotnetScopeException>(() => SafeTarExtractor.NormalizeName(name));
        Assert.Equal(ExitCodes.Integrity, exception.ExitCode);
    }

    [Theory]
    [InlineData("/home/teste/.dotnet", "/home/teste/.dotnet")]
    [InlineData("/home/teste/ferramentas/dotnet/", "/home/teste/ferramentas/dotnet")]
    [InlineData("/home/teste/a/../dotnet", "/home/teste/dotnet")]
    [InlineData("/tmp/dotnet", "/tmp/dotnet")]
    [InlineData("/data/dotnet", "/data/dotnet")]
    [InlineData("/var/folders/ab/T/dotnet", "/var/folders/ab/T/dotnet")]
    public void ValidateUserRoot_AceitaDiretoriosDoUsuario(string path, string expected)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Regras Unix.");
        Assert.Equal(expected, InstallLocations.ValidateUserRoot(path, "/home/teste"));
    }

    [Theory]
    [InlineData("/root", "/root/.dotnet")]
    [InlineData("/var/lib/jenkins", "/var/lib/jenkins/.dotnet")]
    [InlineData("/opt/app-user", "/opt/app-user/dotnet")]
    public void ValidateUserRoot_HomeEmLocalDoSistema_PermiteSubdiretorios(string home, string path)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Regras Unix.");
        Assert.Equal(path, InstallLocations.ValidateUserRoot(path, home));
    }

    [Fact]
    public void ValidateUserRoot_HomeNaRaiz_NaoLiberaDiretoriosDoSistema()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Regras Unix.");
        var exception = Assert.Throws<DotnetScopeException>(() => InstallLocations.ValidateUserRoot("/usr/dotnet", "/"));
        Assert.Equal(ExitCodes.Privilege, exception.ExitCode);
    }

    [Theory]
    [InlineData("/", ExitCodes.Privilege)]
    [InlineData("/home/teste", ExitCodes.Privilege)]
    [InlineData("/home/teste/", ExitCodes.Privilege)]
    [InlineData("/usr/share/dotnet", ExitCodes.Privilege)]
    [InlineData("/usr/local/dotnet", ExitCodes.Privilege)]
    [InlineData("/etc/dotnet", ExitCodes.Privilege)]
    [InlineData("/opt/dotnet", ExitCodes.Privilege)]
    [InlineData("/root/.dotnet", ExitCodes.Privilege)]
    [InlineData("/home/outro/.dotnet", ExitCodes.Privilege)]
    [InlineData("/home/teste/../outro/.dotnet", ExitCodes.Privilege)]
    [InlineData("relativo/dotnet", ExitCodes.Usage)]
    public void ValidateUserRoot_RecusaLocaisInadequados(string path, int expected)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Regras Unix.");
        var exception = Assert.Throws<DotnetScopeException>(() => InstallLocations.ValidateUserRoot(path, "/home/teste"));
        Assert.Equal(expected, exception.ExitCode);
    }

    [Fact]
    public void ValidateUserRoot_LinkParaDiretorioDoSistema_Recusa()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Regras Unix.");
        var home = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        try
        {
            var link = Path.Combine(home, "dotnet");
            Directory.CreateSymbolicLink(link, "/usr/lib");

            var exception = Assert.Throws<DotnetScopeException>(() => InstallLocations.ValidateUserRoot(Path.Combine(link, "x"), home));

            Assert.Equal(ExitCodes.Privilege, exception.ExitCode);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Theory]
    [InlineData(ComponentKind.Sdk, "10.0.402", "sdk/10.0.402")]
    [InlineData(ComponentKind.Runtime, "10.0.13", "shared/Microsoft.NETCore.App/10.0.13")]
    [InlineData(ComponentKind.AspNetCore, "10.0.13", "shared/Microsoft.AspNetCore.App/10.0.13")]
    [InlineData(ComponentKind.WindowsDesktop, "10.0.13", "shared/Microsoft.WindowsDesktop.App/10.0.13")]
    public void PrimaryPath_SegueLayoutOficial(ComponentKind component, string version, string expected) =>
        Assert.Equal(expected, DotnetLayout.PrimaryPath(component, DotnetVersion.Parse(version)));

    [Theory]
    [InlineData("sdk", 2)]
    [InlineData("templates", 2)]
    [InlineData("shared", 3)]
    [InlineData("host", 3)]
    [InlineData("packs", 3)]
    [InlineData("sdk-manifests", 4)]
    [InlineData("dotnet", 1)]
    [InlineData("desconhecido", 1)]
    public void LeafSegments_DefineUnidadeDeInstalacao(string topLevel, int expected) =>
        Assert.Equal(expected, DotnetLayout.LeafSegments(topLevel));

    [Fact]
    public void FindMissing_ReportaBibliotecasECertificadosAusentes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "libicuuc.so.74"), string.Empty);
            File.WriteAllText(Path.Combine(directory, "libssl.so.3"), string.Empty);
            File.WriteAllText(Path.Combine(directory, "libstdc++.so.6"), string.Empty);

            var missing = NativeDependencyChecker.FindMissing([directory], hasCertificates: false);

            Assert.Equal(["libgcc_s", "certificados de CA (ca-certificates)"], missing);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetDataDirectory_UsaXdgDataHomeOuLocalShare()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Regras Unix.");
        Assert.Equal("/xdg/dotnetscope", DotnetScopePaths.GetDataDirectory(n => n == "XDG_DATA_HOME" ? "/xdg" : null, false, "/home/t"));
        Assert.Equal("/home/t/.local/share/dotnetscope", DotnetScopePaths.GetDataDirectory(_ => null, false, "/home/t"));
    }
}
