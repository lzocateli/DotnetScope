using DotnetScope.Core;
using DotnetScope.Core.Releases;
using DotnetScope.Infrastructure.Metadata;
using DotnetScope.Infrastructure.Platform;

namespace DotnetScope.Cli;

/// <summary>Dependências da CLI, substituíveis em testes.</summary>
internal sealed record CliServices(
    TextWriter Output,
    TextWriter Error,
    Func<MetadataOptions, IDiagnosticSink, IReleaseMetadataSource> CreateMetadataSource,
    Func<string> DetectRid)
{
    public static CliServices CreateDefault(TextWriter output, TextWriter error) =>
        new(output, error, ReleaseMetadataClient.CreateOfficial, PlatformDetector.DetectRid);
}

internal sealed class ConsoleDiagnostics(TextWriter error, bool verbose) : IDiagnosticSink
{
    public void Warning(string message) => error.WriteLine($"aviso: {message}");

    public void Verbose(string message)
    {
        if (verbose)
        {
            error.WriteLine($"detalhe: {message}");
        }
    }
}
