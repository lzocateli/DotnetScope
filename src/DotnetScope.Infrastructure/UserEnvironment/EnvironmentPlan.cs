using DotnetScope.Core.UserEnvironment;

namespace DotnetScope.Infrastructure.UserEnvironment;

/// <summary>Alteração planejada em um perfil de shell.</summary>
/// <param name="Path">Caminho do perfil no home.</param>
/// <param name="WritePath">Arquivo efetivamente gravado (alvo final quando o perfil é link simbólico).</param>
/// <param name="Delete">O arquivo foi criado pelo DotnetScope e fica vazio após a remoção do bloco.</param>
public sealed record ProfileChange(
    string Path,
    string WritePath,
    bool IsSymlink,
    bool Exists,
    string NewContent,
    bool BlockInserted,
    bool AddedLeadingNewline,
    string? OriginalContent,
    bool Delete = false);

/// <summary>Arquivo próprio do DotnetScope a gravar (conteúdo) ou remover (nulo).</summary>
/// <param name="Original">Conteúdo no momento do plano (nulo se ausente), conferido antes de gravar quando informado.</param>
public sealed record OwnedFileChange(string Path, string? Content, string? Original = null, bool CheckOriginal = false);

/// <summary>Alteração planejada em uma variável de ambiente persistente (registro do usuário no Windows).</summary>
/// <param name="Display">Localização legível, por exemplo <c>HKCU\Environment\Path</c>.</param>
/// <param name="OldValue">Valor bruto atual (sem expansão), ou nulo se a variável não existe.</param>
/// <param name="NewValue">Valor bruto a gravar, ou nulo para remover a variável.</param>
public sealed record VariableChange(
    string Name,
    string Display,
    string? OldValue,
    bool OldExpandable,
    string? NewValue,
    bool NewExpandable);

public sealed record EnvironmentPlan(
    EnvironmentTarget? Target,
    IReadOnlyList<ProfileChange> Profiles,
    IReadOnlyList<OwnedFileChange> Files,
    IReadOnlyList<ThirdPartyAssignment> Conflicts,
    bool Skipped,
    IReadOnlyList<VariableChange>? Variables = null,
    IReadOnlyList<string>? Notices = null)
{
    public IReadOnlyList<VariableChange> ChangedVariables => Variables ?? [];

    /// <summary>Avisos do planejamento, por exemplo um host do PowerShell cuja política impede carregar perfis.</summary>
    public IReadOnlyList<string> Warnings => Notices ?? [];

    public bool HasChanges => Profiles.Count > 0 || Files.Count > 0 || ChangedVariables.Count > 0;

    /// <summary>Perfis, arquivos próprios e variáveis que o plano altera, na forma exibida ao usuário.</summary>
    public IReadOnlyList<string> ChangedTargets =>
        Profiles.Select(p => p.Path).Concat(Files.Select(f => f.Path)).Concat(ChangedVariables.Select(v => v.Display)).ToList();

    public bool RequiresConfirmation => Profiles.Any(p => p.IsSymlink);
}

public sealed record EnvironmentApplyResult(IReadOnlyList<string> ChangedFiles, IReadOnlyList<string> Backups);
