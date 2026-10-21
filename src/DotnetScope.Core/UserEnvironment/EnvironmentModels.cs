namespace DotnetScope.Core.UserEnvironment;

/// <summary>Valores gerenciados no ambiente do usuário (seção 4.1 do plano).</summary>
public sealed record EnvironmentTarget(string DotnetRoot, string ToolsDirectory, bool TelemetryOptOut);

/// <summary>Política quando terceiros já definem <c>DOTNET_ROOT</c> com valor diferente.</summary>
public enum EnvConflictPolicy
{
    Fail,
    Override,
    Skip,
}

/// <summary>Atribuição de <c>DOTNET_ROOT</c> feita fora do bloco do DotnetScope.</summary>
/// <param name="ResolvedValue">Valor normalizado, ou nulo quando depende de expansões que não podem ser avaliadas.</param>
public sealed record ThirdPartyAssignment(string File, int Line, string RawValue, string? ResolvedValue);
