#!/usr/bin/env pwsh
# Gerenciado pelo Altivy.Groundwork (componente release-preview). Não edite: mudanças vão para o catálogo.

<#
.SYNOPSIS
Calcula a próxima candidata vX.Y.Z-rc.N a partir do Release PR aberto.

.DESCRIPTION
Lê a versão X.Y.Z do .release-please-manifest.json na cabeça do Release PR
(release-please--branches--<branch padrão>), exige que o Release PR esteja sobre o
commit informado (o HEAD da branch padrão no disparo) e escolhe N como o próximo
número entre as tags vX.Y.Z-rc.*. Grava as notas da candidata (corpo do Release PR)
em -NotesPath. Somente leitura no GitHub; requer GH_TOKEN com contents:read e
pull-requests:read. Ver docs/estrategia-de-release.md.

.PARAMETER Repository
Repositório owner/nome. Padrão: $env:GITHUB_REPOSITORY.

.PARAMETER DefaultBranch
Branch padrão do repositório.

.PARAMETER Commit
Commit da branch padrão em que a candidata será marcada (github.sha no disparo).

.PARAMETER ConfigPath
Configuração do pipeline. Padrão: .github/ci-cd.json.

.PARAMETER NotesPath
Arquivo em que as notas da candidata são gravadas.

.PARAMETER OutputPath
Arquivo de saída no formato chave=valor (por exemplo $env:GITHUB_OUTPUT).

.EXAMPLE
./tools/scripts/ci/Get-PreviewVersion.ps1 -Repository minha-org/app -DefaultBranch main -Commit $env:GITHUB_SHA -NotesPath notes.md

.EXAMPLE
./tools/scripts/ci/Get-PreviewVersion.ps1 --help

.NOTES
Requer PowerShell 7+ e GitHub CLI autenticado. Código de saída 0 sucesso, 1 recusa ou erro, 2 argumento desconhecido.

.LINK
../../../docs/estrategia-de-release.md
#>
#Requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Repository = $env:GITHUB_REPOSITORY,
    [string] $DefaultBranch = 'main',
    [string] $Commit = '',
    [string] $ConfigPath = '.github/ci-cd.json',
    [string] $NotesPath = '',
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
    if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Repositório inválido.' }
    if ($DefaultBranch -notmatch '^[A-Za-z0-9._/-]+$' -or $DefaultBranch.Contains('..')) { throw 'Branch padrão inválida.' }
    if ($Commit -notmatch '^[0-9a-f]{40}$') { throw 'Commit inválido; informe o SHA completo da branch padrão.' }

    $policy = Get-PreviewPolicy -Config (Read-CiCdConfig -Path $ConfigPath)
    if (-not $policy.Enabled) { throw 'preview.enabled é false em .github/ci-cd.json; candidatas desligadas.' }

    $owner = $Repository.Split('/')[0]
    $branch = "release-please--branches--$DefaultBranch"
    $pulls = @(Invoke-GhJson -Path "repos/$Repository/pulls?state=open&head=${owner}:$branch&per_page=10")
    $pulls = @($pulls | Where-Object { $_ })
    if ($pulls.Count -ne 1) { throw "Nenhum Release PR aberto ($branch): não há versão a pré-lançar." }
    $pull = $pulls[0]
    $head = [string]$pull['head']['sha']

    $headCommit = Invoke-GhJson -Path "repos/$Repository/commits/$head"
    $parents = @($headCommit['parents'] | ForEach-Object { [string]$_['sha'] })
    if ($parents.Count -ne 1 -or $parents[0] -ne $Commit) {
        throw "O Release PR #$($pull['number']) não está sobre $Commit; aguarde o release-please atualizá-lo e dispare de novo."
    }

    $content = Invoke-GhJson -Path "repos/$Repository/contents/.release-please-manifest.json?ref=$head"
    $encoded = ([string]$content['content']) -replace '\s', ''
    $manifest = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded)) | ConvertFrom-Json -AsHashtable
    $version = [string]$manifest['.']
    if (-not (Test-SemVer $version) -or $version.Contains('-')) { throw "Versão do Release PR inválida: '$version'." }

    $refs = @(Invoke-GhJson -Path "repos/$Repository/git/matching-refs/tags/v$version-rc." | Where-Object { $_ })
    $numbers = @($refs | ForEach-Object { ([string]$_['ref']).Substring('refs/tags/'.Length) } | Where-Object { Test-CandidateTag $_ } |
        ForEach-Object { [int]($_.Split('.')[-1]) })
    $next = $numbers.Count -eq 0 ? 1 : (($numbers | Measure-Object -Maximum).Maximum + 1)
    $tag = "v$version-rc.$next"

    if ($NotesPath) {
        $body = [string]($pull['body'] ?? '')
        $notes = "> Candidata $tag da versão $version, para validação em $($policy.Environment). Não é a versão estável.`n`n$body"
        [System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($NotesPath), $notes)
    }
}
catch {
    Write-Output "::error title=Candidata::$($_.Exception.Message)"
    exit 1
}

Write-GitHubOutput -Path $OutputPath -Values @{
    tag          = $tag
    version      = $tag.Substring(1)
    base_version = $version
    commit       = $Commit
    release_pr   = [string]$pull['number']
    environment  = $policy.Environment
} | Out-Null
Write-Output "Candidata $tag no commit $Commit (Release PR #$($pull['number']))."
exit 0
