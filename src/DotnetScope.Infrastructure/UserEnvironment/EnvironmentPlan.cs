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
public sealed record OwnedFileChange(string Path, string? Content);

public sealed record EnvironmentPlan(
    EnvironmentTarget? Target,
    IReadOnlyList<ProfileChange> Profiles,
    IReadOnlyList<OwnedFileChange> Files,
    IReadOnlyList<ThirdPartyAssignment> Conflicts,
    bool Skipped)
{
    public bool HasChanges => Profiles.Count > 0 || Files.Count > 0;

    public bool RequiresConfirmation => Profiles.Any(p => p.IsSymlink);
}

public sealed record EnvironmentApplyResult(IReadOnlyList<string> ChangedFiles, IReadOnlyList<string> Backups);
