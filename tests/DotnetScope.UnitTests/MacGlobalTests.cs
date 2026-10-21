using System.Text.Json;
using DotnetScope.Cli;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Core.Resolution;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.Platform;
using DotnetScope.Infrastructure.SystemScope;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.UnitTests;

public sealed class MacGlobalTests : IDisposable
{
    private const string Signed = """
        Package "dotnet-runtime-9.0.4-osx-arm64.pkg":
           Status: signed by a developer certificate issued by Apple for distribution
           Notarization: trusted by the Apple notary service
           Signed with a trusted timestamp on: 2025-04-08 10:00:00 +0000
           Certificate Chain:
            1. Developer ID Installer: Microsoft Corporation (UBF8T346G9)
               Expires: 2027-02-01 22:12:15 +0000
            2. Developer ID Certification Authority
            3. Apple Root CA
        """;

    private readonly string _base = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_base))
        {
            Directory.Delete(_base, recursive: true);
        }
    }

    [Fact]
    public void PkgSignature_InstaladorDaMicrosoft_EhAceito() => Assert.True(PkgSignature.IsMicrosoft(0, Signed));

    [Theory]
    [InlineData("1. Developer ID Installer: Contoso (ABCDE12345)")]
    [InlineData("1. Developer ID Installer: Microsoft Corporation (ABCDE12345)")]
    [InlineData("1. Developer ID Installer: Microsoft Corporation Fans (UBF8T346G9)")]
    [InlineData("1. Developer ID Application: Microsoft Corporation (UBF8T346G9)")]
    public void PkgSignature_OutroAssinante_EhRecusado(string firstCertificate) =>
        Assert.False(PkgSignature.IsMicrosoft(0, Signed.Replace(PkgSignature.MicrosoftInstaller, firstCertificate, StringComparison.Ordinal)));

    [Fact]
    public void PkgSignature_SemDeveloperIdOuComFalha_EhRecusado()
    {
        Assert.False(PkgSignature.IsMicrosoft(0, Signed.Replace("issued by Apple for distribution", "issued by a third party", StringComparison.Ordinal)));
        Assert.False(PkgSignature.IsMicrosoft(1, Signed));
        Assert.False(PkgSignature.IsMicrosoft(0, "Package \"x.pkg\":\n   Status: no signature\n"));
    }

    [Fact]
    public async Task Resolver_ComInstaller_EscolheOPkg()
    {
        var resolver = new VersionResolver(new FixtureMetadataSource());

        var artifact = await resolver.ResolveAsync(
            ComponentRequest.Parse(ComponentKind.Runtime, "10.0"), new ResolveOptions("osx-arm64", false, Installer: true), TestContext.Current.CancellationToken);

        Assert.EndsWith("-osx-arm64.pkg", artifact.File.Url.AbsolutePath, StringComparison.Ordinal);
        Assert.NotEmpty(artifact.Sha512);
    }

    [Fact]
    public async Task Install_Global_SemRoot_RetornaPrivilegio()
    {
        var result = await RunAsync(["install", "runtime", "10.0", "--scope", "global", "--yes"], elevated: false);

        Assert.Equal(ExitCodes.Privilege, result.ExitCode);
        Assert.Contains("sudo", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, result.Installer.Calls);
    }

    [Theory]
    [InlineData("--source", "archive")]
    [InlineData("--source", "apt")]
    [InlineData("--install-dir", "/opt/dotnet")]
    public async Task Install_Global_OpcoesQueNaoSeAplicamAoPkg_SaoErroDeUso(string option, string value)
    {
        var result = await RunAsync(["install", "runtime", "10.0", "--scope", "global", option, value]);

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
    }

    [Fact]
    public async Task Install_Global_UsaPkgOficialSemConfigurarAmbiente()
    {
        var result = await RunAsync(["install", "runtime", "10.0", "--scope", "global", "--yes", "--output", "json"]);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Equal(result.Layout.Root, result.Installer.Root);
        using var json = JsonDocument.Parse(result.Output);
        Assert.EndsWith(".pkg", json.RootElement.GetProperty("artifact").GetProperty("fileName").GetString(), StringComparison.Ordinal);
        Assert.Equal("package", json.RootElement.GetProperty("environment").GetProperty("status").GetString());
        Assert.Equal([result.Layout.StateDirectory], result.Protected);
    }

    [Fact]
    public async Task Install_Global_RaizComAncestralDeOutroUsuario_SomenteAvisa()
    {
        var result = await RunAsync(
            ["install", "runtime", "10.0", "--scope", "global", "--yes"],
            stat: path => new UnixFileStatus(path.EndsWith("/share", StringComparison.Ordinal) ? 501 : 0, Convert.ToInt32("755", 8)));

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("UID 501", result.Error, StringComparison.Ordinal);
        Assert.Contains("grava nesse local mesmo assim", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uninstall_Global_ComRegistro_OrientaRemocaoManual()
    {
        var result = await RunAsync(
            ["uninstall", "runtime", "10.0.13", "--scope", "global"],
            prepare: layout => new InstallationManifestStore(layout.ManifestFile).AddAsync(
                new InstallationRecord(
                    ComponentKind.Runtime, "10.0.13", "osx-arm64", InstallScope.Global, layout.Root, "dotnet-runtime-osx-arm64.pkg",
                    new Uri("https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.13/dotnet-runtime-10.0.13-osx-arm64.pkg"),
                    new string('a', 128), DateTimeOffset.UtcNow, [], [], false),
                new NullDiagnostics(),
                CancellationToken.None).GetAwaiter().GetResult());

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Contains("dotnet-core-uninstall", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uninstall_Global_SemRegistro_RetornaNaoEncontrado()
    {
        var result = await RunAsync(["uninstall", "runtime", "10.0.13", "--scope", "global"]);

        Assert.Equal(ExitCodes.NotFound, result.ExitCode);
    }

    private async Task<(int ExitCode, string Output, string Error, InstallCommandTests.FakeInstaller Installer, InstallerGlobalLayout Layout, List<string> Protected)> RunAsync(
        string[] args,
        bool elevated = true,
        Func<string, UnixFileStatus?>? stat = null,
        Action<InstallerGlobalLayout>? prepare = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var installer = new InstallCommandTests.FakeInstaller();
        var layout = new InstallerGlobalLayout(Path.Combine(_base, "usr", "local", "share", "dotnet"), Path.Combine(_base, "var", "db", "dotnetscope"));
        prepare?.Invoke(layout);
        var protectedPaths = new List<string>();
        var services = new CliServices(output, error, (_, _) => new FixtureMetadataSource(), () => "osx-arm64")
        {
            CreateInstaller = (_, _) => installer,
            CreateOfficialInstaller = (_, _, _) => installer,
            IsElevated = () => elevated,
            IsRoot = () => elevated,
            HomeDirectory = () => Path.Combine(_base, "home"),
            FindMissingDependencies = () => [],
            CreateInstallerLayout = _ => layout,
            EnsureProtectedDirectory = protectedPaths.Add,
            StatPath = stat ?? (_ => new UnixFileStatus(0, Convert.ToInt32("755", 8))),
            GetEnvironmentVariable = _ => null,
            UnsetEnvironmentVariable = _ => { },
            ProbeLoginShell = (_, _, _) => Task.FromResult<LoginShellResult?>(null),
        };

        var exitCode = await CliApplication.RunAsync(args, services, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString(), installer, layout, protectedPaths);
    }

    private sealed class NullDiagnostics : IDiagnosticSink
    {
        public void Warning(string message)
        {
        }

        public void Verbose(string message)
        {
        }
    }
}
