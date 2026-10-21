using System.Runtime.Versioning;
using System.Text.Json;
using DotnetScope.Cli;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Infrastructure.Http;
using DotnetScope.Infrastructure.Installation;

namespace DotnetScope.IntegrationTests;

/// <summary>
/// Fluxo completo de install em processo: HTTP local real, filesystem real e host sintético executável em shell.
/// O fluxo do Windows (zip e registro) é coberto em fase própria.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class InstallFlowTests : IDisposable
{
    private readonly LocalMetadataServer _server = new();
    private readonly SyntheticMetadataSource _source = new();
    private readonly string _home = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
    private readonly FakePrompt _prompt = new();
    private IReadOnlyList<string> _missing = [];
    private Action<ArchiveInstaller>? _configure;

    public InstallFlowTests()
    {
        Directory.CreateDirectory(_home);
        Publish("10.0.13", ComponentKind.Sdk, "10.0.402", SyntheticArchives.Sdk("10.0.402", "10.0.13"));
        Publish("10.0.12", ComponentKind.Sdk, "10.0.401", SyntheticArchives.Sdk("10.0.401", "10.0.12"));
        Publish("10.0.13", ComponentKind.Runtime, "10.0.13", SyntheticArchives.Runtime("10.0.13"));
    }

    private string Root => Path.Combine(_home, ".dotnet");

    private string ManifestPath => Path.Combine(_home, "data", "manifest.json");

    [Fact]
    public async Task Install_SdkExato_InstalaValidaERegistra()
    {
        var result = await RunAsync("install", "sdk", "10.0.402", "--output", "json");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("installed", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("validation").GetProperty("succeeded").GetBoolean());
        Assert.Equal(Root, json.RootElement.GetProperty("installDir").GetString());
        Assert.True(File.Exists(Path.Combine(Root, "sdk/10.0.402/dotnet.dll")));
        Assert.True(File.GetUnixFileMode(Path.Combine(Root, "dotnet")).HasFlag(UnixFileMode.UserExecute));
        Assert.Empty(Directory.EnumerateDirectories(Root, ArchiveInstaller.StagingPrefix + "*"));

        var record = Assert.Single(new InstallationManifestStore(ManifestPath).Read());
        Assert.Equal(ComponentKind.Sdk, record.Component);
        Assert.Equal("10.0.402", record.Version);
        Assert.Contains("sdk/10.0.402", record.AddedPaths);
        Assert.Contains("shared/Microsoft.NETCore.App/10.0.13", record.Paths);
        Assert.Contains("sdk-manifests/10.0.100/microsoft.net.sdk.android/36.1.2", record.AddedPaths);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(ManifestPath));
    }

    [Fact]
    public async Task Install_SegundaVez_NaoBaixaNovamente()
    {
        await RunAsync("install", "sdk", "10.0.402");
        var downloads = DownloadCount();

        var result = await RunAsync("install", "SDK-10.0.402", "--output", "json");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("alreadyInstalled", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(downloads, DownloadCount());
    }

    [Fact]
    public async Task Install_LadoALado_AtualizaHostSomenteSeMaisNovo()
    {
        await RunAsync("install", "sdk", "10.0.401");
        Assert.Contains("# host 10.0.12", await ReadAsync("dotnet"), StringComparison.Ordinal);

        var newer = await RunAsync("install", "sdk", "10.0.402", "--output", "json");

        Assert.Equal(ExitCodes.Success, newer.ExitCode);
        using var json = JsonDocument.Parse(newer.Output);
        Assert.True(json.RootElement.GetProperty("hostReplaced").GetBoolean());
        Assert.Contains("# host 10.0.13", await ReadAsync("dotnet"), StringComparison.Ordinal);
        Assert.Equal("sdk 10.0.401", await ReadAsync("sdk/10.0.401/dotnet.dll"));
        Assert.Contains("sdk-manifests/10.0.100/microsoft.net.sdk.android/36.1.2",
            json.RootElement.GetProperty("sharedPaths").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(2, new InstallationManifestStore(ManifestPath).Read().Count);
    }

    [Fact]
    public async Task Install_VersaoMaisAntigaDepois_NaoRebaixaHost()
    {
        await RunAsync("install", "sdk", "10.0.402");

        var older = await RunAsync("install", "sdk", "10.0.401");

        Assert.Equal(ExitCodes.Success, older.ExitCode);
        Assert.Contains("# host 10.0.13", await ReadAsync("dotnet"), StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(Root, "host/fxr/10.0.12")));
    }

    [Fact]
    public async Task Install_ArquivoExistente_NaoESobrescrito()
    {
        Directory.CreateDirectory(Root);
        await File.WriteAllTextAsync(Path.Combine(Root, "LICENSE.txt"), "do usuário", TestContext.Current.CancellationToken);

        var result = await RunAsync("install", "sdk", "10.0.402");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Equal("do usuário", await ReadAsync("LICENSE.txt"));
    }

    [Fact]
    public async Task Install_HashDivergente_NaoAlteraRaiz()
    {
        var url = _server.FileUrl("/bad/dotnet-sdk-linux-x64.tar.gz");
        _server.Files["/bad/dotnet-sdk-linux-x64.tar.gz"] = SyntheticArchives.Sdk("10.0.400", "10.0.11");
        _source.Add("10.0.11", ComponentKind.Sdk, "10.0.400", url, new string('a', 128), "dotnet-sdk-linux-x64.tar.gz");

        var result = await RunAsync("install", "sdk", "10.0.400");

        Assert.Equal(ExitCodes.Integrity, result.ExitCode);
        Assert.Contains("SHA-512", result.Error, StringComparison.Ordinal);
        AssertRootUntouched();
    }

    [Fact]
    public async Task Install_ArquivoMalicioso_NaoAlteraRaiz()
    {
        var archive = SyntheticArchives.Create(w =>
        {
            SyntheticArchives.File(w, "./sdk/10.0.400/dotnet.dll", "x");
            SyntheticArchives.File(w, "./../escape.txt", "x");
        });
        PublishRaw("10.0.11", ComponentKind.Sdk, "10.0.400", archive);

        var result = await RunAsync("install", "sdk", "10.0.400");

        Assert.Equal(ExitCodes.Integrity, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_home, "escape.txt")));
        AssertRootUntouched();
    }

    [Fact]
    public async Task Install_ArquivoSemPastaDoComponente_Integridade()
    {
        PublishRaw("10.0.11", ComponentKind.Sdk, "10.0.400", SyntheticArchives.Sdk("10.0.400", "10.0.11", omitSdkDirectory: true));

        var result = await RunAsync("install", "sdk", "10.0.400");

        Assert.Equal(ExitCodes.Integrity, result.ExitCode);
        AssertRootUntouched();
    }

    [Fact]
    public async Task Install_DownloadInterrompido_RetomaComRange()
    {
        _server.InterruptAfterBytes = 1000;

        var result = await RunAsync("install", "sdk", "10.0.402");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("bytes=1000-", _server.RangeHeaders);
    }

    [Fact]
    public async Task Install_ServidorIgnoraRange_ReiniciaDownload()
    {
        _server.InterruptAfterBytes = 1000;
        _server.IgnoreRange = true;

        var result = await RunAsync("install", "sdk", "10.0.402");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Equal("sdk 10.0.402", await ReadAsync("sdk/10.0.402/dotnet.dll"));
    }

    [Fact]
    public async Task Install_FalhaNaMesclagem_DesfazTudoERestauraHost()
    {
        await RunAsync("install", "sdk", "10.0.401");
        var before = Snapshot();
        var moves = 0;
        _configure = installer => installer.ConfigureMerger = merger => merger.BeforeMove = _ =>
        {
            if (++moves == 4)
            {
                throw new IOException("falha simulada");
            }
        };

        var result = await RunAsync("install", "sdk", "10.0.402");

        Assert.Equal(ExitCodes.UnexpectedError, result.ExitCode);
        Assert.Equal(before, Snapshot());
        Assert.Contains("# host 10.0.12", await ReadAsync("dotnet"), StringComparison.Ordinal);
        Assert.Single(new InstallationManifestStore(ManifestPath).Read());
    }

    [Fact]
    public async Task Install_RaizBloqueada_Conflito()
    {
        Directory.CreateDirectory(Root);
        using var held = await LockAsync();

        var result = await RunAsync("install", "sdk", "10.0.402");

        Assert.Equal(ExitCodes.Conflict, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(Root, "sdk")));
    }

    [Fact]
    public async Task Install_ManifestoCorrompido_DesfazInstalacaoSemSobrescrever()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        await File.WriteAllTextAsync(ManifestPath, "{ corrompido", TestContext.Current.CancellationToken);

        var result = await RunAsync("install", "sdk", "10.0.402");

        Assert.Equal(ExitCodes.UnexpectedError, result.ExitCode);
        Assert.Equal("{ corrompido", await File.ReadAllTextAsync(ManifestPath, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.Combine(Root, "sdk", "10.0.402")));
    }

    [Theory]
    [InlineData(new string[0], ExitCodes.UnexpectedError)]
    [InlineData(new[] { "ICU (libicu)" }, ExitCodes.MissingDependency)]
    public async Task Install_ValidacaoFalha_InformaCodigo(string[] missing, int expected)
    {
        _missing = missing;
        PublishRaw("10.0.11", ComponentKind.Sdk, "10.0.400", SyntheticArchives.Sdk("10.0.400", "10.0.11", hostExitCode: 1));

        var result = await RunAsync("install", "sdk", "10.0.400");

        Assert.Equal(expected, result.ExitCode);
        Assert.Contains("validação falhou", result.Error, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(Root, "sdk/10.0.400")));
    }

    [Fact]
    public async Task Install_VersaoNaoExataSemTerminal_ExigeYes()
    {
        var withoutYes = await RunAsync("install", "sdk", "10.0");
        Assert.Equal(ExitCodes.Usage, withoutYes.ExitCode);
        Assert.Contains("--yes", withoutYes.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Root));

        var withYes = await RunAsync("install", "sdk", "10.0", "--yes");
        Assert.Equal(ExitCodes.Success, withYes.ExitCode);
        Assert.True(Directory.Exists(Path.Combine(Root, "sdk/10.0.402")));
    }

    [Theory]
    [InlineData(true, ExitCodes.Success)]
    [InlineData(false, ExitCodes.Canceled)]
    public async Task Install_VersaoNaoExataInterativa_PerguntaAntes(bool answer, int expected)
    {
        _prompt.Interactive = true;
        _prompt.Answer = answer;

        var result = await RunAsync("install", "runtime", "lts");

        Assert.Equal(expected, result.ExitCode);
        Assert.Contains("resolve para runtime 10.0.13", _prompt.LastQuestion, StringComparison.Ordinal);
        Assert.Equal(answer, Directory.Exists(Path.Combine(Root, "shared/Microsoft.NETCore.App/10.0.13")));
    }

    [Fact]
    public async Task Install_DryRun_NaoBaixaNemCriaRaiz()
    {
        var result = await RunAsync("install", "sdk", "10.0", "--dry-run", "--output", "json");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("planned", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("10.0.402", json.RootElement.GetProperty("version").GetString());
        Assert.Equal(0, DownloadCount());
        Assert.False(Directory.Exists(Root));
    }

    [Fact]
    public async Task Install_InstallDirPersonalizado_UsaDiretorioInformado()
    {
        var custom = Path.Combine(_home, "ferramentas", "dotnet com espaço");

        var result = await RunAsync("install", "sdk", "10.0.402", "--install-dir", custom);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.True(Directory.Exists(Path.Combine(custom, "sdk/10.0.402")));
        Assert.Contains($"export DOTNET_ROOT='{custom}'", result.Output, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _server.Dispose();
        Directory.Delete(_home, recursive: true);
    }

    private void Publish(string release, ComponentKind component, string version, byte[] archive) =>
        PublishRaw(release, component, version, archive);

    private void PublishRaw(string release, ComponentKind component, string version, byte[] archive)
    {
        var name = component == ComponentKind.Sdk ? "dotnet-sdk-linux-x64.tar.gz" : "dotnet-runtime-linux-x64.tar.gz";
        var path = $"/{component.ToName()}/{version}/{name}";
        _server.Files[path] = archive;
        _source.Add(release, component, version, _server.FileUrl(path), SyntheticArchives.Sha512(archive), name);
    }

    private int DownloadCount() => _server.Requests.Count(r => r.EndsWith(".tar.gz", StringComparison.Ordinal));

    private Task<string> ReadAsync(string relative) =>
        File.ReadAllTextAsync(Path.Combine(Root, relative), TestContext.Current.CancellationToken);

    private void AssertRootUntouched()
    {
        Assert.False(Directory.Exists(Path.Combine(Root, "sdk")));
        Assert.False(File.Exists(Path.Combine(Root, "dotnet")));
        Assert.Empty(Directory.EnumerateDirectories(Root, ArchiveInstaller.StagingPrefix + "*"));
        Assert.False(File.Exists(ManifestPath));
    }

    private string Snapshot() => string.Join('\n', Directory
        .EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(Root, path))
        .Where(path => !path.StartsWith(ArchiveInstaller.StagingPrefix, StringComparison.Ordinal))
        .Order(StringComparer.Ordinal));

    private async Task<IDisposable> LockAsync()
    {
        // Mesmo mecanismo do instalador: FileShare.None (flock) no arquivo de lock da raiz.
        var stream = new FileStream(
            Path.Combine(Root, ArchiveInstaller.LockFileName),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        await Task.CompletedTask;
        return stream;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var services = new CliServices(output, error, (_, _) => _source, () => "linux-x64")
        {
            CreateInstaller = (diagnostics, progress) =>
            {
                var installer = new ArchiveInstaller(
                    new TrustedHttpFetcher(
                        new SocketsHttpHandler { AllowAutoRedirect = false },
                        new TrustedEndpoints(["127.0.0.1"], requireHttps: false),
                        diagnostics,
                        delay: (_, _) => Task.CompletedTask),
                    new InstallationManifestStore(ManifestPath),
                    diagnostics,
                    progress)
                {
                    LockTimeout = TimeSpan.FromMilliseconds(300),
                };
                _configure?.Invoke(installer);
                return installer;
            },
            IsRoot = () => false,
            HomeDirectory = () => _home,
            FindMissingDependencies = () => _missing,
            Prompt = _prompt,
        };

        var exitCode = await CliApplication.RunAsync(args, services, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private sealed class FakePrompt : IUserPrompt
    {
        public bool Interactive { get; set; }

        public bool Answer { get; set; }

        public string LastQuestion { get; private set; } = string.Empty;

        public bool IsInteractive => Interactive;

        public bool Confirm(string question)
        {
            LastQuestion = question;
            return Answer;
        }
    }
}
