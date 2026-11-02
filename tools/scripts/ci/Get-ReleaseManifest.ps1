#!/usr/bin/env pwsh
# Gerenciado pelo Altivy.Groundwork (componente deploy). Não edite: mudanças vão para o catálogo.

<#
.SYNOPSIS
Baixa e valida o release-manifest.json de uma GitHub Release publicada.

.DESCRIPTION
Localiza a release pela tag, recusa rascunhos (exceto com -AllowDraft), baixa o
asset release-manifest.json, valida o schema, a tag e, opcionalmente, o commit, e
grava o arquivo em OutputPath. Não verifica a atestação; isso é feito pelo guard.

.PARAMETER Tag
Tag da release.

.PARAMETER Repository
Repositório owner/nome. Padrão: $env:GITHUB_REPOSITORY.

.PARAMETER OutputPath
Arquivo de destino do manifesto.

.PARAMETER ExpectedCommit
SHA completo esperado para o commit da release.

.PARAMETER AllowDraft
Aceita release em rascunho (uso operacional de diagnóstico).

.EXAMPLE
./tools/scripts/ci/Get-ReleaseManifest.ps1 -Tag v1.4.0 -OutputPath artifacts/release-manifest.json

.EXAMPLE
./tools/scripts/ci/Get-ReleaseManifest.ps1 --help

.NOTES
Requer PowerShell 7+ e GitHub CLI autenticado. Código de saída 0 sucesso, 1 erro e 2 argumento desconhecido.

.LINK
../../../docs/runbooks/deploy-e-rollback.md
#>
#Requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Tag,
    [string] $Repository = $env:GITHUB_REPOSITORY,
    [string] $OutputPath = 'artifacts/release-manifest.json',
    [string] $ExpectedCommit = '',
    [switch] $AllowDraft,
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
    if (-not (Test-ReleaseTagName ($Tag ?? ''))) { throw "Tag inválida: '$Tag'." }
    if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Repositório inválido.' }
    $release = Find-GitHubRelease -Repository $Repository -Tag $Tag
    if ($null -eq $release) { throw "GitHub Release $Tag inexistente." }
    if ($release['draft'] -and -not $AllowDraft) { throw "GitHub Release $Tag ainda é rascunho; somente releases publicadas são implantáveis." }

    $destination = [System.IO.Path]::GetFullPath($OutputPath)
    if (-not (Save-ReleaseAsset -Repository $Repository -Release $release -Name 'release-manifest.json' -Destination $destination)) {
        throw "GitHub Release $Tag sem release-manifest.json."
    }

    # O arquivo mantém os bytes publicados, para que a verificação da atestação use o mesmo digest.
    $manifest = ConvertFrom-Json -InputObject ([System.IO.File]::ReadAllText($destination)) -AsHashtable -Depth 32
    Assert-ReleaseManifest -Manifest $manifest -ExpectedTag $Tag -ExpectedCommit $ExpectedCommit
    if ($manifest['repository'] -ne $Repository) { throw "release-manifest.json pertence a $($manifest['repository'])." }
    Write-Output "release-manifest.json de $Tag válido ($($manifest['artifacts'].Count) artefato(s), rollbackMinimumVersion $($manifest['rollbackMinimumVersion']))."
}
catch {
    Write-Output "::error title=Manifesto da release::$($_.Exception.Message)"
    exit 1
}

exit 0
