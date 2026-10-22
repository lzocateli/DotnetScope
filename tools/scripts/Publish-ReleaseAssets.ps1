<#
.SYNOPSIS
Cria uma release ou completa assets ausentes sem substituir assets existentes.

.DESCRIPTION
Cria a release quando ela ainda não existe. Para releases existentes, compara
os SHA-256 locais com os digests da API; quando a API não fornece digest,
baixa o asset remoto para staging temporário e calcula o hash. Assets idênticos
são preservados, divergências falham antes de qualquer upload e somente assets
ausentes são enviados. Uma publicação parcial pode ser retomada com segurança.

.PARAMETER Tag
Tag da release, no formato vX.Y.Z ou vX.Y.Z-sufixo.

.PARAMETER ReleaseDirectory
Diretório dos assets a publicar. Padrão: dist.

.PARAMETER Repository
Repositório GitHub no formato owner/repo. Padrão: GITHUB_REPOSITORY.

.PARAMETER Prerelease
Cria a release como pré-release.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/Publish-ReleaseAssets.ps1 -Tag v1.0.0 -ReleaseDirectory dist -Repository lzocateli/DotnetScope

.NOTES
Requer PowerShell 7, GitHub CLI autenticada com permissão de escrita em releases
e SHA-256 para verificar assets existentes. Nunca usa --clobber.
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Tag,

    [string] $ReleaseDirectory = 'dist',

    [string] $Repository = $env:GITHUB_REPOSITORY,

    [switch] $Prerelease,

    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

$ErrorActionPreference = 'Stop'

if ($RemainingArguments -contains '--help') {
    Get-Help $PSCommandPath -Full
    exit 0
}

if ($RemainingArguments.Count -gt 0 -or [string]::IsNullOrWhiteSpace($Tag) -or [string]::IsNullOrWhiteSpace($Repository)) {
    [Console]::Error.WriteLine('Informe -Tag e -Repository. Use --help.')
    exit 2
}

$releasePath = [IO.Path]::GetFullPath($ReleaseDirectory, (Get-Location).Path)
if (-not (Test-Path -LiteralPath $releasePath -PathType Container)) {
    [Console]::Error.WriteLine("Diretório de assets não encontrado: $releasePath.")
    exit 2
}

$localAssets = @(
    Get-ChildItem -LiteralPath $releasePath -File |
        Sort-Object -Property Name |
        ForEach-Object {
            [pscustomobject]@{
                Name   = $_.Name
                Path   = $_.FullName
                Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
)
if ($localAssets.Count -eq 0) {
    [Console]::Error.WriteLine("Nenhum asset encontrado em $releasePath.")
    exit 2
}

function Invoke-Gh {
    param([string[]] $Arguments)

    $nativePreference = $PSNativeCommandUseErrorActionPreference
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        $output = @(& gh @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $PSNativeCommandUseErrorActionPreference = $nativePreference
    }

    [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
}

function Invoke-GhChecked {
    param([string[]] $Arguments)

    $result = Invoke-Gh $Arguments
    if ($result.ExitCode -ne 0) {
        $detail = ($result.Output | ForEach-Object { $_.ToString() }) -join ' '
        throw "gh $($Arguments[0]) falhou (código $($result.ExitCode)): $detail"
    }

    $result.Output
}

function Get-HttpResponseBody {
    param([string[]] $Lines)

    $separator = -1
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ([string]::IsNullOrWhiteSpace($Lines[$index])) {
            $separator = $index
            break
        }
    }

    if ($separator -lt 0 -or $separator -ge $Lines.Count - 1) {
        throw 'A API do GitHub retornou uma resposta sem corpo JSON.'
    }

    ($Lines[($separator + 1)..($Lines.Count - 1)] -join "`n")
}

$null = Invoke-GhChecked @('api', "repos/$Repository", '--silent')
$response = Invoke-Gh @('api', '--include', "repos/$Repository/releases/tags/$Tag")
$statusLine = $response.Output | Where-Object { $_ -match '^HTTP/\S+\s+([0-9]{3})\b' } | Select-Object -First 1
if (-not $statusLine) {
    $detail = ($response.Output | ForEach-Object { $_.ToString() }) -join ' '
    [Console]::Error.WriteLine("A API do GitHub não retornou status HTTP para a release: $detail")
    exit 1
}

$statusLine -match '^HTTP/\S+\s+([0-9]{3})\b' | Out-Null
$httpStatus = [int]$Matches[1]
$releaseExists = $httpStatus -eq 200
if (-not $releaseExists -and $httpStatus -ne 404) {
    $detail = ($response.Output | ForEach-Object { $_.ToString() }) -join ' '
    [Console]::Error.WriteLine("A consulta da release falhou com HTTP ${httpStatus}: $detail")
    exit 1
}

$remoteAssets = [System.Collections.Generic.List[object]]::new()
$temporaryRoot = $null
try {
    if ($releaseExists) {
        $release = Get-HttpResponseBody $response.Output | ConvertFrom-Json
        $localByName = @{}
        foreach ($asset in $localAssets) {
            $localByName[$asset.Name] = $true
        }

        foreach ($asset in $release.assets) {
            if (-not $localByName.ContainsKey($asset.name)) {
                continue
            }

            $sha256 = $null
            if ($asset.digest -match '^sha256:([0-9a-fA-F]{64})$') {
                $sha256 = $Matches[1].ToLowerInvariant()
            }
            else {
                if ([string]::IsNullOrWhiteSpace($asset.name) -or [IO.Path]::GetFileName($asset.name) -ne $asset.name) {
                    [Console]::Error.WriteLine('A API retornou um nome de asset inválido; nenhuma alteração foi feita.')
                    exit 1
                }

                if ($null -eq $temporaryRoot) {
                    $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "dotnetscope-release-assets-$([guid]::NewGuid().ToString('N'))"
                    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
                }

                $assetDirectory = Join-Path $temporaryRoot ([guid]::NewGuid().ToString('N'))
                [IO.Directory]::CreateDirectory($assetDirectory) | Out-Null
                $null = Invoke-GhChecked @('release', 'download', $Tag, '--repo', $Repository, '--pattern', $asset.name, '--dir', $assetDirectory)
                $downloadedPath = Join-Path $assetDirectory $asset.name
                if (-not (Test-Path -LiteralPath $downloadedPath -PathType Leaf)) {
                    [Console]::Error.WriteLine("A API não forneceu digest e o asset $($asset.name) não foi baixado; nenhuma alteração foi feita.")
                    exit 1
                }

                $sha256 = (Get-FileHash -LiteralPath $downloadedPath -Algorithm SHA256).Hash.ToLowerInvariant()
            }

            $remoteAssets.Add([pscustomobject]@{ Name = $asset.name; Sha256 = $sha256 })
        }
    }

    Import-Module (Join-Path $PSScriptRoot 'ReleaseGovernance.psm1') -Force
    $plan = Get-ReleaseAssetPublicationPlan -ReleaseExists $releaseExists -LocalAssets $localAssets -RemoteAssets @($remoteAssets)

    if ($plan.Action -eq 'Create') {
        $arguments = [System.Collections.Generic.List[string]]::new()
        $arguments.AddRange([string[]]@('release', 'create', $Tag))
        foreach ($asset in $plan.UploadAssets) {
            $arguments.Add($asset.Path)
        }

        $arguments.AddRange([string[]]@('--repo', $Repository, '--verify-tag', '--generate-notes', '--title', "DotnetScope $Tag"))
        if ($Prerelease) {
            $arguments.Add('--prerelease')
        }

        $null = Invoke-GhChecked $arguments.ToArray()
        Write-Output "Release $Tag criada com $($plan.UploadAssets.Count) asset(s)."
    }
    elseif ($plan.Action -eq 'NoOp') {
        Write-Output "Release $Tag já contém todos os assets idênticos; nenhuma alteração feita."
    }
    else {
        $arguments = [System.Collections.Generic.List[string]]::new()
        $arguments.AddRange([string[]]@('release', 'upload', $Tag))
        foreach ($asset in $plan.UploadAssets) {
            $arguments.Add($asset.Path)
        }

        $arguments.AddRange([string[]]@('--repo', $Repository))
        $null = Invoke-GhChecked $arguments.ToArray()
        Write-Output "Release $Tag completada com $($plan.UploadAssets.Count) asset(s) ausente(s), sem substituir os existentes."
    }
}
finally {
    if ($null -ne $temporaryRoot -and [IO.Directory]::Exists($temporaryRoot)) {
        [IO.Directory]::Delete($temporaryRoot, $true)
    }
}
