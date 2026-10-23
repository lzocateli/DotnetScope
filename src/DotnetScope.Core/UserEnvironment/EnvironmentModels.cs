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

/// <summary>Onde o ambiente do usuário é persistido no Windows (<c>--windows-env-target</c>).</summary>
public enum WindowsEnvironmentTarget
{
    /// <summary>Variáveis do usuário em <c>HKCU\Environment</c>, vistas por todos os processos novos.</summary>
    Registry,

    /// <summary>
    /// Script em <c>%LOCALAPPDATA%\dotnetscope\env.ps1</c> carregado pelos perfis do PowerShell, sem gravar no registro;
    /// somente terminais PowerShell recebem o ambiente.
    /// </summary>
    Profile,
}

/// <summary>Atribuição de <c>DOTNET_ROOT</c> feita fora do bloco do DotnetScope.</summary>
/// <param name="ResolvedValue">Valor normalizado, ou nulo quando depende de expansões que não podem ser avaliadas.</param>
public sealed record ThirdPartyAssignment(string File, int Line, string RawValue, string? ResolvedValue)
{
    /// <summary>Arquivo e linha, ou somente a origem quando não há linha (por exemplo, um valor do registro).</summary>
    public string Location => Line > 0 ? $"{File}:{Line}" : File;
}
