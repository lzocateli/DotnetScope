<#
.SYNOPSIS
Gera a formula Homebrew do dotnetscope a partir do SHA256SUMS de uma release.

.DESCRIPTION
Le os SHA-256 de dotnetscope-osx-arm64, osx-x64, linux-arm64 e linux-x64 (.tar.gz)
no SHA256SUMS de -ReleaseDirectory e grava Formula/dotnetscope.rb apontando para
os assets da tag v<versao> no GitHub. A formula instala somente o binario em bin
e testa 'dotnetscope --version'. Usada pelo release.yml para atualizar o tap
lzocateli/homebrew-tap; nao altera nada fora de -OutputFile.

.PARAMETER Version
Versao publicada (X.Y.Z), sem o prefixo v. Releases com sufixo nao atualizam o tap.

.PARAMETER ReleaseDirectory
Diretorio com SHA256SUMS. Padrao: artifacts/release.

.PARAMETER OutputFile
Arquivo da formula. Padrao: artifacts/homebrew/Formula/dotnetscope.rb.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/New-HomebrewFormula.ps1 -Version 0.3.0 -ReleaseDirectory dist

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

    [string] $OutputFile,

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
if ([string]::IsNullOrWhiteSpace($OutputFile)) {
    $OutputFile = Join-Path $repositoryRoot 'artifacts/homebrew/Formula/dotnetscope.rb'
}

$checksums = Join-Path ([IO.Path]::GetFullPath($ReleaseDirectory, (Get-Location).Path)) 'SHA256SUMS'
if (-not (Test-Path -LiteralPath $checksums -PathType Leaf)) {
    [Console]::Error.WriteLine("SHA256SUMS nao encontrado em $ReleaseDirectory. Use --help.")
    exit 2
}

$hashes = @{}
foreach ($line in Get-Content -LiteralPath $checksums) {
    if ($line -cmatch '^([0-9a-f]{64})  (dotnetscope-(osx|linux)-(arm64|x64)\.tar\.gz)$') {
        $hashes[$Matches[2]] = $Matches[1]
    }
}

$base = "https://github.com/lzocateli/DotnetScope/releases/download/v$Version"
$blocks = @{}
foreach ($rid in 'osx-arm64', 'osx-x64', 'linux-arm64', 'linux-x64') {
    $file = "dotnetscope-$rid.tar.gz"
    if (-not $hashes.ContainsKey($file)) {
        [Console]::Error.WriteLine("SHA256SUMS sem $file. Use --help.")
        exit 2
    }

    $blocks[$rid] = "      url `"$base/$file`"`n      sha256 `"$($hashes[$file])`""
}

$formula = @"
# Gerado por tools/scripts/New-HomebrewFormula.ps1 a partir do SHA256SUMS da release; nao edite.
class Dotnetscope < Formula
  desc "Install, list, validate and remove .NET SDKs and runtimes"
  homepage "https://github.com/lzocateli/DotnetScope"
  version "$Version"
  license "MIT"

  on_macos do
    on_arm do
$($blocks['osx-arm64'])
    end
    on_intel do
$($blocks['osx-x64'])
    end
  end

  on_linux do
    on_arm do
$($blocks['linux-arm64'])
    end
    on_intel do
$($blocks['linux-x64'])
    end
  end

  def install
    bin.install "dotnetscope"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/dotnetscope --version")
  end
end
"@ -replace "`r", ''

$null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputFile)
[IO.File]::WriteAllText($OutputFile, "$formula`n", [Text.UTF8Encoding]::new($false))
Write-Output "Formula: $OutputFile"
