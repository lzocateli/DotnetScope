using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Releases;
using DotnetScope.Infrastructure.Installation;
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
    public Func<IDiagnosticSink, IProgress<DownloadProgress>?, IArtifactInstaller> CreateInstaller { get; init; } =
        ArchiveInstaller.CreateOfficial;

    public Func<bool> IsRoot { get; init; } = UnixUser.IsRoot;

    public Func<string> HomeDirectory { get; init; } = () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public Func<IReadOnlyList<string>> FindMissingDependencies { get; init; } = NativeDependencyChecker.FindMissing;

    public IUserPrompt Prompt { get; init; } = new ConsolePrompt();

    public static CliServices CreateDefault(TextWriter output, TextWriter error) =>
        new(output, error, ReleaseMetadataClient.CreateOfficial, PlatformDetector.DetectRid);
}

/// <summary>Interação com o usuário; em ambiente não interativo nenhuma pergunta é feita.</summary>
internal interface IUserPrompt
{
    bool IsInteractive { get; }

    bool Confirm(string question);
}

internal sealed class ConsolePrompt : IUserPrompt
{
    public bool IsInteractive => !Console.IsInputRedirected && !Console.IsErrorRedirected;

    public bool Confirm(string question)
    {
        Console.Error.Write($"{question} [s/N] ");
        var answer = Console.ReadLine()?.Trim().ToUpperInvariant();
        return answer is "S" or "SIM" or "Y" or "YES";
    }
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

/// <summary>Progresso de download em stderr, somente em terminal interativo.</summary>
internal sealed class ConsoleDownloadProgress(TextWriter error) : IProgress<DownloadProgress>
{
    private int _lastPercent = -1;
    private long _lastMebibytes = -1;

    public void Report(DownloadProgress value)
    {
        var mebibytes = value.Received / (1024 * 1024);
        if (value.Total is > 0)
        {
            var percent = (int)(value.Received * 100 / value.Total.Value);
            if (percent == _lastPercent)
            {
                return;
            }

            _lastPercent = percent;
            error.Write($"\rBaixando {value.FileName}: {percent,3}% ({mebibytes}/{value.Total.Value / (1024 * 1024)} MiB)");
            if (percent == 100)
            {
                error.WriteLine();
            }
        }
        else if (mebibytes != _lastMebibytes)
        {
            _lastMebibytes = mebibytes;
            error.Write($"\rBaixando {value.FileName}: {mebibytes} MiB");
        }
    }
}
