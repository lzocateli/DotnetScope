function Get-ReleaseTagVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Tag,

        [Parameter(Mandatory)]
        [string] $VersionPrefix
    )

    if ($Tag -notmatch '^v([0-9]+\.[0-9]+\.[0-9]+)(?:-([0-9A-Za-z][0-9A-Za-z.-]*))?$') {
        throw [InvalidOperationException]::new("Tag '$Tag' fora do formato vX.Y.Z[-sufixo].")
    }

    $version = $Matches[1]
    $suffix = $Matches[2]
    $expected = if ($suffix) { "$version-$suffix" } else { $version }
    if ($expected -ne $VersionPrefix) {
        throw [InvalidOperationException]::new("Tag '$Tag' difere de VersionPrefix $VersionPrefix.")
    }

    $version
}

function Get-ReleaseAssetPublicationPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [bool] $ReleaseExists,

        [Parameter(Mandatory)]
        [object[]] $LocalAssets,

        [object[]] $RemoteAssets = @()
    )

    if ($LocalAssets.Count -eq 0) {
        throw [InvalidOperationException]::new('Nenhum asset local foi informado.')
    }

    $localNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($asset in $LocalAssets) {
        if ([string]::IsNullOrWhiteSpace($asset.Name) -or [IO.Path]::GetFileName($asset.Name) -ne $asset.Name) {
            throw [InvalidOperationException]::new('Nome de asset local inválido.')
        }

        if (-not $localNames.Add($asset.Name)) {
            throw [InvalidOperationException]::new("Nome de asset local duplicado: $($asset.Name).")
        }

        if ($asset.Sha256 -notmatch '^[0-9a-fA-F]{64}$') {
            throw [InvalidOperationException]::new("SHA-256 local inválido para $($asset.Name).")
        }
    }

    if (-not $ReleaseExists) {
        if ($RemoteAssets.Count -gt 0) {
            throw [InvalidOperationException]::new('Uma release inexistente não pode ter assets remotos.')
        }

        return [pscustomobject]@{
            Action       = 'Create'
            UploadAssets = @($LocalAssets)
        }
    }

    $remoteByName = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($asset in $RemoteAssets) {
        if ([string]::IsNullOrWhiteSpace($asset.Name) -or [IO.Path]::GetFileName($asset.Name) -ne $asset.Name) {
            throw [InvalidOperationException]::new('Nome de asset remoto inválido.')
        }

        if ($remoteByName.ContainsKey($asset.Name)) {
            throw [InvalidOperationException]::new("Nome de asset remoto duplicado: $($asset.Name).")
        }

        if ($asset.Sha256 -notmatch '^[0-9a-fA-F]{64}$') {
            throw [InvalidOperationException]::new("SHA-256 remoto não verificado para $($asset.Name).")
        }

        $remoteByName.Add($asset.Name, $asset)
    }

    $missing = [System.Collections.Generic.List[object]]::new()
    foreach ($local in $LocalAssets) {
        if (-not $remoteByName.ContainsKey($local.Name)) {
            $missing.Add($local)
            continue
        }

        $remote = $remoteByName[$local.Name]
        if ($remote.Name -cne $local.Name) {
            throw [InvalidOperationException]::new("O nome remoto de $($local.Name) difere apenas em maiúsculas/minúsculas; publicação recusada.")
        }

        if ($remote.Sha256 -ine $local.Sha256) {
            throw [InvalidOperationException]::new("Asset existente divergente: $($local.Name). Nenhum asset foi enviado ou substituído.")
        }
    }

    [pscustomobject]@{
        Action       = if ($missing.Count -eq 0) { 'NoOp' } else { 'UploadMissing' }
        UploadAssets = @($missing)
    }
}

function Test-WingetSubmissionSource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $EventName,

        [Parameter(Mandatory)]
        [string] $Ref,

        [Parameter(Mandatory)]
        [string] $Version,

        [Parameter(Mandatory)]
        [bool] $Submit
    )

    if (-not $Submit) {
        return [pscustomobject]@{ Allowed = $true; Reason = 'submissão não solicitada' }
    }

    if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
        return [pscustomobject]@{ Allowed = $false; Reason = 'versão deve ser X.Y.Z sem sufixo' }
    }

    if ($EventName -eq 'workflow_dispatch' -and $Ref -eq 'refs/heads/main') {
        return [pscustomobject]@{ Allowed = $true; Reason = 'dispatch manual em main' }
    }

    if ($EventName -eq 'push' -and $Ref -ceq "refs/tags/v$Version") {
        return [pscustomobject]@{ Allowed = $true; Reason = 'chamada automática pela tag correspondente' }
    }

    [pscustomobject]@{ Allowed = $false; Reason = 'submissão permitida somente por dispatch em main ou evento push da tag vX.Y.Z correspondente' }
}

Export-ModuleMember -Function Get-ReleaseTagVersion, Get-ReleaseAssetPublicationPlan, Test-WingetSubmissionSource
