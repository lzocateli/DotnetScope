<#
.SYNOPSIS
Executa o smoke test de um pacote de release do dotnetscope sem .NET instalado.

.DESCRIPTION
Confere o pacote dotnetscope-<rid> contra SHA256SUMS, extrai o binario e
executa: --version (deve comecar pela versao esperada), --help e
'list --available --offline' usando as fixtures de tests/Fixtures/metadata
como cache, sem acesso a rede.

RIDs Linux rodam em containers limpos, sem .NET: imagens glibc antigas para
validar a base de compatibilidade (glibc 2.27) e Alpine para musl. O host deve
ter a mesma arquitetura do RID (sem emulacao).
RIDs Windows e macOS rodam nativamente no host correspondente, com cache
isolado em diretorio temporario.

.PARAMETER Rid
RID do pacote: linux-x64, linux-arm64, linux-musl-x64, win-x64, win-arm64,
osx-arm64 ou osx-x64.

.PARAMETER ReleaseDirectory
Diretorio com os pacotes e SHA256SUMS. Padrao: artifacts/release.

.PARAMETER VersionSuffix
Sufixo de pre-release esperado na versao (por exemplo rc.1). Padrao: vazio.

.PARAMETER Image
Imagens para RIDs Linux. Padrao: ubuntu:18.04, rockylinux:8 e debian:12 para
glibc; alpine:3.17 e alpine:3.24 para musl.

.PARAMETER Engine
Engine de container para RIDs Linux: docker ou podman. Padrao:
DOTNETSCOPE_CONTAINER_ENGINE, depois Docker e, na ausencia dele, Podman.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/Test-AotPackage.ps1 -Rid linux-musl-x64 -Engine podman

.EXAMPLE
./tools/scripts/Test-AotPackage.ps1 -Rid win-x64 -ReleaseDirectory dist

.NOTES
Requer PowerShell 7+ e o pacote gerado por Publish-Aot.ps1. RIDs Linux exigem
Docker ou Podman. O script nunca altera perfil, registro ou PATH do host.
Codigos: 0 sucesso; 1 smoke reprovado; 2 erro de uso ou host incompativel.

.LINK
./Publish-Aot.ps1
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('linux-x64', 'linux-arm64', 'linux-musl-x64', 'win-x64', 'win-arm64', 'osx-arm64', 'osx-x64')]
    [string] $Rid,

    [string] $ReleaseDirectory,

    [ValidatePattern('^[0-9A-Za-z][0-9A-Za-z.-]*$')]
    [string] $VersionSuffix,

    [string[]] $Image,

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

if ([string]::IsNullOrWhiteSpace($Rid)) {
    [Console]::Error.WriteLine('Informe -Rid. Use --help.')
    exit 2
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
    $ReleaseDirectory = Join-Path $repositoryRoot 'artifacts/release'
}
$ReleaseDirectory = [IO.Path]::GetFullPath($ReleaseDirectory, (Get-Location).Path)
$fixtures = Join-Path $repositoryRoot 'tests/Fixtures/metadata'

$isWindowsRid = $Rid -like 'win-*'
$packageName = if ($isWindowsRid) { "dotnetscope-$Rid.zip" } else { "dotnetscope-$Rid.tar.gz" }
$package = Join-Path $ReleaseDirectory $packageName
$checksums = Join-Path $ReleaseDirectory 'SHA256SUMS'
foreach ($required in @($package, $checksums)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        [Console]::Error.WriteLine("Arquivo nao encontrado: $required. Execute Publish-Aot.ps1. Use --help.")
        exit 2
    }
}

[xml] $props = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Raw
$expectedVersion = @($props.Project.PropertyGroup | ForEach-Object { $_.VersionPrefix } | Where-Object { $_ })[0]
if (-not [string]::IsNullOrWhiteSpace($VersionSuffix)) {
    $expectedVersion = "$expectedVersion-$VersionSuffix"
}

$expectedHash = @(Get-Content -LiteralPath $checksums |
    Where-Object { $_ -match '^([0-9a-f]{64})  (.+)$' -and $Matches[2] -eq $packageName } |
    ForEach-Object { $_.Substring(0, 64) })
$actualHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expectedHash.Count -ne 1 -or $expectedHash[0] -ne $actualHash) {
    [Console]::Error.WriteLine("SHA256 de $packageName nao confere com SHA256SUMS.")
    exit 1
}
Write-Output "SHA256 conferido: $packageName"

$workDirectory = Join-Path ([IO.Path]::GetTempPath()) "dotnetscope-smoke-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $workDirectory
try {
    $extractDirectory = Join-Path $workDirectory 'package'
    $null = New-Item -ItemType Directory -Path $extractDirectory
    if ($isWindowsRid) {
        Expand-Archive -LiteralPath $package -DestinationPath $extractDirectory
    }
    else {
        & tar -xzf $package -C $extractDirectory
        if ($LASTEXITCODE -ne 0) {
            [Console]::Error.WriteLine("Falha ao extrair $packageName.")
            exit 1
        }
    }

    $binaryName = if ($isWindowsRid) { 'dotnetscope.exe' } else { 'dotnetscope' }
    $entries = @(Get-ChildItem -LiteralPath $extractDirectory -Force | ForEach-Object Name)
    if ($entries.Count -ne 2 -or $entries -cnotcontains 'LICENSE' -or $entries -cnotcontains $binaryName) {
        [Console]::Error.WriteLine("Conteudo inesperado em ${packageName}: $($entries -join ', ').")
        exit 1
    }
    $binary = Join-Path $extractDirectory $binaryName

    if ($Rid -like 'linux-*') {
        $ridArchitecture = if ($Rid -like '*-arm64') { [Runtime.InteropServices.Architecture]::Arm64 } else { [Runtime.InteropServices.Architecture]::X64 }
        if (-not $IsLinux -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne $ridArchitecture) {
            [Console]::Error.WriteLine("$Rid exige host Linux $ridArchitecture (sem emulacao). Use --help.")
            exit 2
        }

        if ($null -eq $Image -or $Image.Count -eq 0) {
            $Image = if ($Rid -eq 'linux-musl-x64') {
                @('docker.io/library/alpine:3.17', 'docker.io/library/alpine:3.24')
            }
            else {
                @('docker.io/library/ubuntu:18.04', 'docker.io/library/rockylinux:8', 'docker.io/library/debian:12')
            }
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

        $smoke = @'
set -eu
if command -v dotnet >/dev/null 2>&1; then echo "imagem possui dotnet no PATH" >&2; exit 1; fi
version=$(dotnetscope --version)
case "$version" in
  "$EXPECTED_VERSION"|"$EXPECTED_VERSION"+*) ;;
  *) echo "versao inesperada: $version (esperada $EXPECTED_VERSION)" >&2; exit 1 ;;
esac
dotnetscope --help >/dev/null
dotnetscope list --available --offline > /tmp/list.txt
grep -q '^10\.0 ' /tmp/list.txt
echo "ok: $version"
'@
        # O arquivo .ps1 usa CRLF; o shell do container exige LF.
        $smoke = $smoke -replace "`r", ''

        $failures = @()
        foreach ($name in $Image) {
            Write-Output "== $Rid em $name"
            & $containerEngine run --rm --network none `
                --volume "${binary}:/usr/local/bin/dotnetscope:ro" `
                --volume "${fixtures}:/cache/dotnetscope/metadata:ro" `
                --env XDG_CACHE_HOME=/cache `
                --env "EXPECTED_VERSION=$expectedVersion" `
                $name sh -c $smoke
            if ($LASTEXITCODE -ne 0) {
                $failures += "$name (codigo $LASTEXITCODE)"
            }
        }

        if ($failures.Count -gt 0) {
            [Console]::Error.WriteLine("Smoke de $Rid falhou em: $($failures -join ', ')")
            exit 1
        }
        Write-Output "Smoke de $Rid aprovado em: $($Image -join ', ')"
        exit 0
    }

    $hostMatches = if ($isWindowsRid) { $IsWindows } else { $IsMacOS }
    if (-not $hostMatches) {
        [Console]::Error.WriteLine("$Rid exige host $(if ($isWindowsRid) { 'Windows' } else { 'macOS' }). Use --help.")
        exit 2
    }

    # Cache isolado: LOCALAPPDATA no Windows e XDG_CACHE_HOME no macOS (ver DotnetScopePaths).
    $cacheRoot = Join-Path $workDirectory 'cache'
    $cacheVariable = if ($isWindowsRid) { 'LOCALAPPDATA' } else { 'XDG_CACHE_HOME' }
    $metadataDirectory = if ($isWindowsRid) { Join-Path $cacheRoot 'dotnetscope/cache/metadata' } else { Join-Path $cacheRoot 'dotnetscope/metadata' }
    $null = New-Item -ItemType Directory -Force -Path $metadataDirectory
    Copy-Item -Path (Join-Path $fixtures '*.json') -Destination $metadataDirectory
    if (-not $isWindowsRid) {
        & chmod 0755 $binary
    }

    $previousCache = [Environment]::GetEnvironmentVariable($cacheVariable)
    [Environment]::SetEnvironmentVariable($cacheVariable, $cacheRoot)
    try {
        $version = (& $binary --version | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or -not ($version -eq $expectedVersion -or $version.StartsWith("$expectedVersion+", [StringComparison]::Ordinal))) {
            [Console]::Error.WriteLine("versao inesperada: '$version' (codigo $LASTEXITCODE, esperada $expectedVersion)")
            exit 1
        }

        $null = & $binary --help
        if ($LASTEXITCODE -ne 0) {
            [Console]::Error.WriteLine("--help falhou (codigo $LASTEXITCODE)")
            exit 1
        }

        $list = & $binary list --available --offline
        if ($LASTEXITCODE -ne 0 -or -not ($list | Where-Object { $_ -match '^10\.0 ' })) {
            [Console]::Error.WriteLine("list --available --offline falhou (codigo $LASTEXITCODE)")
            exit 1
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable($cacheVariable, $previousCache)
    }

    Write-Output "Smoke de $Rid aprovado: $version"
}
finally {
    Remove-Item -LiteralPath $workDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
