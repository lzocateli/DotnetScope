using System.CommandLine;
using System.CommandLine.Help;
using DotnetScope.Cli.Commands;
using DotnetScope.Core;

namespace DotnetScope.Cli;

internal static class CliApplication
{
    internal const string Description =
        "Instala, lista, valida e remove SDKs e runtimes .NET nos escopos user e global.";

    internal static readonly Option<bool> VerboseOption = new("--verbose")
    {
        Description = "Exibe diagnóstico detalhado em stderr",
        Recursive = true,
    };

    internal static RootCommand BuildRootCommand(CliServices services)
    {
        var root = new RootCommand(Description) { VerboseOption };
        root.SetAction(parseResult => new HelpAction().Invoke(parseResult));
        root.Subcommands.Add(ListCommand.Build(services));
        return root;
    }

    internal static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken) =>
        RunAsync(args, CliServices.CreateDefault(output, error), cancellationToken);

    internal static async Task<int> RunAsync(string[] args, CliServices services, CancellationToken cancellationToken)
    {
        var parseResult = BuildRootCommand(services).Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            foreach (var parseError in parseResult.Errors)
            {
                await services.Error.WriteLineAsync(parseError.Message).ConfigureAwait(false);
            }

            await services.Error.WriteLineAsync("Use --help para ver a sintaxe.").ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        var configuration = new InvocationConfiguration { Output = services.Output, Error = services.Error };
        return await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Executa a ação de um comando convertendo falhas em mensagens e códigos de saída documentados.</summary>
    internal static async Task<int> ExecuteAsync(
        CliServices services,
        bool verbose,
        Func<IDiagnosticSink, CancellationToken, Task<int>> action,
        CancellationToken cancellationToken)
    {
        var diagnostics = new ConsoleDiagnostics(services.Error, verbose);
        try
        {
            return await action(diagnostics, cancellationToken).ConfigureAwait(false);
        }
        catch (DotnetScopeException exception)
        {
            await services.Error.WriteLineAsync($"erro: {exception.Message}").ConfigureAwait(false);
            return exception.ExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await services.Error.WriteLineAsync("Operação cancelada.").ConfigureAwait(false);
            return ExitCodes.Canceled;
        }
#pragma warning disable CA1031 // Última barreira da CLI: toda falha inesperada vira código 1 com mensagem.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            await services.Error.WriteLineAsync($"erro inesperado: {exception.Message}").ConfigureAwait(false);
            if (verbose)
            {
                await services.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            }

            return ExitCodes.UnexpectedError;
        }
    }
}
