using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using DotnetScope.Core;
using DotnetScope.Infrastructure.SystemScope;

namespace DotnetScope.IntegrationTests;

/// <summary>ACL do estado global e Authenticode em um Windows real (runner efêmero ou estação elevada).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGlobalTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void DiretorioProtegido_SomenteAdministradoresESystem()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && WindowsSecurity.IsElevated(), "Somente Windows elevado.");
        var path = Path.Combine(_directory, "estado");
        Directory.CreateDirectory(_directory);

        WindowsSecurity.EnsureProtectedDirectory(path);

        var acl = new DirectoryInfo(path).GetAccessControl();
        Assert.True(acl.AreAccessRulesProtected);
        var identities = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Select(r => ((SecurityIdentifier)r.IdentityReference).Value).Distinct().Order().ToList();
        Assert.Equal(["S-1-5-18", "S-1-5-32-544"], identities);
        WindowsSecurity.EnsureProtectedDirectory(path);
    }

    [Fact]
    public void DiretorioComEscritaParaUsuarios_EhRecusado()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && WindowsSecurity.IsElevated(), "Somente Windows elevado.");
        var path = Path.Combine(_directory, "pre-criado");
        var info = Directory.CreateDirectory(path);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Modify, AccessControlType.Allow));
        info.SetAccessControl(acl);

        var exception = Assert.Throws<DotnetScopeException>(() => WindowsSecurity.EnsureProtectedDirectory(path));

        Assert.Equal(ExitCodes.Privilege, exception.ExitCode);
    }

    [Fact]
    public void Authenticode_DotnetExeDaMicrosoft_EhAceito()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Somente Windows.");
        var dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        Assert.SkipUnless(File.Exists(dotnet), "dotnet.exe da máquina indisponível.");

        using var handle = File.OpenHandle(dotnet, FileMode.Open, FileAccess.Read, FileShare.Read);
        Authenticode.VerifyMicrosoftSignature(handle, dotnet);
    }

    [Fact]
    public void Authenticode_ArquivoAdulteradoOuSemAssinatura_EhRecusado()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Somente Windows.");
        var dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        Assert.SkipUnless(File.Exists(dotnet), "dotnet.exe da máquina indisponível.");
        Directory.CreateDirectory(_directory);
        var tampered = Path.Combine(_directory, "adulterado.exe");
        var bytes = File.ReadAllBytes(dotnet);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(tampered, bytes);
        var unsigned = Path.Combine(_directory, "sem-assinatura.exe");
        File.WriteAllBytes(unsigned, [0x4D, 0x5A, 0x90, 0x00]);

        foreach (var path in new[] { tampered, unsigned })
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var exception = Assert.Throws<DotnetScopeException>(() => Authenticode.VerifyMicrosoftSignature(handle, path));
            Assert.Equal(ExitCodes.Integrity, exception.ExitCode);
        }
    }

    [Fact]
    public void HandleAbertoSemCompartilharEscrita_ImpedeTrocaDoArquivo()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Somente Windows.");
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "instalador.exe");
        File.WriteAllText(path, "original");

        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.ThrowsAny<IOException>(() => File.WriteAllText(path, "trocado"));
        Assert.ThrowsAny<IOException>(() => File.Move(path, path + ".old"));
    }
}
