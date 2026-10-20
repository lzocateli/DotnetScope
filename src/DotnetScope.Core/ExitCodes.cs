namespace DotnetScope.Core;

/// <summary>
/// Códigos de saída da CLI. São contrato estável a partir da primeira release;
/// consulte a seção 3.3 de docs/plano-implementacao.md.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int UnexpectedError = 1;
    public const int Usage = 2;
    public const int NotFound = 3;
    public const int Network = 4;
    public const int Integrity = 5;
    public const int Conflict = 6;
    public const int Privilege = 7;
    public const int MissingDependency = 8;
    public const int Canceled = 130;
}
