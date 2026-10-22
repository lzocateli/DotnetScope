using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Infrastructure.Installation;

namespace DotnetScope.UnitTests;

public sealed class InstallationManifestStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
    private string ManifestPath => Path.Combine(_directory, "manifest.json");

    public InstallationManifestStoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ResolveDeclaredInvoker_AppliesScopePlatformAndInputPolicy()
    {
        Assert.Equal("alice", InstallationManifestStore.ResolveDeclaredInvoker(InstallScope.Global, isUnix: true, value: "  alice  "));
        Assert.Null(InstallationManifestStore.ResolveDeclaredInvoker(InstallScope.User, isUnix: true, value: "alice"));
        Assert.Null(InstallationManifestStore.ResolveDeclaredInvoker(InstallScope.Global, isUnix: false, value: "alice"));
        Assert.Null(InstallationManifestStore.ResolveDeclaredInvoker(InstallScope.Global, isUnix: true, value: null));
        Assert.Null(InstallationManifestStore.ResolveDeclaredInvoker(InstallScope.Global, isUnix: true, value: "alice\nsecret"));
        Assert.Null(InstallationManifestStore.ResolveDeclaredInvoker(InstallScope.Global, isUnix: true, value: new string('a', 257)));
    }

    [Fact]
    public async Task AddAsync_PersistsDeclaredInvokerForGlobalRecord()
    {
        var record = CreateRecord(InstallScope.Global) with { DeclaredInvoker = "sudo-user" };

        await new InstallationManifestStore(ManifestPath).AddAsync(
            record,
            NullDiagnosticSink.Instance,
            CancellationToken.None);

        var stored = Assert.Single(new InstallationManifestStore(ManifestPath).Read());
        Assert.Equal("sudo-user", stored.DeclaredInvoker);
        Assert.Contains("\"declaredInvoker\": \"sudo-user\"", File.ReadAllText(ManifestPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Read_LegacyManifestWithoutDeclaredInvoker_RemainsCompatible()
    {
        File.WriteAllText(
            ManifestPath,
            """
            {"schemaVersion":1,"installations":[{"component":"sdk","version":"10.0.402","rid":"linux-x64","scope":"global","root":"/usr/local/share/dotnet","fileName":"sdk.tar.gz","url":"https://example.test/sdk.tar.gz","sha512":"","installedAt":"2026-10-22T00:00:00+00:00","paths":[],"addedPaths":[],"hostReplaced":false}]}
            """);

        var stored = Assert.Single(new InstallationManifestStore(ManifestPath).Read());

        Assert.Null(stored.DeclaredInvoker);
        Assert.Equal(InstallScope.Global, stored.Scope);
    }

    [Fact]
    public void Read_InvalidDeclaredInvoker_FailsWithoutRevealingItsValue()
    {
        File.WriteAllText(
            ManifestPath,
            """
            {"schemaVersion":1,"installations":[{"component":"sdk","version":"10.0.402","rid":"linux-x64","scope":"global","root":"/usr/local/share/dotnet","fileName":"sdk.tar.gz","url":"https://example.test/sdk.tar.gz","sha512":"","installedAt":"2026-10-22T00:00:00+00:00","paths":[],"addedPaths":[],"hostReplaced":false,"declaredInvoker":"alice\nsecret"}]}
            """);

        var exception = Assert.Throws<DotnetScopeException>(() => new InstallationManifestStore(ManifestPath).Read());

        Assert.DoesNotContain("alice", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static InstallationRecord CreateRecord(InstallScope scope) => new(
        ComponentKind.Sdk,
        "10.0.402",
        "linux-x64",
        scope,
        "/usr/local/share/dotnet",
        "sdk.tar.gz",
        new Uri("https://example.test/sdk.tar.gz"),
        string.Empty,
        DateTimeOffset.Parse("2026-10-22T00:00:00+00:00"),
        [],
        [],
        false);
}
