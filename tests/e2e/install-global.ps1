<#
.SYNOPSIS
Cenario E2E do escopo global no Windows: instalador oficial .exe com SHA-512 e Authenticode, e remocao.

.DESCRIPTION
Executado por tools/scripts/Invoke-WindowsE2E.ps1 somente em runner efemero e elevado, porque instala um
runtime para toda a maquina em %ProgramFiles%\dotnet. Escolhe um runtime 9.0 ainda ausente, instala com
--scope global, valida o host da maquina, o estado protegido em ProgramData e a idempotencia, e remove com
o mesmo instalador (/uninstall).

.PARAMETER Binary
Caminho do dotnetscope.exe publicado.

.NOTES
Codigos: 0 sucesso; 1 falha.
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory)]
    [string] $Binary
)

$ErrorActionPreference = 'Stop'
$root = Join-Path $env:ProgramFiles 'dotnet'
$state = Join-Path $env:ProgramData 'dotnetscope'

function Fail([string] $Message) { throw "FALHA: $Message" }
function Pass([string] $Message) { Write-Output "ok: $Message" }

function Invoke-Scope([string[]] $Arguments, [int] $Expected = 0) {
    $errorFile = [IO.Path]::GetTempFileName()
    $output = & $Binary @Arguments 2> $errorFile
    $code = $LASTEXITCODE
    $stderr = Get-Content -LiteralPath $errorFile -Raw
    Remove-Item -LiteralPath $errorFile -Force
    if ($code -ne $Expected) {
        Fail "dotnetscope $($Arguments -join ' '): codigo $code, esperado $Expected`n$($output -join "`n")`n$stderr"
    }
    return ($output -join "`n")
}

$null = Invoke-Scope @('install', 'runtime', '9.0', '--scope', 'global', '--source', 'archive') -Expected 2
Pass '--source archive e recusado no Windows'

$version = @('9.0.1', '9.0.2', '9.0.3', '9.0.4') |
    Where-Object { -not (Test-Path -LiteralPath (Join-Path $root "shared\Microsoft.NETCore.App\$_")) } |
    Select-Object -First 1
if (-not $version) { Fail 'nenhum runtime 9.0 ausente disponivel para o teste' }
Write-Output "Runtime escolhido: $version"

$install = Invoke-Scope @('install', 'runtime', $version, '--scope', 'global', '--yes', '--verbose', '--output', 'json') | ConvertFrom-Json
if ($install.status -ne 'installed') { Fail "status $($install.status)" }
if ($install.artifact.fileName -notlike '*.exe') { Fail "artefato $($install.artifact.fileName)" }
if ($install.installDir -ne $root) { Fail "raiz $($install.installDir)" }
if ($install.environment.status -ne 'package') { Fail "ambiente $($install.environment.status)" }
if (-not $install.validation.succeeded) { Fail "validacao: $($install.validation.detail)" }
$runtimes = & (Join-Path $root 'dotnet.exe') --list-runtimes
if (-not ($runtimes -match [regex]::Escape("Microsoft.NETCore.App $version "))) { Fail "runtime nao listado: $runtimes" }
Pass "runtime $version instalado pelo instalador oficial"

$acl = Get-Acl -LiteralPath $state
$identities = $acl.Access | ForEach-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } | Sort-Object -Unique
if (-not $acl.AreAccessRulesProtected -or (($identities -join ',') -ne 'S-1-5-18,S-1-5-32-544')) { Fail "ACL de ${state}: $($identities -join ',')" }
if (Get-ChildItem -LiteralPath (Join-Path $state 'staging') -ErrorAction SilentlyContinue) { Fail 'staging nao foi removido' }
Pass 'estado global protegido (Administradores e SYSTEM) e staging limpo'

$again = Invoke-Scope @('install', 'runtime', $version, '--scope', 'global', '--yes', '--output', 'json') | ConvertFrom-Json
if ($again.status -ne 'alreadyInstalled') { Fail "reinstalacao: $($again.status)" }
Pass 'idempotente'

$removed = Invoke-Scope @('uninstall', 'runtime', $version, '--scope', 'global', '--output', 'json') | ConvertFrom-Json
if ($removed.origin -ne 'installer' -or $removed.status -ne 'removed') { Fail "uninstall: $($removed | ConvertTo-Json -Compress)" }
if (Test-Path -LiteralPath (Join-Path $root "shared\Microsoft.NETCore.App\$version")) { Fail 'runtime permaneceu' }
$null = Invoke-Scope @('uninstall', 'runtime', $version, '--scope', 'global') -Expected 3
Pass 'uninstall pelo instalador oficial; sem registro, nada e removido'

Write-Output 'E2E Windows global aprovado.'
