namespace DotnetScope.Infrastructure.Installation;

/// <summary>
/// No Windows, antivírus e indexadores abrem por instantes arquivos recém-gravados e fazem renomeações falharem com
/// acesso negado; a operação é repetida por até ~3 s. Em Linux e macOS executa uma única vez.
/// </summary>
internal static class FileSystemRetry
{
    internal static int Attempts { get; set; } = 15;

    public static void Run(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception exception) when (OperatingSystem.IsWindows()
                && attempt < Attempts
                && exception is UnauthorizedAccessException or IOException
                && exception is not FileNotFoundException and not DirectoryNotFoundException)
            {
                Thread.Sleep(200);
            }
        }
    }
}
