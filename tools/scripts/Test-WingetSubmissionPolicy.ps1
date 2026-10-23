<#
.SYNOPSIS
Testa a matriz de origens permitidas para submissão winget.

.DESCRIPTION
Usa decisões sintéticas para post-release (push em main), republish na tag,
workflow_dispatch, PR e execução sem submissão. Não acessa o GitHub nem submete manifestos.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/Test-WingetSubmissionPolicy.ps1
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
$cases = @(
    @{ EventName = 'push'; Ref = 'refs/heads/main'; Version = '1.2.3'; Submit = $true; Expected = $true }
    @{ EventName = 'workflow_dispatch'; Ref = 'refs/heads/main'; Version = '1.2.3'; Submit = $true; Expected = $true }
    @{ EventName = 'workflow_dispatch'; Ref = 'refs/tags/v1.2.3'; Version = '1.2.3'; Submit = $true; Expected = $true }
    @{ EventName = 'workflow_dispatch'; Ref = 'refs/heads/main'; Version = '1.2.3'; Submit = $false; Expected = $true }
    @{ EventName = 'pull_request'; Ref = 'refs/pull/12/merge'; Version = '1.2.3'; Submit = $false; Expected = $true }
    @{ EventName = 'workflow_dispatch'; Ref = 'refs/heads/feature'; Version = '1.2.3'; Submit = $true; Expected = $false }
    @{ EventName = 'push'; Ref = 'refs/heads/feature'; Version = '1.2.3'; Submit = $true; Expected = $false }
    @{ EventName = 'push'; Ref = 'refs/tags/v1.2.3'; Version = '1.2.3'; Submit = $true; Expected = $false }
    @{ EventName = 'workflow_dispatch'; Ref = 'refs/tags/v1.2.4'; Version = '1.2.3'; Submit = $true; Expected = $false }
    @{ EventName = 'workflow_dispatch'; Ref = 'refs/heads/main'; Version = '1.2.3-rc.1'; Submit = $true; Expected = $false }
    @{ EventName = 'pull_request'; Ref = 'refs/pull/12/merge'; Version = '1.2.3'; Submit = $true; Expected = $false }
)

foreach ($case in $cases) {
    $actual = Test-WingetSubmissionSource `
        -EventName $case.EventName `
        -Ref $case.Ref `
        -Version $case.Version `
        -Submit $case.Submit
    if ($actual.Allowed -ne $case.Expected) {
        throw "Caso inesperado: $($case.EventName) $($case.Ref) submit=$($case.Submit): $($actual.Reason)."
    }
}

Write-Output 'Aprovado: post-release em main, republish na tag, dispatch manual, sem submissão, PR, origem inválida e recusa.'
