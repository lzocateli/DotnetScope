#!/usr/bin/env pwsh
# Gerenciado pelo Altivy.Groundwork (componente publish-github-assets). Não edite: mudanças vão para o catálogo.

<#
.SYNOPSIS
Anexa arquivos à GitHub Release de forma idempotente por SHA-256.

.DESCRIPTION
Para cada arquivo do diretório: se a release já possui asset com o mesmo nome, ele
não é reenviado nem substituído e a entrada do manifesto usa o SHA-256 publicado
(builds de republish podem não ser reprodutíveis byte a byte); se ausente, envia
com 'gh release upload'. Grava as entradas do manifesto da release. Nunca usa
--clobber.

.PARAMETER Tag
Tag da release.

.PARAMETER Path
Diretório com os arquivos a anexar (sem subdiretórios).

.PARAMETER Repository
Repositório owner/nome. Padrão: $env:GITHUB_REPOSITORY.

.PARAMETER EntriesPath
Arquivo JSON de saída com as entradas do manifesto.

.PARAMETER DryRun
Mostra o que seria enviado sem alterar a release.

.EXAMPLE
./tools/scripts/ci/Publish-ReleaseAssets.ps1 -Tag v1.4.0 -Path artifacts/release-assets -DryRun

.EXAMPLE
./tools/scripts/ci/Publish-ReleaseAssets.ps1 --help

.NOTES
Requer PowerShell 7+ e GitHub CLI autenticado (GH_TOKEN com contents:write). Código de saída 0 sucesso, 1 erro e 2 argumento desconhecido.

.LINK
../../../docs/runbooks/release.md
#>
#Requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Tag,
    [string] $Path = 'artifacts/release-assets',
    [string] $Repository = $env:GITHUB_REPOSITORY,
    [string] $EntriesPath = 'artifacts/release-entries/assets.json',
    [switch] $DryRun,
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
    $files = @(Get-ChildItem -LiteralPath $Path -File | Sort-Object Name)
    if ($files.Count -eq 0) { throw "Nenhum arquivo em $Path." }
    if (@(Get-ChildItem -LiteralPath $Path -Directory).Count -gt 0) { throw "$Path contém subdiretórios; envie arquivos planos." }

    $release = Find-GitHubRelease -Repository $Repository -Tag $Tag
    if ($null -eq $release) { throw "Release $Tag inexistente." }
    $assets = @{}
    foreach ($asset in @($release['assets'] ?? @())) { $assets[$asset['name']] = $asset }

    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($file in $files) {
        if ($file.Name -eq 'release-manifest.json') { throw 'release-manifest.json é reservado ao job de finalização.' }
        $digest = 'sha256:' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($assets.ContainsKey($file.Name)) {
            $published = [string]($assets[$file.Name]['digest'] ?? '')
            if (-not $published) {
                $temporary = New-TemporaryFile
                try {
                    Save-ReleaseAsset -Repository $Repository -Release $release -Name $file.Name -Destination $temporary.FullName | Out-Null
                    $published = 'sha256:' + (Get-FileHash -LiteralPath $temporary.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                }
                finally {
                    Remove-Item -LiteralPath $temporary.FullName -Force -ErrorAction SilentlyContinue
                }
            }

            if ($published -ne $digest) {
                Write-Output "::warning title=Assets da release::$($file.Name) já publicado com outro SHA-256; o asset publicado é mantido e registrado no manifesto."
            }

            Write-Output "$($file.Name) já publicado; não reenviado."
            $digest = $published
        }
        elseif ($DryRun) {
            Write-Output "[dry-run] enviaria $($file.Name)."
        }
        else {
            & gh release upload $Tag $file.FullName --repo $Repository | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Falha ao anexar $($file.Name)." }
            Write-Output "$($file.Name) anexado a $Tag."
        }

        $entries.Add([ordered]@{ kind = 'asset'; name = $file.Name; reference = "$Repository@$Tag/$($file.Name)"; digest = $digest })
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent ([System.IO.Path]::GetFullPath($EntriesPath))) | Out-Null
    ConvertTo-Json -InputObject $entries.ToArray() -Depth 4 | Set-Content -LiteralPath $EntriesPath -Encoding utf8NoBOM
}
catch {
    Write-Output "::error title=Assets da release::$($_.Exception.Message)"
    exit 1
}

exit 0
