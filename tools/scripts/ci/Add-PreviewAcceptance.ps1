#!/usr/bin/env pwsh
# Gerenciado pelo Altivy.Groundwork (componente release-preview). Não edite: mudanças vão para o catálogo.

<#
.SYNOPSIS
Registra o aceite de uma candidata no Environment de preview e reavalia o Release PR.

.DESCRIPTION
Chamado pelo gw-deploy.yml depois do deploy (ou aceite) com sucesso. Para tag de candidata
vX.Y.Z-rc.N no Environment de preview (deploy.previewEnvironment), anexa à GitHub Release o
asset preview-acceptance.json (schemaVersion, tag, versão, commit, Environment, resultado,
instante, ator e execução) e reexecuta a última execução do workflow CI do Release PR
aberto, para o preview gate ler o aceite. Em qualquer outra tag ou Environment, não faz nada.
Requer GH_TOKEN com contents:write e actions:write. Ver docs/estrategia-de-release.md.

.PARAMETER Tag
Tag implantada.

.PARAMETER Environment
Environment do deploy.

.PARAMETER Repository
Repositório owner/nome. Padrão: $env:GITHUB_REPOSITORY.

.PARAMETER DefaultBranch
Branch padrão do repositório.

.PARAMETER RunUrl
URL da execução do deploy.

.PARAMETER Actor
Quem disparou ou aprovou o deploy.

.PARAMETER ConfigPath
Configuração do pipeline. Padrão: .github/ci-cd.json.

.EXAMPLE
./tools/scripts/ci/Add-PreviewAcceptance.ps1 -Tag v1.4.0-rc.1 -Environment quality -Repository minha-org/app -RunUrl $url -Actor $env:GITHUB_ACTOR

.EXAMPLE
./tools/scripts/ci/Add-PreviewAcceptance.ps1 --help

.NOTES
Requer PowerShell 7+, Git e GitHub CLI autenticado. Código de saída 0 sucesso ou não aplicável, 1 erro, 2 argumento desconhecido.

.LINK
../../../docs/estrategia-de-release.md
#>
#Requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Tag,
    [string] $Environment,
    [string] $Repository = $env:GITHUB_REPOSITORY,
    [string] $DefaultBranch = 'main',
    [string] $RunUrl = '',
    [string] $Actor = $env:GITHUB_ACTOR,
    [string] $ConfigPath = '.github/ci-cd.json',
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
    $policy = Get-PreviewPolicy -Config (Read-CiCdConfig -Path $ConfigPath)
    if (-not (Test-CandidateTag ($Tag ?? '')) -or $Environment -cne $policy.Environment) {
        Write-Output "Sem aceite a registrar: $Tag em $Environment não é candidata no Environment de preview ($($policy.Environment))."
        exit 0
    }

    if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Repositório inválido.' }
    if ($DefaultBranch -notmatch '^[A-Za-z0-9._/-]+$' -or $DefaultBranch.Contains('..')) { throw 'Branch padrão inválida.' }
    $commit = Get-TagCommit $Tag
    if (-not $commit) { throw "Tag $Tag ausente no checkout." }
    $release = Find-GitHubRelease -Repository $Repository -Tag $Tag
    if ($null -eq $release -or $release['draft'] -or -not $release['prerelease']) { throw "A candidata $Tag não está publicada como prerelease." }

    $acceptance = [ordered]@{
        schemaVersion = 1
        tag           = $Tag
        version       = $Tag.Substring(1)
        commit        = $commit
        environment   = $Environment
        result        = 'success'
        acceptedAt    = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        actor         = [string]$Actor
        run           = [string]$RunUrl
    }
    $directory = Join-Path ([System.IO.Path]::GetTempPath()) "preview-acceptance-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $directory | Out-Null
    try {
        $file = Join-Path $directory 'preview-acceptance.json'
        [System.IO.File]::WriteAllText($file, (ConvertTo-Json -InputObject $acceptance -Depth 4))
        & gh release upload $Tag $file --clobber --repo $Repository | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Falha ao anexar preview-acceptance.json a $Tag." }
    }
    finally {
        Remove-Item -LiteralPath $directory -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Output "Aceite de $Tag em $Environment registrado."

    $owner = $Repository.Split('/')[0]
    $pulls = @(Invoke-GhJson -Path "repos/$Repository/pulls?state=open&head=${owner}:release-please--branches--$DefaultBranch&per_page=10" | Where-Object { $_ })
    if ($pulls.Count -ne 1) {
        Write-Output 'Nenhum Release PR aberto para reavaliar.'
        exit 0
    }

    $head = [string]$pulls[0]['head']['sha']
    $runs = Invoke-GhJson -Path "repos/$Repository/actions/runs?head_sha=$head&event=pull_request&per_page=20"
    $run = @($runs['workflow_runs'] ?? @()) | Where-Object { $_['name'] -ceq 'CI' } | Select-Object -First 1
    if ($null -eq $run) {
        Write-Output "Nenhuma execução CI do Release PR #$($pulls[0]['number']) para reexecutar."
    }
    elseif ($run['status'] -ne 'completed') {
        Write-Output "CI do Release PR #$($pulls[0]['number']) ainda em execução; o gate lerá o aceite ao terminar só se for reexecutado depois."
    }
    else {
        Invoke-GhJson -Path "repos/$Repository/actions/runs/$($run['id'])/rerun" -Method POST -Body @{} | Out-Null
        Write-Output "CI do Release PR #$($pulls[0]['number']) reexecutado para reavaliar o preview gate."
    }
}
catch {
    Write-Output "::error title=Aceite da candidata::$($_.Exception.Message)"
    exit 1
}

exit 0
