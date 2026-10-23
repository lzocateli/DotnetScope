<#
.SYNOPSIS
Valida a origem de uma submissão ao winget antes de acessar o Environment.

.DESCRIPTION
Permite submit=true somente em workflow_dispatch executado em main, no evento
push em main do gw-release.yml (post-release, depois que a release foi publicada)
ou no workflow_dispatch do modo republish na tag vX.Y.Z correspondente à versão
do manifesto. Se submit=false, nenhuma submissão é autorizada.

.PARAMETER EventName
Nome do evento GitHub Actions.

.PARAMETER Ref
Referência completa do evento, como refs/heads/main ou refs/tags/v1.0.0.

.PARAMETER Version
Versão X.Y.Z do manifesto, sem prefixo v.

.PARAMETER Submit
Indica se a submissão remota foi solicitada.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/Assert-WingetSubmissionSource.ps1 -EventName workflow_dispatch -Ref refs/heads/main -Version 1.0.0 -Submit
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $EventName,

    [string] $Ref,

    [string] $Version,

    [switch] $Submit,

    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

$ErrorActionPreference = 'Stop'

if ($RemainingArguments -contains '--help') {
    Get-Help $PSCommandPath -Full
    exit 0
}

if ($RemainingArguments.Count -gt 0 -or [string]::IsNullOrWhiteSpace($EventName) -or [string]::IsNullOrWhiteSpace($Ref)) {
    [Console]::Error.WriteLine('Informe -EventName e -Ref. Use --help.')
    exit 2
}

Import-Module (Join-Path $PSScriptRoot 'ReleaseGovernance.psm1') -Force
$decision = Test-WingetSubmissionSource -EventName $EventName -Ref $Ref -Version $Version -Submit $Submit.IsPresent
if (-not $decision.Allowed) {
    [Console]::Error.WriteLine("Origem de submissão recusada: $($decision.Reason).")
    exit 1
}

Write-Output "Origem de submissão validada: $($decision.Reason)."
