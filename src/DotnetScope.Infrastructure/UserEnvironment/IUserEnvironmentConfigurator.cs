using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;

namespace DotnetScope.Infrastructure.UserEnvironment;

/// <summary>
/// Configuração persistente de <c>DOTNET_ROOT</c> e <c>PATH</c> do usuário: perfis de shell no Linux e macOS; no
/// Windows, registro (<c>HKCU\Environment</c>) ou perfis do PowerShell (<c>--windows-env-target profile</c>). Planejar
/// nunca grava; executar falha fechado se o estado mudou.
/// </summary>
public interface IUserEnvironmentConfigurator
{
    string Home { get; }

    /// <summary>Script a carregar na sessão atual, ou nulo quando não há (registro do Windows).</summary>
    string? SessionScript { get; }

    EnvironmentPlan PlanApply(EnvironmentTarget target, EnvConflictPolicy policy);

    EnvironmentPlan PlanRemove();

    Task<EnvironmentApplyResult> ExecuteAsync(EnvironmentPlan plan, IDiagnosticSink diagnostics, CancellationToken cancellationToken);

    /// <summary>Raiz configurada pelo último <c>env apply</c>, ou nula se o ambiente não está configurado.</summary>
    string? ConfiguredRoot();
}
