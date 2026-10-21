<#
.SYNOPSIS
Executa os E2E do macOS (escopo user e escopo global pelo .pkg oficial), somente em runner efemero.

.DESCRIPTION
Usa o binario Native AOT publicado por Publish-Aot.ps1 (artifacts/publish/osx-<arch>/dotnetscope).

- user: tests/e2e/install-user-macos.sh como usuario comum, com HOME isolado em diretorio
  temporario: instala SDK e runtime, valida ~/.zprofile e shells de login zsh e bash, executa
  uma aplicacao de console e remove tudo, restaurando os perfis.
- global: tests/e2e/install-global-macos.sh como root (sudo -n): instala um runtime 9.0 para
  toda a maquina em /usr/local/share/dotnet pelo .pkg da Microsoft.

O cenario global altera a maquina; por isso o script recusa executar fora de um runner
hospedado pelo GitHub (GITHUB_ACTIONS=true e RUNNER_ENVIRONMENT=github-hosted), que e
descartado ao fim do job.

.PARAMETER Scenario
user, global ou all. Padrao: all.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/Publish-Aot.ps1 -Rid osx-arm64; ./tools/scripts/Invoke-MacE2E.ps1

.EXAMPLE
./tools/scripts/Invoke-MacE2E.ps1 -Scenario user

.NOTES
Requer PowerShell 7, macOS, sudo sem senha e acesso a builds.dotnet.microsoft.com.
Codigos: 0 sucesso; 1 cenario reprovado; 2 erro de uso ou ambiente nao efemero.

.LINK
../../tests/e2e/install-global-macos.sh
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('user', 'global', 'all')]
    [string] $Scenario = 'all',

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

if (-not $IsMacOS) {
    [Console]::Error.WriteLine('Este E2E exige macOS. Use --help.')
    exit 2
}

if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    [Console]::Error.WriteLine('Recusado: o E2E instala o .NET para toda a maquina e so executa em runner hospedado pelo GitHub. Use --help.')
    exit 2
}

$arch = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [Runtime.InteropServices.Architecture]::Arm64) { 'arm64' } else { 'x64' }
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$binary = Join-Path $repositoryRoot "artifacts/publish/osx-$arch/dotnetscope"
if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) {
    [Console]::Error.WriteLine("Binario nao encontrado: $binary. Execute Publish-Aot.ps1 -Rid osx-$arch. Use --help.")
    exit 2
}

if ($Scenario -in 'user', 'all') {
    & /bin/sh (Join-Path $repositoryRoot 'tests/e2e/install-user-macos.sh') $binary
    if ($LASTEXITCODE -ne 0) {
        exit 1
    }
}

if ($Scenario -in 'global', 'all') {
    & sudo -n /bin/sh (Join-Path $repositoryRoot 'tests/e2e/install-global-macos.sh') $binary
    if ($LASTEXITCODE -ne 0) {
        exit 1
    }
}
