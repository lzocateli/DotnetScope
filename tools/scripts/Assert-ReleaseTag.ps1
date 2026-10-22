<#
.SYNOPSIS
Confere versão, profundidade do clone e ancestralidade da tag de release.

.DESCRIPTION
Recusa tags fora do formato vX.Y.Z[-sufixo], incompatíveis com VersionPrefix,
clones rasos, tags que não apontam para commit e commits fora da história de
origin/main. Exige a referência origin/main local; não busca nem altera refs.

.PARAMETER Tag
Nome da tag a validar.

.PARAMETER VersionPrefix
Valor de VersionPrefix em Directory.Build.props.

.PARAMETER RepositoryRoot
Raiz do clone Git. Padrão: diretório atual.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/Assert-ReleaseTag.ps1 -Tag v1.0.0 -VersionPrefix 1.0.0
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Tag,

    [string] $VersionPrefix,

    [string] $RepositoryRoot = (Get-Location).Path,

    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

$ErrorActionPreference = 'Stop'

if ($RemainingArguments -contains '--help') {
    Get-Help $PSCommandPath -Full
    exit 0
}

if ($RemainingArguments.Count -gt 0 -or [string]::IsNullOrWhiteSpace($Tag) -or [string]::IsNullOrWhiteSpace($VersionPrefix)) {
    [Console]::Error.WriteLine('Informe -Tag e -VersionPrefix. Use --help.')
    exit 2
}

Import-Module (Join-Path $PSScriptRoot 'ReleaseGovernance.psm1') -Force
try {
    $null = Get-ReleaseTagVersion -Tag $Tag -VersionPrefix $VersionPrefix
}
catch [InvalidOperationException] {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}

$repositoryPath = [IO.Path]::GetFullPath($RepositoryRoot)
if (-not (Test-Path -LiteralPath (Join-Path $repositoryPath '.git'))) {
    [Console]::Error.WriteLine("O caminho não é um clone Git: $repositoryPath.")
    exit 1
}

function Invoke-Git {
    param([string[]] $Arguments)

    $nativePreference = $PSNativeCommandUseErrorActionPreference
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        $output = @(& git -C $repositoryPath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $PSNativeCommandUseErrorActionPreference = $nativePreference
    }

    [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
}

$shallow = Invoke-Git @('rev-parse', '--is-shallow-repository')
if ($shallow.ExitCode -ne 0 -or $shallow.Output.Count -ne 1) {
    [Console]::Error.WriteLine('Não foi possível determinar se o clone Git é raso.')
    exit 1
}
if ($shallow.Output[0].ToString().Trim() -ne 'false') {
    [Console]::Error.WriteLine('Clone raso: não é possível provar a ancestralidade da tag em origin/main.')
    exit 1
}

$tagExpression = "$Tag^{commit}"
$tagCommit = Invoke-Git @('rev-parse', '--verify', '--quiet', $tagExpression)
if ($tagCommit.ExitCode -ne 0 -or $tagCommit.Output.Count -ne 1) {
    [Console]::Error.WriteLine("A tag $Tag não resolve para um commit.")
    exit 1
}

$mainCommit = Invoke-Git @('rev-parse', '--verify', '--quiet', 'refs/remotes/origin/main^{commit}')
if ($mainCommit.ExitCode -ne 0 -or $mainCommit.Output.Count -ne 1) {
    [Console]::Error.WriteLine('A referência origin/main não está disponível; a publicação foi recusada.')
    exit 1
}

$tagSha = $tagCommit.Output[0].ToString().Trim()
$mainSha = $mainCommit.Output[0].ToString().Trim()
$ancestry = Invoke-Git @('merge-base', '--is-ancestor', $tagSha, $mainSha)
if ($ancestry.ExitCode -eq 1) {
    [Console]::Error.WriteLine("O commit $tagSha da tag $Tag não pertence à história de origin/main.")
    exit 1
}
if ($ancestry.ExitCode -ne 0) {
    $detail = ($ancestry.Output | ForEach-Object { $_.ToString() }) -join ' '
    [Console]::Error.WriteLine("Não foi possível provar a ancestralidade de ${Tag}: $detail")
    exit 1
}

Write-Output "Tag $Tag válida: commit $tagSha pertence à história de origin/main ($mainSha)."
