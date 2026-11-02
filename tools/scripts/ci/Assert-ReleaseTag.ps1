#!/usr/bin/env pwsh
# Gerenciado pelo Altivy.Groundwork (componente release). Não edite: mudanças vão para o catálogo.

<#
.SYNOPSIS
Valida a tag de release contra os arquivos de versão e a ancestralidade na branch padrão.

.DESCRIPTION
Executado no checkout da tag (histórico completo). Exige tag vX.Y.Z[-pre], todos os
arquivos de versionFiles (.github/ci-cd.json) e o .release-please-manifest.json com
a mesma versão, e o commit da tag como ancestral de origin/<branch padrão>.
Com -Candidate, exige tag vX.Y.Z-rc.N com versão maior que a dos arquivos de versão (a
branch padrão ainda tem a versão anterior) e mantém a exigência de ancestralidade.
Escreve tag, version e commit na saída indicada. Somente leitura; não acessa a rede.

.PARAMETER Tag
Tag da release, por exemplo v1.4.0.

.PARAMETER DefaultBranch
Branch padrão do repositório.

.PARAMETER Candidate
Valida uma tag de candidata (release preview) em vez de uma release estável.

.PARAMETER ConfigPath
Configuração do pipeline. Padrão: .github/ci-cd.json.

.PARAMETER OutputPath
Arquivo de saída no formato chave=valor (por exemplo $env:GITHUB_OUTPUT).

.EXAMPLE
./tools/scripts/ci/Assert-ReleaseTag.ps1 -Tag v1.4.0 -DefaultBranch main

.EXAMPLE
./tools/scripts/ci/Assert-ReleaseTag.ps1 -Tag v1.4.0-rc.1 -DefaultBranch main -Candidate

.EXAMPLE
./tools/scripts/ci/Assert-ReleaseTag.ps1 --help

.NOTES
Requer PowerShell 7+ e Git. Código de saída 0 válido, 1 inválido e 2 argumento desconhecido.

.LINK
../../../docs/runbooks/release.md
#>
#Requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Tag,
    [string] $DefaultBranch = 'main',
    [switch] $Candidate,
    [string] $ConfigPath = '.github/ci-cd.json',
    [string] $OutputPath = '',
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

try {
    if (-not (Test-ReleaseTagName ($Tag ?? ''))) { throw "Tag inválida: '$Tag'. Use vX.Y.Z[-pre]." }
    if ($DefaultBranch -notmatch '^[A-Za-z0-9._/-]+$' -or $DefaultBranch.Contains('..')) { throw 'Branch padrão inválida.' }
    $version = $Tag.Substring(1)
    $config = Read-CiCdConfig -Path $ConfigPath
    if ($Candidate) {
        $base = Get-CandidateBaseVersion $Tag
        foreach ($file in Get-ProjectVersionFiles -Config $config) {
            $found = Get-VersionFromFile -Path $file
            if ((Compare-SemVer $base $found) -le 0) { throw "$file contém $found; a candidata $Tag precisa ser de uma versão maior." }
        }
    }
    elseif (Test-CandidateTag $Tag) {
        throw "A tag $Tag é de candidata; releases estáveis usam vX.Y.Z."
    }
    else {
        foreach ($file in Get-ProjectVersionFiles -Config $config) {
            $found = Get-VersionFromFile -Path $file
            if ($found -ne $version) { throw "$file contém $found, mas a tag é $Tag." }
        }
    }

    if (-not $Candidate -and (Test-Path -LiteralPath '.release-please-manifest.json' -PathType Leaf)) {
        $manifest = Get-Content -LiteralPath '.release-please-manifest.json' -Raw | ConvertFrom-Json -AsHashtable
        if ([string]$manifest['.'] -ne $version) { throw ".release-please-manifest.json contém $($manifest['.']), mas a tag é $Tag." }
    }

    $commit = (git rev-parse --verify "refs/tags/$Tag^{commit}" 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw "Tag $Tag não encontrada no checkout." }
    # actions/checkout com fetch-depth 0 já traz refs/remotes/origin/*; sem credenciais persistidas não há fetch.
    git rev-parse --verify --quiet "refs/remotes/origin/$DefaultBranch" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "origin/$DefaultBranch ausente; faça checkout com fetch-depth 0." }
    git merge-base --is-ancestor $commit "refs/remotes/origin/$DefaultBranch"
    if ($LASTEXITCODE -ne 0) { throw "O commit da tag $Tag não pertence a origin/$DefaultBranch." }
}
catch {
    Write-Output "::error title=Tag de release::$($_.Exception.Message)"
    exit 1
}

Write-GitHubOutput -Values @{ tag = $Tag; version = $version; commit = $commit } -Path $OutputPath | Out-Null
Write-Output "Tag $Tag válida: versões consistentes e commit $commit em origin/$DefaultBranch."
exit 0
