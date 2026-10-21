using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;

namespace DotnetScope.Infrastructure.UserEnvironment;

/// <summary>
/// Configuração persistente de <c>DOTNET_ROOT</c> e <c>PATH</c> do usuário: perfis de shell no Linux e macOS,
/// registro (<c>HKCU\Environment</c>) no Windows. Planejar nunca grava; executar falha fechado se o estado mudou.
/// </summary>
public interface IUserEnvironmentConfigurator
{
    string Home { get; }

    /// <summary>Script a carregar na sessão atual (Linux e macOS), ou nulo quando não há (Windows).</summary>
    string? SessionScript { get; }

    EnvironmentPlan PlanApply(EnvironmentTarget target, EnvConflictPolicy policy);

    EnvironmentPlan PlanRemove();

    Task<EnvironmentApplyResult> ExecuteAsync(EnvironmentPlan plan, IDiagnosticSink diagnostics, CancellationToken cancellationToken);

    /// <summary>Raiz configurada pelo último <c>env apply</c>, ou nula se o ambiente não está configurado.</summary>
    string? ConfiguredRoot();
}
