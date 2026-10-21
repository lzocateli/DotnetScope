using System.Formats.Tar;
using DotnetScope.Core;
using DotnetScope.Infrastructure.Installation;

namespace DotnetScope.IntegrationTests;

public sealed class SafeTarExtractorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));

    public SafeTarExtractorTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Extract_ArquivoOficial_PreservaConteudoEPermissoes()
    {
        var destination = await ExtractAsync(SyntheticArchives.Sdk("10.0.402", "10.0.13"));

        Assert.Equal("sdk 10.0.402", await File.ReadAllTextAsync(Path.Combine(destination, "sdk/10.0.402/dotnet.dll"), TestContext.Current.CancellationToken));
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(Path.Combine(destination, "dotnet")).HasFlag(UnixFileMode.UserExecute));
            Assert.False(File.GetUnixFileMode(Path.Combine(destination, "LICENSE.txt")).HasFlag(UnixFileMode.UserExecute));
        }
    }

    public static TheoryData<string, Action<TarWriter>> MaliciousArchives => new()
    {
        { "caminho absoluto", w => SyntheticArchives.File(w, "/tmp/dotnetscope-evil", "x") },
        { "travessia", w => SyntheticArchives.File(w, "./sdk/../../dotnetscope-evil", "x") },
        { "travessia no início", w => SyntheticArchives.File(w, "../dotnetscope-evil", "x") },
        { "barra invertida", w => SyntheticArchives.File(w, "sdk\\..\\evil", "x") },
        { "link simbólico", w => w.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "./evil") { LinkName = "/etc/passwd" }) },
        { "link simbólico relativo", w => w.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "./evil") { LinkName = "sdk" }) },
        { "hard link", w => w.WriteEntry(new PaxTarEntry(TarEntryType.HardLink, "./evil") { LinkName = "/etc/passwd" }) },
        { "dispositivo", w => w.WriteEntry(new PaxTarEntry(TarEntryType.CharacterDevice, "./evil")) },
        { "fifo", w => w.WriteEntry(new PaxTarEntry(TarEntryType.Fifo, "./evil")) },
        {
            "duplicada", w =>
            {
                SyntheticArchives.File(w, "./dotnet", "a");
                SyntheticArchives.File(w, "./dotnet", "b");
            }
        },
        {
            "arquivo como diretório", w =>
            {
                SyntheticArchives.File(w, "./sdk", "a");
                SyntheticArchives.File(w, "./sdk/10.0.402/x", "b");
            }
        },
    };

    [Theory]
    [MemberData(nameof(MaliciousArchives))]
    public async Task Extract_EntradaMaliciosa_RecusaComIntegridade(string caso, Action<TarWriter> write)
    {
        var archive = SyntheticArchives.Create(write);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => ExtractAsync(archive));

        Assert.True(exception.ExitCode == ExitCodes.Integrity, caso);
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "dotnetscope-evil")), caso);
        Assert.False(File.Exists(Path.Combine(_directory, "dotnetscope-evil")), caso);
    }

    [Fact]
    public async Task Extract_AcimaDoLimiteDeTamanho_Recusa()
    {
        var archive = SyntheticArchives.Create(w => SyntheticArchives.File(w, "./grande", new string('x', 4096)));
        var path = Path.Combine(_directory, "a.tar.gz");
        await File.WriteAllBytesAsync(path, archive, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() =>
            new SafeTarExtractor(maxTotalBytes: 1024).ExtractAsync(path, Path.Combine(_directory, "out"), TestContext.Current.CancellationToken));

        Assert.Contains("excede", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extract_ExcessoDeEntradas_Recusa()
    {
        var archive = SyntheticArchives.Create(w =>
        {
            for (var i = 0; i < 5; i++)
            {
                SyntheticArchives.File(w, $"./f{i}", "x");
            }
        });
        var path = Path.Combine(_directory, "a.tar.gz");
        await File.WriteAllBytesAsync(path, archive, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DotnetScopeException>(() =>
            new SafeTarExtractor(maxEntries: 3).ExtractAsync(path, Path.Combine(_directory, "out"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Extract_ArquivoTruncadoOuNaoGzip_Recusa(bool truncated)
    {
        var valid = SyntheticArchives.Sdk("10.0.402", "10.0.13");
        var archive = truncated ? valid[..(valid.Length / 2)] : "não é gzip"u8.ToArray();

        var exception = await Assert.ThrowsAsync<DotnetScopeException>(() => ExtractAsync(archive));

        Assert.Equal(ExitCodes.Integrity, exception.ExitCode);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private async Task<string> ExtractAsync(byte[] archive)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.tar.gz");
        await File.WriteAllBytesAsync(path, archive, TestContext.Current.CancellationToken);
        var destination = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        await new SafeTarExtractor().ExtractAsync(path, destination, TestContext.Current.CancellationToken);
        return destination;
    }
}
