<#
.SYNOPSIS
Gera o manifesto winget do dotnetscope a partir do SHA256SUMS de uma release.

.DESCRIPTION
Le os SHA-256 de dotnetscope-win-x64.zip e dotnetscope-win-arm64.zip no
SHA256SUMS de -ReleaseDirectory e grava em -OutputDirectory o manifesto
multiarquivo (schema 1.10.0) do pacote lzocateli.DotnetScope:

  lzocateli.DotnetScope.yaml                (version)
  lzocateli.DotnetScope.installer.yaml      (zip com dotnetscope.exe portatil)
  lzocateli.DotnetScope.locale.en-US.yaml   (defaultLocale)

Os instaladores apontam para os assets da tag v<versao> no GitHub. O script nao
envia nada; a submissao ao microsoft/winget-pkgs e feita pelo workflow winget.yml
com wingetcreate submit.

.PARAMETER Version
Versao publicada (X.Y.Z), sem o prefixo v. Releases com sufixo nao vao ao winget.

.PARAMETER ReleaseDirectory
Diretorio com SHA256SUMS. Padrao: artifacts/release.

.PARAMETER OutputDirectory
Diretorio do manifesto; arquivos existentes sao substituidos. Padrao:
artifacts/winget/<versao>.

.PARAMETER ReleaseDate
Data da release (yyyy-MM-dd). Padrao: data UTC atual.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/New-WingetManifest.ps1 -Version 0.4.0 -ReleaseDirectory dist -ReleaseDate 2026-10-22

.NOTES
Requer PowerShell 7+. Codigos: 0 sucesso; 2 erro de uso ou SHA256SUMS incompleto.

.LINK
../../docs/release.md
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string] $Version,

    [string] $ReleaseDirectory,

    [string] $OutputDirectory,

    [ValidatePattern('^[0-9]{4}-[0-9]{2}-[0-9]{2}$')]
    [string] $ReleaseDate,

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

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
    $ReleaseDirectory = Join-Path $repositoryRoot 'artifacts/release'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts/winget/$Version"
}
if ([string]::IsNullOrWhiteSpace($ReleaseDate)) {
    $ReleaseDate = [DateTime]::UtcNow.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
}

$checksums = Join-Path ([IO.Path]::GetFullPath($ReleaseDirectory, (Get-Location).Path)) 'SHA256SUMS'
if (-not (Test-Path -LiteralPath $checksums -PathType Leaf)) {
    [Console]::Error.WriteLine("SHA256SUMS nao encontrado em $ReleaseDirectory. Use --help.")
    exit 2
}

$hashes = @{}
foreach ($line in Get-Content -LiteralPath $checksums) {
    if ($line -cmatch '^([0-9a-f]{64})  (dotnetscope-win-(arm64|x64)\.zip)$') {
        $hashes[$Matches[2]] = $Matches[1].ToUpperInvariant()
    }
}

$base = "https://github.com/lzocateli/DotnetScope/releases/download/v$Version"
$installers = foreach ($architecture in 'x64', 'arm64') {
    $file = "dotnetscope-win-$architecture.zip"
    if (-not $hashes.ContainsKey($file)) {
        [Console]::Error.WriteLine("SHA256SUMS sem $file. Use --help.")
        exit 2
    }

    "- Architecture: $architecture`n  InstallerUrl: $base/$file`n  InstallerSha256: $($hashes[$file])"
}

$identifier = 'lzocateli.DotnetScope'
$header = '# Gerado por tools/scripts/New-WingetManifest.ps1 a partir do SHA256SUMS da release.'
$schema = 'https://aka.ms/winget-manifest.{0}.1.10.0.schema.json'

$files = [ordered]@{
    "$identifier.yaml" = @"
$header
# yaml-language-server: `$schema=$($schema -f 'version')

PackageIdentifier: $identifier
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.10.0
"@
    "$identifier.installer.yaml" = @"
$header
# yaml-language-server: `$schema=$($schema -f 'installer')

PackageIdentifier: $identifier
PackageVersion: $Version
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
- RelativeFilePath: dotnetscope.exe
  PortableCommandAlias: dotnetscope
Commands:
- dotnetscope
ReleaseDate: $ReleaseDate
Installers:
$($installers -join "`n")
ManifestType: installer
ManifestVersion: 1.10.0
"@
    "$identifier.locale.en-US.yaml" = @"
$header
# yaml-language-server: `$schema=$($schema -f 'defaultLocale')

PackageIdentifier: $identifier
PackageVersion: $Version
PackageLocale: en-US
Publisher: Lincoln Zocateli
PublisherUrl: https://github.com/lzocateli
PublisherSupportUrl: https://github.com/lzocateli/DotnetScope/issues
PackageName: DotnetScope
PackageUrl: https://github.com/lzocateli/DotnetScope
License: MIT
LicenseUrl: https://github.com/lzocateli/DotnetScope/blob/HEAD/LICENSE
Copyright: Copyright (c) 2026 Lincoln Zocateli
ShortDescription: Install, list, validate and remove .NET SDKs and runtimes in user or global scope.
Description: Native AOT command line tool that resolves .NET SDK and runtime versions from the official Microsoft release metadata, verifies SHA-512 hashes, extracts archives safely and configures DOTNET_ROOT and PATH for the current user or the whole machine, without requiring .NET to be installed.
Moniker: dotnetscope
Tags:
- cli
- dotnet
- runtime
- sdk
- version-manager
ReleaseNotesUrl: https://github.com/lzocateli/DotnetScope/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: 1.10.0
"@
}

$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
foreach ($name in $files.Keys) {
    $content = ($files[$name] -replace "`r", '') + "`n"
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $name), $content, [Text.UTF8Encoding]::new($false))
}

Write-Output "Manifesto: $OutputDirectory"
