using System.CommandLine;
using System.CommandLine.Help;
using DotnetScope.Core;

namespace DotnetScope.Cli;

internal static class CliApplication
{
    internal const string Description =
        "Instala, lista, valida e remove SDKs e runtimes .NET nos escopos user e global.";

    internal static RootCommand BuildRootCommand()
    {
        var root = new RootCommand(Description);
        root.SetAction(parseResult => new HelpAction().Invoke(parseResult));
        return root;
    }

    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var parseResult = BuildRootCommand().Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            foreach (var parseError in parseResult.Errors)
            {
                await error.WriteLineAsync(parseError.Message).ConfigureAwait(false);
            }

            await error.WriteLineAsync("Use --help para ver a sintaxe.").ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        var configuration = new InvocationConfiguration { Output = output, Error = error };
        return await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
    }
}
