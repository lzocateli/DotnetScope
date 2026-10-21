using DotnetScope.Core;

namespace DotnetScope.Infrastructure.Installation;

/// <summary>
/// Lock exclusivo entre processos baseado em <see cref="FileShare.None"/> (flock no Unix). É liberado no descarte ou
/// quando o processo termina, inclusive de forma anormal.
/// </summary>
internal sealed class InstallLock : IDisposable
{
    private readonly FileStream _stream;

    private InstallLock(FileStream stream) => _stream = stream;

    public static async Task<InstallLock> AcquireAsync(
        string path,
        TimeSpan timeout,
        IDiagnosticSink diagnostics,
        CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        var deadline = DateTime.UtcNow + timeout;
        var warned = false;
        while (true)
        {
            try
            {
                return new InstallLock(new FileStream(path, options));
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                if (!warned)
                {
                    diagnostics.Warning($"aguardando outra operação do dotnetscope liberar {path}.");
                    warned = true;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new DotnetScopeException(
                    ExitCodes.Conflict,
                    $"Outra operação do dotnetscope está em andamento ({path}). Tente novamente quando ela terminar.",
                    exception);
            }
        }
    }

    public void Dispose() => _stream.Dispose();
}
