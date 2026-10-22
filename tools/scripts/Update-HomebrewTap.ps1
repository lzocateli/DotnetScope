<#
.SYNOPSIS
Atualiza a formula do dotnetscope no tap Homebrew a partir de uma release.

.DESCRIPTION
Clona -Repository (raso) em -WorkDirectory, gera Formula/dotnetscope.rb com
New-HomebrewFormula.ps1 a partir do SHA256SUMS de -ReleaseDirectory e cria um
commit "dotnetscope <versao>" no ramo -Branch. Aceita tap vazio (primeira
publicacao). Nao faz nada quando a formula ja corresponde a versao e, quando o
tap ja publica versao maior (correcao de linha antiga), nao altera o tap e
retorna 3.

Sem -Push o commit fica apenas no clone local, para inspecao. Com -Push envia o
commit; para remotos https://github.com/ o token e lido exclusivamente da
variavel de ambiente HOMEBREW_TAP_TOKEN (PAT fine-grained com Contents read/write
somente no tap) e repassado ao git por GIT_CONFIG_*, fora da linha de comando.

.PARAMETER Version
Versao publicada (X.Y.Z), sem o prefixo v. Releases com sufixo nao atualizam o tap.

.PARAMETER ReleaseDirectory
Diretorio com SHA256SUMS. Padrao: artifacts/release.

.PARAMETER Repository
URL ou caminho do repositorio do tap. Padrao: https://github.com/lzocateli/homebrew-tap.git.

.PARAMETER Branch
Ramo do tap. Padrao: main.

.PARAMETER WorkDirectory
Diretorio do clone; nao pode existir. Padrao: diretorio temporario novo.

.PARAMETER Push
Envia o commit ao remoto. Sem esta opcao nada e publicado.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/Update-HomebrewTap.ps1 -Version 0.4.0 -ReleaseDirectory dist

.EXAMPLE
./tools/scripts/Update-HomebrewTap.ps1 -Version 0.4.0 -ReleaseDirectory dist -Push

.NOTES
Requer PowerShell 7+ e git 2.31+. Codigos: 0 tap na versao pedida (atualizado ou
ja atual); 1 falha do git; 2 erro de uso; 3 tap mantido em versao maior.

.LINK
../../docs/release.md
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string] $Version,

    [string] $ReleaseDirectory,

    [string] $Repository = 'https://github.com/lzocateli/homebrew-tap.git',

    [ValidatePattern('^[A-Za-z0-9._/-]+$')]
    [string] $Branch = 'main',

    [string] $WorkDirectory,

    [switch] $Push,

    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

$ErrorActionPreference = 'Stop'

if ($RemainingArguments -contains '--help') {
    Get-Help $PSCommandPath -Full
    exit 0
}

if ($RemainingArguments.Count -gt 0 -or [string]::IsNullOrWhiteSpace($Version)) {
    [Console]::Error.WriteLine('Informe -Version X.Y.Z. Use --help.')
    exit 2
}

if ([string]::IsNullOrWhiteSpace($WorkDirectory)) {
    $WorkDirectory = Join-Path ([IO.Path]::GetTempPath()) "dotnetscope-tap-$([guid]::NewGuid().ToString('N'))"
}
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory, (Get-Location).Path)
if (Test-Path -LiteralPath $WorkDirectory) {
    [Console]::Error.WriteLine("WorkDirectory ja existe: $WorkDirectory. Use --help.")
    exit 2
}

$needsToken = $Push -and $Repository -like 'https://github.com/*'
if ($needsToken -and [string]::IsNullOrWhiteSpace($env:HOMEBREW_TAP_TOKEN)) {
    [Console]::Error.WriteLine('HOMEBREW_TAP_TOKEN ausente para -Push. Use --help.')
    exit 2
}

function Invoke-Git {
    param([string[]] $Arguments)
    & git -C $WorkDirectory @Arguments
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("git $($Arguments[0]) falhou (codigo $LASTEXITCODE).")
        exit 1
    }
}

& git clone --quiet --depth 1 -- $Repository $WorkDirectory
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("git clone de $Repository falhou (codigo $LASTEXITCODE).")
    exit 1
}

& git -C $WorkDirectory rev-parse --verify --quiet HEAD *> $null
if ($LASTEXITCODE -ne 0) {
    # Tap vazio: o primeiro commit nasce no ramo pedido.
    Invoke-Git @('symbolic-ref', 'HEAD', "refs/heads/$Branch")
}
else {
    $current = (& git -C $WorkDirectory symbolic-ref --short HEAD)
    if ($current -ne $Branch) {
        [Console]::Error.WriteLine("Ramo padrao do tap e '$current', esperado '$Branch'. Use --help.")
        exit 2
    }
}

$formula = Join-Path $WorkDirectory 'Formula/dotnetscope.rb'
if (Test-Path -LiteralPath $formula -PathType Leaf) {
    $published = Select-String -LiteralPath $formula -Pattern '^\s*version "([0-9]+\.[0-9]+\.[0-9]+)"' |
        Select-Object -First 1
    if ($null -ne $published -and [version] $published.Matches[0].Groups[1].Value -gt [version] $Version) {
        Write-Output "Tap ja publica $($published.Matches[0].Groups[1].Value), maior que $Version; tap mantido."
        exit 3
    }
}

$generatorArguments = @{ Version = $Version; OutputFile = $formula }
if (-not [string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
    $generatorArguments.ReleaseDirectory = $ReleaseDirectory
}
& (Join-Path $PSScriptRoot 'New-HomebrewFormula.ps1') @generatorArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Invoke-Git @('add', '--', 'Formula/dotnetscope.rb')
& git -C $WorkDirectory diff --cached --quiet
if ($LASTEXITCODE -eq 0) {
    Write-Output "Tap ja contem a formula de $Version; nada a fazer."
    exit 0
}

Invoke-Git @(
    '-c', 'user.name=github-actions[bot]',
    '-c', 'user.email=41898283+github-actions[bot]@users.noreply.github.com',
    'commit', '--quiet', '-m', "dotnetscope $Version")

if (-not $Push) {
    Write-Output "Commit criado somente em $WorkDirectory (sem -Push)."
    exit 0
}

if ($needsToken) {
    $basic = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("x-access-token:$($env:HOMEBREW_TAP_TOKEN)"))
    if ($env:GITHUB_ACTIONS -eq 'true') {
        Write-Output "::add-mask::$basic"
    }
    $env:GIT_CONFIG_COUNT = '1'
    $env:GIT_CONFIG_KEY_0 = 'http.https://github.com/.extraheader'
    $env:GIT_CONFIG_VALUE_0 = "AUTHORIZATION: basic $basic"
}

try {
    Invoke-Git @('push', '--quiet', 'origin', "HEAD:refs/heads/$Branch")
}
finally {
    Remove-Item Env:GIT_CONFIG_COUNT, Env:GIT_CONFIG_KEY_0, Env:GIT_CONFIG_VALUE_0 -ErrorAction SilentlyContinue
}

Write-Output "Tap atualizado para dotnetscope $Version."
