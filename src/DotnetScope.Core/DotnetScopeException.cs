namespace DotnetScope.Core;

/// <summary>
/// Falha esperada da CLI, com mensagem acionável para o usuário e código de saída de <see cref="ExitCodes"/>.
/// </summary>
public sealed class DotnetScopeException : Exception
{
    public DotnetScopeException(int exitCode, string message, Exception? innerException = null)
        : base(message, innerException) => ExitCode = exitCode;

    public int ExitCode { get; }
}
