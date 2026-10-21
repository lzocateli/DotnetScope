using System.Runtime.Versioning;
using System.Text.Json;
using DotnetScope.Cli;
using DotnetScope.Core;
using DotnetScope.Core.Releases;
using DotnetScope.Infrastructure.Http;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.UserEnvironment;

namespace DotnetScope.IntegrationTests;

/// <summary>Ciclo de vida da F4: uninstall, list --installed, doctor, --archive e --from-global-json.</summary>
// Usa host de teste em shell script, modos Unix e /bin/sh; filtrado no Windows por Platform=Unix.
[UnsupportedOSPlatform("windows")]
[Trait("Platform", "Unix")]
public sealed class LifecycleTests : IDisposable
{
    private readonly LocalMetadataServer _server = new();
    private readonly SyntheticMetadataSource _source = new();
    private readonly string _home = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));
    private bool _metadataUnavailable;
    private string? _currentDirectory;

    public LifecycleTests()
    {
        Directory.CreateDirectory(_home);
        Publish("10.0.13", "10.0.402", SyntheticArchives.Sdk("10.0.402", "10.0.13"));
        Publish("10.0.13", "10.0.113", SyntheticArchives.Sdk("10.0.113", "10.0.13"));
        Publish("10.0.12", "10.0.401", SyntheticArchives.Sdk("10.0.401", "10.0.12"));
    }

    private string Root => Path.Combine(_home, ".dotnet");

    private string ManifestPath => Path.Combine(_home, "data", "manifest.json");

    private InstallationManifestStore Manifest => new(ManifestPath);

    [Fact]
    public async Task Uninstall_DuasInstalacoesCompartilhadas_RaizEPerfisVoltamAoEstadoInicial()
    {
        await RunAsync("install", "sdk", "10.0.402");
        await RunAsync("install", "sdk", "10.0.113");
        Assert.True(File.Exists(Path.Combine(_home, ".profile")));

        var first = await RunAsync("uninstall", "sdk", "10.0.402", "--output", "json");

        Assert.Equal(ExitCodes.Success, first.ExitCode);
        using (var json = JsonDocument.Parse(first.Output))
        {
            Assert.Equal(["sdk/10.0.402"], json.RootElement.GetProperty("removedPaths").EnumerateArray().Select(e => e.GetString()));
            Assert.Contains("shared/Microsoft.NETCore.App/10.0.13",
                json.RootElement.GetProperty("keptPaths").EnumerateArray().Select(e => e.GetProperty("path").GetString()));
            Assert.Equal("unchanged", json.RootElement.GetProperty("environment").GetString());
        }

        Assert.False(Directory.Exists(Path.Combine(Root, "sdk/10.0.402")));
        Assert.True(Directory.Exists(Path.Combine(Root, "sdk/10.0.113")));
        var remaining = Assert.Single(Manifest.Read());
        Assert.Contains("shared/Microsoft.NETCore.App/10.0.13", remaining.AddedPaths);
        Assert.Contains("dotnet", remaining.AddedPaths);

        var last = await RunAsync("uninstall", "SDK-10.0.113");

        Assert.Equal(ExitCodes.Success, last.ExitCode);
        Assert.Contains("DOTNET_ROOT/PATH foram removidos", last.Output, StringComparison.Ordinal);
        Assert.Equal([ArchiveInstaller.LockFileName], Directory.EnumerateFileSystemEntries(Root).Select(Path.GetFileName));
        Assert.Empty(Manifest.Read());
        Assert.False(File.Exists(Path.Combine(_home, ".profile")));
        Assert.False(File.Exists(Path.Combine(_home, ".config/dotnetscope/env.sh")));
    }

    [Fact]
    public async Task Uninstall_KeepEnv_MantemAmbiente()
    {
        await RunAsync("install", "sdk", "10.0.402");

        var result = await RunAsync("uninstall", "sdk", "10.0.402", "--keep-env");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(_home, ".config/dotnetscope/env.sh")));
    }

    [Fact]
    public async Task Uninstall_ComponenteExterno_ExigeForceERemoveSoAPasta()
    {
        await RunAsync("install", "sdk", "10.0.402");
        Directory.CreateDirectory(Path.Combine(Root, "sdk/9.0.100"));

        var refused = await RunAsync("uninstall", "sdk", "9.0.100");
        var forced = await RunAsync("uninstall", "sdk", "9.0.100", "--force", "--output", "json");

        Assert.Equal(ExitCodes.Conflict, refused.ExitCode);
        Assert.Equal(ExitCodes.Success, forced.ExitCode);
        using var json = JsonDocument.Parse(forced.Output);
        Assert.Equal("external", json.RootElement.GetProperty("origin").GetString());
        Assert.False(Directory.Exists(Path.Combine(Root, "sdk/9.0.100")));
        Assert.True(Directory.Exists(Path.Combine(Root, "sdk/10.0.402")));
        Assert.Single(Manifest.Read());
    }

    [Theory]
    [InlineData(ExitCodes.NotFound, "uninstall", "sdk", "10.0.402")]
    [InlineData(ExitCodes.Usage, "uninstall", "sdk", "10.0")]
    [InlineData(ExitCodes.Usage, "uninstall", "sdk", "lts")]
    public async Task Uninstall_EntradasInvalidas(int expected, params string[] args)
    {
        Directory.CreateDirectory(Root);

        Assert.Equal(expected, (await RunAsync(args)).ExitCode);
    }

    [Fact]
    public async Task Uninstall_DryRun_NaoRemoveNada()
    {
        await RunAsync("install", "sdk", "10.0.402");

        var result = await RunAsync("uninstall", "sdk", "10.0.402", "--dry-run");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("Plano: remover", result.Output, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(Root, "sdk/10.0.402")));
        Assert.Single(Manifest.Read());
    }

    [Fact]
    public async Task Uninstall_FalhaAoAtualizarManifesto_RestauraArquivos()
    {
        await RunAsync("install", "sdk", "10.0.402");
        var before = Snapshot();
        var manifest = await File.ReadAllTextAsync(ManifestPath, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(ManifestPath, manifest + "lixo", TestContext.Current.CancellationToken);

        var result = await RunAsync("uninstall", "sdk", "10.0.402");

        Assert.Equal(ExitCodes.UnexpectedError, result.ExitCode);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task ListInstalled_InformaOrigemDeCadaComponente()
    {
        await RunAsync("install", "sdk", "10.0.402");
        Directory.CreateDirectory(Path.Combine(Root, "sdk/9.0.100"));

        var result = await RunAsync("list", "--installed", "--output", "json");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var items = json.RootElement.GetProperty("installations").EnumerateArray()
            .ToDictionary(e => $"{e.GetProperty("component").GetString()} {e.GetProperty("version").GetString()}");
        Assert.Equal("dotnetscope", items["sdk 10.0.402"].GetProperty("origin").GetString());
        Assert.Equal("external", items["sdk 9.0.100"].GetProperty("origin").GetString());
        Assert.Equal("sdk 10.0.402", items["runtime 10.0.13"].GetProperty("managedBy").GetString());
    }

    [Fact]
    public async Task List_AvailableEInstalledJuntos_ErroDeUso() =>
        Assert.Equal(ExitCodes.Usage, (await RunAsync("list", "--available", "--installed")).ExitCode);

    [Fact]
    public async Task Doctor_InstalacaoSaudavel_SemProblemas()
    {
        await RunAsync("install", "sdk", "10.0.402");

        var result = await RunAsync("doctor");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("Nenhum problema encontrado", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Doctor_InstalacaoInterrompida_ReparaPeloJournal()
    {
        await RunAsync("install", "sdk", "10.0.402");
        Directory.CreateDirectory(Path.Combine(Root, "sdk/10.0.999"));
        var staging = CreateStaging(new MergeJournal("install", Root, ["sdk/10.0.999"], null));

        var check = await RunAsync("doctor", "--output", "json");
        var repair = await RunAsync("doctor", "--repair");
        var after = await RunAsync("doctor");

        Assert.Equal(ExitCodes.Conflict, check.ExitCode);
        Assert.Contains("interrupted-install", check.Output, StringComparison.Ordinal);
        Assert.Equal(ExitCodes.Success, repair.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(Root, "sdk/10.0.999")));
        Assert.False(Directory.Exists(staging));
        Assert.True(Directory.Exists(Path.Combine(Root, "sdk/10.0.402")));
        Assert.Equal(ExitCodes.Success, after.ExitCode);
    }

    [Fact]
    public async Task Doctor_RemocaoInterrompida_RestauraDaLixeira()
    {
        await RunAsync("install", "sdk", "10.0.402");
        var staging = CreateStaging(new MergeJournal("uninstall", Root, ["sdk/10.0.402"], null, "sdk", "10.0.402"));
        Directory.CreateDirectory(Path.Combine(staging, "trash/sdk"));
        Directory.Move(Path.Combine(Root, "sdk/10.0.402"), Path.Combine(staging, "trash/sdk/10.0.402"));

        var repair = await RunAsync("doctor", "--repair", "--output", "json");

        Assert.Equal(ExitCodes.Success, repair.ExitCode);
        Assert.Contains("interrupted-uninstall", repair.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Root, "sdk/10.0.402/dotnet.dll")));
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public async Task Doctor_TrocaDeHostInterrompida_RestauraHostDoBackup()
    {
        await RunAsync("install", "sdk", "10.0.401");
        var host = await File.ReadAllTextAsync(Path.Combine(Root, "dotnet"), TestContext.Current.CancellationToken);
        var staging = CreateStaging(new MergeJournal("install", Root, [], Path.Combine("/qualquer", "caminho"), "sdk", "10.0.402"));
        Directory.CreateDirectory(Path.Combine(staging, "backup"));
        File.Move(Path.Combine(Root, "dotnet"), Path.Combine(staging, "backup", "dotnet"));

        var repair = await RunAsync("doctor", "--repair", "--output", "json");

        Assert.Contains("interrupted-install", repair.Output, StringComparison.Ordinal);
        Assert.Equal(host, await File.ReadAllTextAsync(Path.Combine(Root, "dotnet"), TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public async Task Doctor_JournalComCaminhoForaDaRaiz_NaoApagaNada()
    {
        await RunAsync("install", "sdk", "10.0.402");
        var outside = Path.Combine(_home, "projetos");
        Directory.CreateDirectory(outside);
        CreateStaging(new MergeJournal("install", Root, ["../projetos"], null, "sdk", "10.0.999"));

        var repair = await RunAsync("doctor", "--repair");

        Assert.Equal(ExitCodes.Conflict, repair.ExitCode);
        Assert.Contains("[invalid-journal]", repair.Output, StringComparison.Ordinal);
        Assert.True(Directory.Exists(outside));
    }

    [Fact]
    public async Task Doctor_RemocaoInterrompidaAntesDoPrimeiroMovimento_MantemArquivos()
    {
        await RunAsync("install", "sdk", "10.0.402");
        var staging = CreateStaging(new MergeJournal("uninstall", Root, ["sdk/10.0.402", "dotnet"], null, "sdk", "10.0.402"));

        var repair = await RunAsync("doctor", "--repair");

        Assert.Equal(ExitCodes.Success, repair.ExitCode);
        Assert.True(Directory.Exists(Path.Combine(Root, "sdk/10.0.402")));
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public async Task Doctor_StagingComLixeiraSemJournal_NaoEhApagado()
    {
        await RunAsync("install", "sdk", "10.0.402");
        var staging = Path.Combine(Root, ArchiveInstaller.StagingPrefix + "sem-journal");
        Directory.CreateDirectory(Path.Combine(staging, "trash/sdk/9.0.100"));

        var repair = await RunAsync("doctor", "--repair");

        Assert.Equal(ExitCodes.Conflict, repair.ExitCode);
        Assert.Contains("[unknown-staging]", repair.Output, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(staging, "trash/sdk/9.0.100")));
    }

    [Fact]
    public async Task Doctor_RegistroSemArquivos_TransferePosseESemVazamento()
    {
        await RunAsync("install", "sdk", "10.0.402");
        await RunAsync("install", "sdk", "10.0.113");
        Directory.Delete(Path.Combine(Root, "sdk/10.0.402"), recursive: true);

        var repair = await RunAsync("doctor", "--repair");
        var uninstall = await RunAsync("uninstall", "sdk", "10.0.113");

        Assert.Equal(ExitCodes.Success, repair.ExitCode);
        Assert.Equal(ExitCodes.Success, uninstall.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(Root, "shared")));
        Assert.False(File.Exists(Path.Combine(Root, "dotnet")));
    }

    [Fact]
    public async Task Uninstall_ForceEmRuntimeDeSdkGerenciado_Recusa()
    {
        await RunAsync("install", "sdk", "10.0.402");

        var result = await RunAsync("uninstall", "runtime", "10.0.13", "--force");

        Assert.Equal(ExitCodes.Conflict, result.ExitCode);
        Assert.True(Directory.Exists(Path.Combine(Root, "shared/Microsoft.NETCore.App/10.0.13")));
    }

    [Fact]
    public async Task Uninstall_ManifestoAdulteradoComCaminhoExterno_NaoMoveNadaForaDaRaiz()
    {
        await RunAsync("install", "sdk", "10.0.402");
        var outside = Path.Combine(_home, "projetos");
        Directory.CreateDirectory(outside);
        var text = await File.ReadAllTextAsync(ManifestPath, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(ManifestPath, text.Replace("\"sdk/10.0.402\"", "\"../projetos\"", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        var result = await RunAsync("uninstall", "sdk", "10.0.402");

        Assert.Equal(ExitCodes.UnexpectedError, result.ExitCode);
        Assert.True(Directory.Exists(outside));
        Assert.True(Directory.Exists(Path.Combine(Root, "sdk/10.0.402")));
    }

    [Fact]
    public async Task Doctor_RegistroSemArquivos_RemoveRegistroComRepair()
    {
        await RunAsync("install", "sdk", "10.0.402");
        Directory.Delete(Path.Combine(Root, "sdk/10.0.402"), recursive: true);

        var check = await RunAsync("doctor");
        var repair = await RunAsync("doctor", "--repair");

        Assert.Equal(ExitCodes.Conflict, check.ExitCode);
        Assert.Contains("[stale-record]", check.Output, StringComparison.Ordinal);
        Assert.Equal(ExitCodes.Success, repair.ExitCode);
        Assert.Empty(Manifest.Read());
    }

    [Fact]
    public async Task Doctor_StagingSemJournal_AvisoERemocao()
    {
        await RunAsync("install", "sdk", "10.0.402");
        var staging = Path.Combine(Root, ArchiveInstaller.StagingPrefix + "abandonado");
        Directory.CreateDirectory(staging);

        var check = await RunAsync("doctor");
        var repair = await RunAsync("doctor", "--repair");

        Assert.Equal(ExitCodes.Success, check.ExitCode);
        Assert.Contains("[orphan-staging]", check.Output, StringComparison.Ordinal);
        Assert.Equal(ExitCodes.Success, repair.ExitCode);
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public async Task InstallArchive_OfflineComHashCorreto_InstalaSemMetadata()
    {
        _metadataUnavailable = true;
        var archive = WriteArchive("dotnet-sdk-10.0.402-linux-x64.tar.gz", SyntheticArchives.Sdk("10.0.402", "10.0.13"));

        var result = await RunAsync("install", "sdk", "10.0.402", "--archive", archive.Path, "--sha512", archive.Sha512.ToUpperInvariant());

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.True(Directory.Exists(Path.Combine(Root, "sdk/10.0.402")));
        var record = Assert.Single(Manifest.Read());
        Assert.True(record.Url.IsFile);
        Assert.Equal(archive.Sha512, record.Sha512);
        Assert.Empty(_server.Requests);
    }

    [Theory]
    [InlineData("10.0.402", true)]
    [InlineData("10.0.401", false)]
    public async Task InstallArchive_HashOuConteudoDivergente_IntegridadeSemAlterarRaiz(string requested, bool wrongHash)
    {
        _metadataUnavailable = true;
        var archive = WriteArchive("sdk.tar.gz", SyntheticArchives.Sdk("10.0.402", "10.0.13"));
        var hash = wrongHash ? new string('a', 128) : archive.Sha512;

        var result = await RunAsync("install", "sdk", requested, "--archive", archive.Path, "--sha512", hash);

        Assert.Equal(ExitCodes.Integrity, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(Root, "sdk")));
        Assert.Empty(Manifest.Read());
    }

    [Fact]
    public async Task InstallFromGlobalJson_RollForwardExigeConfirmacao()
    {
        var project = Path.Combine(_home, "projeto", "src");
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(
            Path.Combine(_home, "projeto", "global.json"),
            "{ \"sdk\": { \"version\": \"10.0.400\", \"rollForward\": \"latestFeature\" } }",
            TestContext.Current.CancellationToken);
        _currentDirectory = project;

        var withoutYes = await RunAsync("install", "--from-global-json");
        var withYes = await RunAsync("install", "--from-global-json", "--yes", "--output", "json");

        Assert.Equal(ExitCodes.Usage, withoutYes.ExitCode);
        Assert.Equal(ExitCodes.Success, withYes.ExitCode);
        using var json = JsonDocument.Parse(withYes.Output);
        Assert.Equal("10.0.402", json.RootElement.GetProperty("version").GetString());
        Assert.StartsWith("global.json 10.0.400", json.RootElement.GetProperty("request").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallFromGlobalJson_VersaoExataDisable_InstalaSemConfirmar()
    {
        var file = Path.Combine(_home, "global.json");
        await File.WriteAllTextAsync(file, "{ \"sdk\": { \"version\": \"10.0.401\", \"rollForward\": \"disable\" } }", TestContext.Current.CancellationToken);

        var result = await RunAsync("install", "--from-global-json", file);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.True(Directory.Exists(Path.Combine(Root, "sdk/10.0.401")));
    }

    public void Dispose()
    {
        _server.Dispose();
        Directory.Delete(_home, recursive: true);
    }

    private (string Path, string Sha512) WriteArchive(string name, byte[] content)
    {
        var directory = Path.Combine(_home, "downloads");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, content);
        return (path, SyntheticArchives.Sha512(content));
    }

    private string CreateStaging(MergeJournal journal)
    {
        var staging = Path.Combine(Root, ArchiveInstaller.StagingPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "journal.json"), JsonSerializer.Serialize(journal, InstallationJsonContext.Default.MergeJournal));
        return staging;
    }

    private void Publish(string release, string version, byte[] archive)
    {
        var path = $"/sdk/{version}/dotnet-sdk-linux-x64.tar.gz";
        _server.Files[path] = archive;
        _source.Add(release, ComponentKind.Sdk, version, _server.FileUrl(path), SyntheticArchives.Sha512(archive), "dotnet-sdk-linux-x64.tar.gz");
    }

    private string Snapshot() => string.Join('\n', Directory
        .EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(Root, path))
        .Order(StringComparer.Ordinal));

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var services = new CliServices(
            output,
            error,
            (_, _) => _metadataUnavailable ? throw new InvalidOperationException("metadata não deve ser consultado") : _source,
            () => "linux-x64")
        {
            CreateInstaller = (diagnostics, progress) => new ArchiveInstaller(
                new TrustedHttpFetcher(
                    new SocketsHttpHandler { AllowAutoRedirect = false },
                    new TrustedEndpoints(["127.0.0.1"], requireHttps: false),
                    diagnostics,
                    delay: (_, _) => Task.CompletedTask),
                Manifest,
                diagnostics,
                progress),
            CreateManifestStore = () => Manifest,
            SystemRoots = () => [],
            IsPackageManaged = _ => false,
            CurrentDirectory = () => _currentDirectory ?? _home,
            IsRoot = () => false,
            HomeDirectory = () => _home,
            FindMissingDependencies = () => [],
            Prompt = new NonInteractive(),
            CreateEnvironmentLayout = home => new EnvironmentLayout(
                home, Path.Combine(home, ".config"), Path.Combine(home, ".local", "state"), IsMacOS: false, FishAvailable: false, SystemFiles: []),
            ProbeLoginShell = (_, _, _) => Task.FromResult<LoginShellResult?>(null),
        };

        var exitCode = await CliApplication.RunAsync(args, services, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private sealed class NonInteractive : IUserPrompt
    {
        public bool IsInteractive => false;

        public bool Confirm(string question) => false;
    }
}
