namespace DotnetScope.Cli;

internal static class Program
{
    public static Task<int> Main(string[] args) =>
        CliApplication.RunAsync(args, Console.Out, Console.Error, CancellationToken.None);
}
