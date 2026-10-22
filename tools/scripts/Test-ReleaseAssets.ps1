<#
.SYNOPSIS
Testa o planejamento idempotente de publicação dos assets de release.

.DESCRIPTION
Usa fixtures sintéticas em memória para release inexistente, assets idênticos,
asset divergente e publicação parcial. Não acessa o GitHub nem altera releases.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/Test-ReleaseAssets.ps1
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

$ErrorActionPreference = 'Stop'

if ($RemainingArguments -contains '--help') {
    Get-Help $PSCommandPath -Full
    exit 0
}

if ($RemainingArguments.Count -gt 0) {
    [Console]::Error.WriteLine('Argumento desconhecido. Use --help.')
    exit 2
}

Import-Module (Join-Path $PSScriptRoot 'ReleaseGovernance.psm1') -Force
$hashA = 'a' * 64
$hashB = 'b' * 64
$local = @(
    [pscustomobject]@{ Name = 'one.tar.gz'; Path = '/fixture/one.tar.gz'; Sha256 = $hashA }
    [pscustomobject]@{ Name = 'two.spdx.json'; Path = '/fixture/two.spdx.json'; Sha256 = $hashB }
)

$newRelease = Get-ReleaseAssetPublicationPlan -ReleaseExists $false -LocalAssets $local
if ($newRelease.Action -ne 'Create' -or $newRelease.UploadAssets.Count -ne 2) {
    throw 'Release inexistente não planejou criação com todos os assets.'
}

$identical = Get-ReleaseAssetPublicationPlan -ReleaseExists $true -LocalAssets $local -RemoteAssets $local
if ($identical.Action -ne 'NoOp' -or $identical.UploadAssets.Count -ne 0) {
    throw 'Assets idênticos não produziram uma operação idempotente.'
}

$divergentRejected = $false
try {
    $divergent = @(
        $local[0]
        [pscustomobject]@{ Name = 'two.spdx.json'; Sha256 = $hashA }
    )
    Get-ReleaseAssetPublicationPlan -ReleaseExists $true -LocalAssets $local -RemoteAssets $divergent | Out-Null
}
catch [InvalidOperationException] {
    $divergentRejected = $true
}

if (-not $divergentRejected) {
    throw 'Asset divergente não foi recusado antes do upload.'
}

$partial = Get-ReleaseAssetPublicationPlan -ReleaseExists $true -LocalAssets $local -RemoteAssets @($local[0])
if ($partial.Action -ne 'UploadMissing' -or $partial.UploadAssets.Count -ne 1 -or $partial.UploadAssets[0].Name -ne 'two.spdx.json') {
    throw 'Release parcial não planejou somente o asset ausente.'
}

Write-Output 'Aprovado: criação, idempotência, divergência e recuperação parcial de assets.'
