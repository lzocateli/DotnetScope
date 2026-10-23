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

    # O post-release.yml roda no mesmo run do gw-release.yml disparado pelo push do merge do Release PR.
    if ($EventName -eq 'push' -and $Ref -ceq 'refs/heads/main') {
        return [pscustomobject]@{ Allowed = $true; Reason = 'chamada automática pelo post-release após a publicação da release' }
    }

    # O modo republish do gw-release.yml é executado na tag da release.
    if ($EventName -eq 'workflow_dispatch' -and $Ref -ceq "refs/tags/v$Version") {
        return [pscustomobject]@{ Allowed = $true; Reason = 'republish na tag correspondente' }
    }

    [pscustomobject]@{ Allowed = $false; Reason = 'submissão permitida somente por dispatch em main, pelo post-release em main ou pelo republish na tag vX.Y.Z correspondente' }
}

Export-ModuleMember -Function Test-WingetSubmissionSource
