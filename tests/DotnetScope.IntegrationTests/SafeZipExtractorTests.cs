using System.IO.Compression;
using DotnetScope.Core;
using DotnetScope.Infrastructure.Installation;

namespace DotnetScope.IntegrationTests;

public sealed class SafeZipExtractorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));

    public SafeZipExtractorTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Extract_LayoutOficial_ExtraiArquivosEDiretorios()
    {
        var destination = await ExtractAsync(Zip(
            ("dotnet.exe", "host"),
            ("sdk/", null),
            ("sdk/10.0.402/dotnet.dll", "sdk 10.0.402"),
            ("shared/Microsoft.NETCore.App/10.0.13/System.Runtime.dll", "rt")));

        Assert.Equal("sdk 10.0.402", await File.ReadAllTextAsync(Path.Combine(destination, "sdk", "10.0.402", "dotnet.dll"), TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(destination, "dotnet.exe")));
        Assert.True(Directory.Exists(Path.Combine(destination, "shared", "Microsoft.NETCore.App", "10.0.13")));
    }

    [Theory]
    [InlineData("/etc/evil")]
    [InlineData("../evil")]
    [InlineData("sdk/../../evil")]
    [InlineData("sdk\\..\\evil")]
    [InlineData("C:/Windows/evil")]
    [InlineData("C:evil")]
    [InlineData("dotnet.exe:stream")]
    [InlineData("sdk/CON")]
    [InlineData("sdk/nul.txt")]
    [InlineData("sdk/com1.dll")]
    [InlineData("sdk/LPT9")]
    [InlineData("sdk/evil.")]
    [InlineData("sdk/evil ")]
    [InlineData("sdk/a|b")]
    [InlineData("sdk/a*b")]
    public async Task Extract_NomesMaliciosos_SaoRecusadosSemEscreverForaDoDestino(string name)
    {
        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => ExtractAsync(Zip(("ok.txt", "ok"), (name, "x"))));

        Assert.Equal(ExitCodes.Integrity, exception.ExitCode);
        Assert.False(File.Exists(Path.Combine(_directory, "evil")));
    }

    [Theory]
    [InlineData("dotnet.exe", "DOTNET.EXE")]
    [InlineData("sdk/a", "sdk/a/b")]
    [InlineData("sdk/a/b", "sdk/a")]
    [InlineData("sdk/a", "SDK/A/")]
    public async Task Extract_ConflitosDeNome_SaoRecusados(string first, string second)
    {
        var exception = await Assert.ThrowsAsync<DotnetScopeException>(
            () => ExtractAsync(Zip((first, first.EndsWith('/') ? null : "1"), (second, second.EndsWith('/') ? null : "2"))));

        Assert.Equal(ExitCodes.Integrity, exception.ExitCode);
    }

    [Fact]
    public async Task Extract_LinkSimbolicoUnix_EhRecusado()
    {
        var path = Path.Combine(_directory, "link.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("evil");
            entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("/etc/passwd");
        }

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => ExtractAsync(path));

        Assert.Contains("link simbólico", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extract_AcimaDoLimite_EhRecusado()
    {
        var path = Zip(("a.bin", new string('x', 4096)));

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(
            () => new SafeZipExtractor(maxTotalBytes: 1024).ExtractAsync(path, Path.Combine(_directory, "out"), TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Integrity, exception.ExitCode);
    }

    [Fact]
    public async Task Extract_ExcessoDeEntradas_EhRecusado()
    {
        var path = Zip(("a", "1"), ("b", "2"), ("c", "3"), ("d", "4"));

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(
            () => new SafeZipExtractor(maxEntries: 3).ExtractAsync(path, Path.Combine(_directory, "out"), TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Integrity, exception.ExitCode);
    }

    [Fact]
    public async Task Extract_ArquivoCorrompido_EhRecusado()
    {
        var path = Path.Combine(_directory, "corrompido.zip");
        await File.WriteAllBytesAsync(path, [0x50, 0x4B, 0x03, 0x04, 1, 2, 3], TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => ExtractAsync(path));

        Assert.Equal(ExitCodes.Integrity, exception.ExitCode);
    }

    [Fact]
    public void CreateExtractor_EscolhePeloFormato()
    {
        Assert.IsType<SafeZipExtractor>(ArchiveInstaller.CreateExtractor("dotnet-sdk-10.0.402-win-x64.zip"));
        Assert.IsType<SafeTarExtractor>(ArchiveInstaller.CreateExtractor("dotnet-sdk-10.0.402-linux-x64.tar.gz"));
        Assert.Equal(ExitCodes.NotFound, Assert.Throws<DotnetScopeException>(() => ArchiveInstaller.CreateExtractor("dotnet-sdk.exe")).ExitCode);
    }

    private string Zip(params (string Name, string? Content)[] entries)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            if (content is not null)
            {
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return path;
    }

    private async Task<string> ExtractAsync(string archive)
    {
        var destination = Path.Combine(_directory, "out-" + Guid.NewGuid().ToString("N"));
        await new SafeZipExtractor().ExtractAsync(archive, destination, TestContext.Current.CancellationToken);
        return destination;
    }
}
