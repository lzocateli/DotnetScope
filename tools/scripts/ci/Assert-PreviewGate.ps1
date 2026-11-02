#!/usr/bin/env pwsh
# Gerenciado pelo Altivy.Groundwork (componente release-preview). Não edite: mudanças vão para o catálogo.

<#
.SYNOPSIS
Preview gate do Release PR: exige candidata vX.Y.Z-rc.N validada no commit-base.

.DESCRIPTION
Executado no checkout da cabeça do Release PR (histórico completo). Fora do Release PR
(release-please--branches--<branch padrão>) ou com preview.requiredForRelease false, só
registra e passa. No Release PR, lê X.Y.Z do .release-please-manifest.json, usa como
commit-base o pai do commit do Release PR e passa quando:
- existe GitHub Release publicada vX.Y.Z-rc.N, marcada como prerelease, nesse commit, com o
  asset preview-acceptance.json de sucesso no Environment de preview; ou
- algum commit desde a última release estável tem o rodapé "Preview-Waiver: <justificativa>".
Somente leitura; requer GH_TOKEN com contents:read. Ver docs/estrategia-de-release.md.

.PARAMETER HeadRef
Branch do PR (github.head_ref).

.PARAMETER HeadCommit
Commit da cabeça do PR.

.PARAMETER Repository
Repositório owner/nome. Padrão: $env:GITHUB_REPOSITORY.

.PARAMETER DefaultBranch
Branch padrão do repositório.

.PARAMETER ConfigPath
Configuração do pipeline. Padrão: .github/ci-cd.json.

.PARAMETER SummaryPath
Arquivo de resumo do job (por exemplo $env:GITHUB_STEP_SUMMARY).

.EXAMPLE
./tools/scripts/ci/Assert-PreviewGate.ps1 -HeadRef release-please--branches--main -HeadCommit $sha -Repository minha-org/app

.EXAMPLE
./tools/scripts/ci/Assert-PreviewGate.ps1 --help

.NOTES
Requer PowerShell 7+, Git e GitHub CLI autenticado. Código de saída 0 liberado, 1 bloqueado ou erro, 2 argumento desconhecido.

.LINK
../../../docs/estrategia-de-release.md
#>
#Requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $HeadRef = '',
    [string] $HeadCommit = '',
    [string] $Repository = $env:GITHUB_REPOSITORY,
    [string] $DefaultBranch = 'main',
    [string] $ConfigPath = '.github/ci-cd.json',
    [string] $SummaryPath = '',
    [Alias('h')]
    [switch] $Help,
    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Help -or ($RemainingArguments | Where-Object { $_ -in @('--help', '-h', '/?', 'help') })) {
    Get-Help -Full $PSCommandPath
    exit 0
}

if ($RemainingArguments) {
    [Console]::Error.WriteLine("Argumento desconhecido: $($RemainingArguments -join ' '). Use --help.")
    exit 2
}

Import-Module (Join-Path $PSScriptRoot 'GroundworkRelease.psm1') -Force

function Write-Summary([string] $Text) {
    Write-Output $Text
    if ($SummaryPath) { "## Preview gate`n`n$Text" | Add-Content -LiteralPath $SummaryPath -Encoding utf8 }
}

try {
    if ($DefaultBranch -notmatch '^[A-Za-z0-9._/-]+$' -or $DefaultBranch.Contains('..')) { throw 'Branch padrão inválida.' }
    if ($HeadRef -cne "release-please--branches--$DefaultBranch") {
        Write-Output 'Não é o Release PR; o preview gate não se aplica.'
        exit 0
    }

    if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Repositório inválido.' }
    if ($HeadCommit -notmatch '^[0-9a-f]{40}$') { throw 'Commit do Release PR inválido.' }
    $policy = Get-PreviewPolicy -Config (Read-CiCdConfig -Path $ConfigPath)
    if (-not $policy.Required) {
        Write-Summary 'preview.requiredForRelease desligado em .github/ci-cd.json; o Release PR não exige candidata.'
        exit 0
    }

    $manifest = Get-Content -LiteralPath '.release-please-manifest.json' -Raw | ConvertFrom-Json -AsHashtable
    $version = [string]$manifest['.']
    if (-not (Test-SemVer $version) -or $version.Contains('-')) { throw "Versão do Release PR inválida: '$version'." }
    $base = (git rev-parse --verify --quiet "$HeadCommit^1" 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $base -notmatch '^[0-9a-f]{40}$') { throw 'Commit-base do Release PR indisponível; o checkout precisa de fetch-depth 0.' }

    $candidate = Find-ValidatedCandidate -Repository $Repository -Version $version -Environment $policy.Environment -Commit $base
    if ($null -ne $candidate) {
        Write-Summary "Liberado: $($candidate.Tag) validada em $($policy.Environment) em $($candidate.Acceptance['acceptedAt']), no commit-base $base."
        exit 0
    }

    $previous = (git describe --tags --abbrev=0 --match 'v[0-9]*' --exclude '*-*' $base 2>$null | Out-String).Trim()
    $waiver = Find-PreviewWaiver -Since $previous -Until $base
    if ($waiver) {
        Write-Output "::warning title=Preview gate::Liberado por waiver: $waiver"
        Write-Summary "Liberado por **waiver** (sem candidata validada): $waiver"
        exit 0
    }

    throw "Nenhuma candidata v$version-rc.N validada em $($policy.Environment) no commit-base $base. Dispare o workflow Release preview (gw-preview.yml) na branch $DefaultBranch, valide a candidata em $($policy.Environment) e reexecute este check; em emergência, use o rodapé Preview-Waiver."
}
catch {
    Write-Output "::error title=Preview gate::$($_.Exception.Message)"
    if ($SummaryPath) { "## Preview gate`n`nBloqueado: $($_.Exception.Message)" | Add-Content -LiteralPath $SummaryPath -Encoding utf8 }
    exit 1
}
