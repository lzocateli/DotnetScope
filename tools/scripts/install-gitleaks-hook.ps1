<#
.SYNOPSIS
Instala o hook pre-commit obrigatorio de deteccao de secrets do repositorio.

.DESCRIPTION
Configura core.hooksPath como .githooks no clone Git informado e confirma que o
hook versionado esta disponivel. O hook executa exclusivamente a imagem
docker.io/lzocateli/gitleaks:8.30.1 por Docker ou Podman e analisa o diff staged
antes do commit. DOTNETSCOPE_CONTAINER_ENGINE força o engine (docker ou podman).

.PARAMETER RepositoryRoot
Raiz do clone Git. O padrao e dois niveis acima de tools/scripts.

.PARAMETER RemainingArguments
Argumentos literais remanescentes. Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/install-gitleaks-hook.ps1

.EXAMPLE
./tools/scripts/install-gitleaks-hook.ps1 -RepositoryRoot C:\src\meu-projeto

.INPUTS
Nenhum.

.OUTPUTS
Confirmacao do caminho de hooks configurado.

.NOTES
Requer PowerShell 7 ou superior, Git 2.9 ou superior e Docker 20.10 ou superior
ou Podman 4 ou superior.

.LINK
../../.github/SECRET-SCANNING.md
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),

    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

$ErrorActionPreference = 'Stop'

if ($RepositoryRoot -eq '--help' -or $RemainingArguments -contains '--help') {
    Get-Help $PSCommandPath -Full
    exit 0
}

if ($RemainingArguments.Count -gt 0) {
    [Console]::Error.WriteLine("Argumento desconhecido: $($RemainingArguments -join ' '). Use --help.")
    exit 2
}

$repositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$hookPath = Join-Path $repositoryRoot '.githooks/pre-commit'

if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot '.git'))) {
    [Console]::Error.WriteLine("O caminho nao e um clone Git: $repositoryRoot. Use --help.")
    exit 1
}

if (-not (Test-Path -LiteralPath $hookPath -PathType Leaf)) {
    [Console]::Error.WriteLine("Hook versionado ausente: $hookPath. Use --help.")
    exit 1
}

if ($null -eq (Get-Command git -ErrorAction SilentlyContinue)) {
    [Console]::Error.WriteLine('Git nao foi encontrado no PATH. Use --help.')
    exit 1
}

$containerEngine = $env:DOTNETSCOPE_CONTAINER_ENGINE
if ([string]::IsNullOrWhiteSpace($containerEngine)) {
    $containerEngine = @('docker', 'podman') |
        Where-Object { $null -ne (Get-Command $_ -ErrorAction SilentlyContinue) } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($containerEngine) -or $null -eq (Get-Command $containerEngine -ErrorAction SilentlyContinue)) {
    [Console]::Error.WriteLine('Docker ou Podman nao foi encontrado no PATH. Use --help.')
    exit 1
}

$nativeCommandPreference = $PSNativeCommandUseErrorActionPreference
$PSNativeCommandUseErrorActionPreference = $false
try {
    & git -C $repositoryRoot config --local core.hooksPath .githooks
    if ($LASTEXITCODE -ne 0) {
        throw "Git nao conseguiu configurar core.hooksPath (codigo $LASTEXITCODE)."
    }

    $configuredHooksPath = (& git -C $repositoryRoot config --local --get core.hooksPath | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $configuredHooksPath -ne '.githooks') {
        throw 'Nao foi possivel confirmar core.hooksPath=.githooks.'
    }
}
finally {
    $PSNativeCommandUseErrorActionPreference = $nativeCommandPreference
}

Write-Output "Hook Gitleaks instalado: core.hooksPath=$configuredHooksPath (engine: $containerEngine)"