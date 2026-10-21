<#
.SYNOPSIS
Executa o cenario E2E de instalacao no escopo user com o binario Native AOT em containers limpos.

.DESCRIPTION
Para cada imagem, inicia um container descartavel sem .NET, monta o binario
artifacts/aot/linux-x64/dotnetscope em /usr/local/bin e executa
tests/e2e/install-user.sh: instala dependencias nativas como root, confirma a
recusa do escopo user como root (codigo 7) e, como usuario comum, instala o SDK
real, cria e executa uma aplicacao de console, repete a instalacao
(idempotencia) e instala um runtime lado a lado.

Baixa os artefatos oficiais (centenas de MiB por imagem). Variaveis de proxy do
host sao repassadas ao container pelo engine. O host nao e alterado.

.PARAMETER Image
Imagens a testar. Padrao: ubuntu 24.04 e fedora 41 (glibc >= 2.39, exigida pelo
binario publicado na imagem de build atual).

.PARAMETER Sdk
Especificacao do SDK. Padrao: lts.

.PARAMETER Runtime
Especificacao do runtime instalado lado a lado. Padrao: 9.0.

.PARAMETER Engine
docker ou podman. Padrao: DOTNETSCOPE_CONTAINER_ENGINE, depois Docker e Podman.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
pwsh ./tools/scripts/Invoke-Validation.ps1 -Aot; pwsh ./tools/scripts/Invoke-E2E.ps1

.EXAMPLE
./tools/scripts/Invoke-E2E.ps1 -Image docker.io/library/ubuntu:24.04 -Sdk 10.0.4xx

.NOTES
Requer PowerShell 7+, Docker ou Podman e o binario gerado por Invoke-Validation.ps1 -Aot.
Codigos: 0 sucesso; 2 erro de uso; demais valores vem do cenario que falhou.

.LINK
../../docs/plano-implementacao.md
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string[]] $Image = @('docker.io/library/ubuntu:24.04', 'registry.fedoraproject.org/fedora:41'),

    [string] $Sdk = 'lts',

    [string] $Runtime = '9.0',

    [ValidateSet('docker', 'podman')]
    [string] $Engine,

    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

$ErrorActionPreference = 'Stop'

if ($RemainingArguments -contains '--help') {
    Get-Help $PSCommandPath -Full
    exit 0
}

if ($RemainingArguments.Count -gt 0) {
    [Console]::Error.WriteLine("Argumento desconhecido: $($RemainingArguments -join ' '). Use --help.")
    exit 2
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$binary = Join-Path $repositoryRoot 'artifacts/aot/linux-x64/dotnetscope'
$scenario = Join-Path $repositoryRoot 'tests/e2e/install-user.sh'
if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) {
    [Console]::Error.WriteLine("Binario nao encontrado: $binary. Execute Invoke-Validation.ps1 -Aot. Use --help.")
    exit 2
}

$containerEngine = $Engine
if ([string]::IsNullOrWhiteSpace($containerEngine)) {
    $containerEngine = $env:DOTNETSCOPE_CONTAINER_ENGINE
}
if ([string]::IsNullOrWhiteSpace($containerEngine)) {
    $containerEngine = @('docker', 'podman') |
        Where-Object { $null -ne (Get-Command $_ -ErrorAction SilentlyContinue) } |
        Select-Object -First 1
}
if ($containerEngine -notin @('docker', 'podman') -or $null -eq (Get-Command $containerEngine -ErrorAction SilentlyContinue)) {
    [Console]::Error.WriteLine('Docker ou Podman nao foi encontrado no PATH. Use --help.')
    exit 2
}

$failures = @()
foreach ($name in $Image) {
    Write-Output "`n##### E2E em $name"
    & $containerEngine run --rm `
        --volume "${binary}:/usr/local/bin/dotnetscope:ro" `
        --volume "${scenario}:/e2e/install-user.sh:ro" `
        --env "E2E_SDK=$Sdk" `
        --env "E2E_RUNTIME=$Runtime" `
        $name sh /e2e/install-user.sh
    if ($LASTEXITCODE -ne 0) {
        $failures += "$name (codigo $LASTEXITCODE)"
    }
}

if ($failures.Count -gt 0) {
    [Console]::Error.WriteLine("E2E falhou em: $($failures -join ', ')")
    exit 1
}

Write-Output "`nE2E aprovado em: $($Image -join ', ')"
