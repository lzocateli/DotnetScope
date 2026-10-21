<#
.SYNOPSIS
Executa os cenarios E2E de instalacao (escopos user e global) com o binario Native AOT em containers limpos.

.DESCRIPTION
Para cada imagem, inicia um container descartavel sem .NET, monta o binario
artifacts/aot/linux-x64/dotnetscope em /usr/local/bin e executa
tests/e2e/install-user.sh: instala dependencias nativas como root, confirma a
recusa do escopo user como root (codigo 7) e, como usuario comum, instala o SDK
real, cria e executa uma aplicacao de console, repete a instalacao
(idempotencia) e instala um runtime lado a lado.

O cenario global (tests/e2e/install-global.sh) roda em outro container limpo,
como root: recusas sem root e com raiz insegura, conflito de install_location,
instalacao em /usr/local/share/dotnet, shell de login de usuario comum,
inventario, doctor e reversao completa de /etc. O cenario apt
(tests/e2e/install-apt.sh) roda somente em imagens Ubuntu: --install-deps,
SDK e runtime pelas fontes apt configuradas, recusa da mistura com archive,
remocao pelo apt e ambiente adulterado (APT_CONFIG, PATH) sem efeito.

Baixa os artefatos oficiais (centenas de MiB por imagem). Variaveis de proxy do
host sao repassadas ao container pelo engine. O host nao e alterado.

.PARAMETER Image
Imagens a testar. Padrao: ubuntu 24.04 e fedora 41 (glibc >= 2.39, exigida pelo
binario publicado na imagem de build atual).

.PARAMETER Sdk
Especificacao do SDK. Padrao: lts.

.PARAMETER Runtime
Especificacao do runtime instalado lado a lado. Padrao: 9.0.

.PARAMETER Scenario
user, global, apt ou all. Padrao: all.

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

    [ValidateSet('user', 'global', 'apt', 'all')]
    [string] $Scenario = 'all',

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
$scenarios = switch ($Scenario) {
    'user' { @('install-user.sh') }
    'global' { @('install-global.sh') }
    'apt' { @('install-apt.sh') }
    default { @('install-user.sh', 'install-global.sh', 'install-apt.sh') }
}
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

# Um tz configurado no containers.conf do Podman monta /etc/localtime e quebra o pos-instalacao do tzdata.
$timezoneOverride = $null
if ($containerEngine -eq 'podman') {
    $timezoneOverride = Join-Path ([IO.Path]::GetTempPath()) "dotnetscope-e2e-$([Guid]::NewGuid().ToString('N')).conf"
    Set-Content -LiteralPath $timezoneOverride -Value "[containers]`ntz = `"`""
    $env:CONTAINERS_CONF_OVERRIDE = $timezoneOverride
}

$failures = @()
foreach ($name in $Image) {
    foreach ($script in $scenarios) {
        if ($script -eq 'install-apt.sh' -and $name -notmatch 'ubuntu') {
            continue
        }

        Write-Output "`n##### E2E $script em $name"
        $scenarioPath = Join-Path $repositoryRoot "tests/e2e/$script"
        & $containerEngine run --rm `
            --volume "${binary}:/usr/local/bin/dotnetscope:ro" `
            --volume "${scenarioPath}:/e2e/${script}:ro" `
            --env "E2E_SDK=$Sdk" `
            --env "E2E_RUNTIME=$Runtime" `
            $name sh "/e2e/$script"
        if ($LASTEXITCODE -ne 0) {
            $failures += "$script em $name (codigo $LASTEXITCODE)"
        }
    }
}

if ($null -ne $timezoneOverride) {
    Remove-Item -LiteralPath $timezoneOverride -Force
    Remove-Item Env:CONTAINERS_CONF_OVERRIDE
}

if ($failures.Count -gt 0) {
    [Console]::Error.WriteLine("E2E falhou em: $($failures -join ', ')")
    exit 1
}

Write-Output "`nE2E aprovado em: $($Image -join ', ')"
