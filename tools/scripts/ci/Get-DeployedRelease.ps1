#!/usr/bin/env pwsh
# Gerenciado pelo Altivy.Groundwork (componente deploy). Não edite: mudanças vão para o catálogo.

<#
.SYNOPSIS
Informa a última release implantada com sucesso em um Environment.

.DESCRIPTION
Consulta a API de deployments do GitHub para o Environment (mais recentes primeiro)
e devolve a tag do primeiro deployment cujo status mais recente é success e cuja
ref é uma tag de release. Usado pela trava de rollback e pelo runbook.
Somente leitura.

.PARAMETER Environment
Nome do Environment de deploy.

.PARAMETER Repository
Repositório owner/nome. Padrão: $env:GITHUB_REPOSITORY.

.PARAMETER OutputPath
Arquivo de saída chave=valor (tag e version; vazios quando não houver deploy).

.EXAMPLE
./tools/scripts/ci/Get-DeployedRelease.ps1 -Environment production

.EXAMPLE
./tools/scripts/ci/Get-DeployedRelease.ps1 --help

.NOTES
Requer PowerShell 7+ e GitHub CLI autenticado (deployments:read). Código de saída 0 sucesso, 1 erro e 2 argumento desconhecido.

.LINK
../../../docs/runbooks/deploy-e-rollback.md
#>
#Requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Environment,
    [string] $Repository = $env:GITHUB_REPOSITORY,
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
    if ($Environment -notmatch '^[A-Za-z0-9._-]{1,255}$') { throw "Environment inválido: '$Environment'." }
    if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Repositório inválido.' }

    $tag = ''
    $deployments = Invoke-GhJson -Path "repos/$Repository/deployments?environment=$([uri]::EscapeDataString($Environment))&per_page=100"
    foreach ($deployment in @($deployments ?? @())) {
        if (-not (Test-ReleaseTagName ([string]$deployment['ref']))) { continue }
        $statuses = Invoke-GhJson -Path "repos/$Repository/deployments/$($deployment['id'])/statuses?per_page=1"
        $latest = @($statuses ?? @()) | Select-Object -First 1
        if ($null -ne $latest -and $latest['state'] -eq 'success') {
            # Um deployment bem-sucedido posterior torna os anteriores 'inactive'; o primeiro 'success' é o atual.
            $tag = [string]$deployment['ref']
            break
        }
    }

    Write-GitHubOutput -Path $OutputPath -Values @{ tag = $tag; version = ($tag ? $tag.Substring(1) : '') }
}
catch {
    Write-Output "::error title=Deploy atual::$($_.Exception.Message)"
    exit 1
}

exit 0
