<#
.SYNOPSIS
Executa o E2E do escopo user no Windows com os artefatos oficiais, somente em runner efemero.

.DESCRIPTION
Roda tests/e2e/install-user.ps1 com o binario Native AOT publicado por
Invoke-Validation.ps1 -Native -Aot (artifacts/aot/win-<arch>/dotnetscope.exe).
O cenario altera HKCU\Environment, o Path da maquina (HKLM) e
%LOCALAPPDATA%\Microsoft\dotnet; por isso o script recusa executar fora de um
runner hospedado pelo GitHub (GITHUB_ACTIONS=true e RUNNER_ENVIRONMENT=github-hosted),
que e descartado ao fim do job. Em estacoes de desenvolvimento, use os testes de
integracao, que usam uma subchave isolada do registro.

.PARAMETER Sdk
Versao do SDK a instalar. Padrao: lts.

.PARAMETER Runtime
Versao do runtime a instalar. Padrao: 9.0.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/Invoke-WindowsE2E.ps1

.EXAMPLE
./tools/scripts/Invoke-WindowsE2E.ps1 -Sdk 10.0 -Runtime 8.0

.NOTES
Requer PowerShell 7, Windows, privilegio de administrador (Path da maquina) e
acesso a builds.dotnet.microsoft.com.
Codigos: 0 sucesso; 1 cenario reprovado; 2 erro de uso ou ambiente nao efemero.

.LINK
../../tests/e2e/install-user.ps1
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Sdk = 'lts',

    [string] $Runtime = '9.0',

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

if (-not $IsWindows) {
    [Console]::Error.WriteLine('Este E2E exige Windows. Use --help.')
    exit 2
}

if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    [Console]::Error.WriteLine('Recusado: o E2E altera o registro e o Path da maquina e so executa em runner hospedado pelo GitHub. Use --help.')
    exit 2
}

$arch = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [Runtime.InteropServices.Architecture]::Arm64) { 'arm64' } else { 'x64' }
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$binary = Join-Path $repositoryRoot "artifacts/aot/win-$arch/dotnetscope.exe"
if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) {
    [Console]::Error.WriteLine("Binario nao encontrado: $binary. Execute Invoke-Validation.ps1 -Native -Aot. Use --help.")
    exit 2
}

& (Join-Path $repositoryRoot 'tests/e2e/install-user.ps1') -Binary $binary -Sdk $Sdk -Runtime $Runtime
exit $(if ($?) { 0 } else { 1 })
