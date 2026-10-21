<#
.SYNOPSIS
Publica o dotnetscope em Native AOT por RID e empacota os artefatos de release.

.DESCRIPTION
Para cada RID informado, executa dotnet publish em Release com Native AOT e gera
em -OutputDirectory um pacote sem versao no nome:

  dotnetscope-<rid>.tar.gz  (Linux e macOS: binario dotnetscope e LICENSE)
  dotnetscope-<rid>.zip     (Windows: dotnetscope.exe e LICENSE)

Ao final, regenera SHA256SUMS (formato sha256sum, LF) com todos os pacotes e
instaladores presentes em -OutputDirectory.

RIDs Linux (linux-x64, linux-arm64, linux-musl-x64) sao compilados em container
com a toolchain oficial de cross-build do .NET (tools/build/linux-cross.Dockerfile):
sysroot com glibc 2.27 ou musl 1.2.3, clang e lld, para que o binario rode em
distribuicoes antigas. Funciona em host Linux x64 com Docker ou Podman.

RIDs Windows e macOS nao suportam compilacao cruzada entre sistemas operacionais
e sao publicados nativamente: win-* exige host Windows com Visual Studio C++ e
osx-* exige host macOS com Xcode; ambos usam o SDK do global.json.

O script nunca altera perfil, registro ou PATH do host.

.PARAMETER Rid
Um ou mais RIDs: linux-x64, linux-arm64, linux-musl-x64, win-x64, win-arm64,
osx-arm64 ou osx-x64. Obrigatorio, exceto com -ChecksumsOnly.

.PARAMETER VersionSuffix
Sufixo SemVer de pre-release (por exemplo rc.1); a versao base vem de
VersionPrefix em Directory.Build.props. Padrao: vazio.

.PARAMETER OutputDirectory
Diretorio dos pacotes e do SHA256SUMS. Padrao: artifacts/release.

.PARAMETER ChecksumsOnly
Nao publica; apenas regenera SHA256SUMS em -OutputDirectory.

.PARAMETER Engine
Engine de container para RIDs Linux: docker ou podman. Padrao:
DOTNETSCOPE_CONTAINER_ENGINE, depois Docker e, na ausencia dele, Podman.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/Publish-Aot.ps1 -Rid linux-x64, linux-musl-x64 -Engine podman

.EXAMPLE
./tools/scripts/Publish-Aot.ps1 -Rid win-x64 -VersionSuffix rc.1

.EXAMPLE
./tools/scripts/Publish-Aot.ps1 -ChecksumsOnly -OutputDirectory dist

.NOTES
Requer PowerShell 7+. RIDs Linux: Docker ou Podman e acesso a mcr.microsoft.com
e NuGet. Windows/macOS: SDK .NET do global.json e toolchain nativa.
Imagens fixadas por digest; atualize tag e digest juntos.
Codigos: 0 sucesso; 2 erro de uso ou host incompativel; demais valores vem da
etapa que falhou.

.LINK
../../docs/plano-implementacao.md
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('linux-x64', 'linux-arm64', 'linux-musl-x64', 'win-x64', 'win-arm64', 'osx-arm64', 'osx-x64')]
    [string[]] $Rid,

    [ValidatePattern('^[0-9A-Za-z][0-9A-Za-z.-]*$')]
    [string] $VersionSuffix,

    [string] $OutputDirectory,

    [switch] $ChecksumsOnly,

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

if (-not $ChecksumsOnly -and ($null -eq $Rid -or $Rid.Count -eq 0)) {
    [Console]::Error.WriteLine('Informe -Rid ou -ChecksumsOnly. Use --help.')
    exit 2
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts/release'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory, (Get-Location).Path)

function Write-ChecksumFile {
    param([string] $Directory)

    $patterns = @('dotnetscope-*.tar.gz', 'dotnetscope-*.zip', 'install.sh', 'install.ps1')
    $files = Get-ChildItem -LiteralPath $Directory -File |
        Where-Object { $name = $_.Name; $patterns.Where({ $name -like $_ }).Count -gt 0 } |
        Sort-Object Name -CaseSensitive
    if ($files.Count -eq 0) {
        throw "Nenhum pacote encontrado em $Directory."
    }

    $lines = foreach ($file in $files) {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $file.Name
    }
    $content = ($lines -join "`n") + "`n"
    [IO.File]::WriteAllText((Join-Path $Directory 'SHA256SUMS'), $content, [Text.UTF8Encoding]::new($false))
    Write-Output "SHA256SUMS ($($files.Count) arquivos) em $Directory"
}

if ($ChecksumsOnly) {
    if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) {
        [Console]::Error.WriteLine("Diretorio nao encontrado: $OutputDirectory. Use --help.")
        exit 2
    }
    Write-ChecksumFile -Directory $OutputDirectory
    exit 0
}

# Imagem oficial de cross-build (tag legivel + digest imutavel) e sysroot por RID.
$sdkImage = 'mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa'
$crossImages = @{
    'linux-x64'      = @('mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net10.0-cross-amd64-ecafe28@sha256:a851d98c330f4e5eaa32f694f29bfed82e8047cf90bfb8a5000aa3fbdda47b4a', '/crossrootfs/x64')
    'linux-arm64'    = @('mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net10.0-cross-arm64-ecafe28@sha256:e8ec62a221b9e1a07abb73eb1ddd3b86802fd50a14462142e9b13f2bf9208cd8', '/crossrootfs/arm64')
    'linux-musl-x64' = @('mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net10.0-cross-amd64-musl-9d37d84@sha256:f244847db10686f8286961ef719957e1203142e274501be9a0fc28d44c81229c', '/crossrootfs/x64')
}

$linuxRids = @($Rid | Where-Object { $_ -like 'linux-*' })
$windowsRids = @($Rid | Where-Object { $_ -like 'win-*' })
$macRids = @($Rid | Where-Object { $_ -like 'osx-*' })

if ($windowsRids.Count -gt 0 -and -not $IsWindows) {
    [Console]::Error.WriteLine("$($windowsRids -join ', ') exige host Windows (Native AOT nao compila entre sistemas operacionais). Use --help.")
    exit 2
}
if ($macRids.Count -gt 0 -and -not $IsMacOS) {
    [Console]::Error.WriteLine("$($macRids -join ', ') exige host macOS (Native AOT nao compila entre sistemas operacionais). Use --help.")
    exit 2
}

$containerEngine = $null
if ($linuxRids.Count -gt 0) {
    $hostArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    if (-not $IsLinux -or $hostArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
        [Console]::Error.WriteLine('RIDs Linux exigem host Linux x64 (imagens de cross-build sao amd64). Use --help.')
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
}

$project = 'src/DotnetScope.Cli/DotnetScope.Cli.csproj'
$license = Join-Path $repositoryRoot 'LICENSE'
$null = New-Item -ItemType Directory -Force -Path $OutputDirectory

function Get-PublishArgumentList {
    param([string] $TargetRid)

    $arguments = @(
        'publish', $project,
        '--configuration', 'Release',
        '--runtime', $TargetRid,
        '--artifacts-path', "artifacts/aot-build/$TargetRid",
        '--output', "artifacts/publish/$TargetRid"
    )
    if (-not [string]::IsNullOrWhiteSpace($VersionSuffix)) {
        $arguments += "-p:VersionSuffix=$VersionSuffix"
    }
    return $arguments
}

function Invoke-LinuxPublish {
    param([string] $TargetRid)

    $image, $sysRoot = $crossImages[$TargetRid]
    $toolchainTag = "localhost/dotnetscope-cross:$TargetRid"

    Write-Host "Preparando toolchain $TargetRid com $containerEngine"
    & $containerEngine build --quiet `
        --file (Join-Path $repositoryRoot 'tools/build/linux-cross.Dockerfile') `
        --build-arg "CROSS_IMAGE=$image" `
        --build-arg "SDK_IMAGE=$sdkImage" `
        --tag $toolchainTag `
        (Join-Path $repositoryRoot 'tools/build') | Out-Host
    if ($LASTEXITCODE -ne 0) {
        return $LASTEXITCODE
    }

    $publish = (Get-PublishArgumentList -TargetRid $TargetRid) + @("-p:SysRoot=$sysRoot", '-p:LinkerFlavor=lld')
    $quoted = ($publish | ForEach-Object { "'" + ($_ -replace "'", "'\''") + "'" }) -join ' '
    $script = "dotnet $quoted"
    if ($containerEngine -eq 'docker') {
        # Docker cria arquivos como root; devolve os diretorios gerados ao usuario do host.
        $hostUid = (& id -u).Trim()
        $hostGid = (& id -g).Trim()
        $script = "status=0; $script || status=`$?; chown -R ${hostUid}:${hostGid} /workspace/artifacts; exit `$status"
    }

    $environment = @(
        '--env', 'NUGET_PACKAGES=/workspace/artifacts/.nuget/packages'
    )
    if ($env:GITHUB_ACTIONS -eq 'true') {
        $environment += @('--env', 'GITHUB_ACTIONS=true')
    }

    Write-Host "Publicando $TargetRid (sysroot $sysRoot)"
    & $containerEngine run --rm --user 0:0 `
        --volume "${repositoryRoot}:/workspace" `
        --workdir /workspace `
        @environment `
        $toolchainTag sh -c $script | Out-Host
    return $LASTEXITCODE
}

function Invoke-NativePublish {
    param([string] $TargetRid)

    Write-Host "Publicando $TargetRid nativamente"
    Push-Location $repositoryRoot
    try {
        & dotnet @(Get-PublishArgumentList -TargetRid $TargetRid) | Out-Host
        return $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}

function Build-Package {
    param([string] $TargetRid)

    $isWindowsRid = $TargetRid -like 'win-*'
    $binaryName = if ($isWindowsRid) { 'dotnetscope.exe' } else { 'dotnetscope' }
    $binary = Join-Path $repositoryRoot "artifacts/publish/$TargetRid/$binaryName"
    if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) {
        throw "Binario nao encontrado apos publicacao: $binary"
    }

    $staging = Join-Path $repositoryRoot "artifacts/package/$TargetRid"
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
    $null = New-Item -ItemType Directory -Force -Path $staging
    Copy-Item -LiteralPath $binary -Destination (Join-Path $staging $binaryName)
    Copy-Item -LiteralPath $license -Destination (Join-Path $staging 'LICENSE')

    if ($isWindowsRid) {
        $package = Join-Path $OutputDirectory "dotnetscope-$TargetRid.zip"
        Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $package -CompressionLevel Optimal -Force
    }
    else {
        & chmod 0755 (Join-Path $staging $binaryName)
        & chmod 0644 (Join-Path $staging 'LICENSE')
        $package = Join-Path $OutputDirectory "dotnetscope-$TargetRid.tar.gz"
        $tarVersion = & tar --version 2>&1 | Select-Object -First 1
        $ownership = if ("$tarVersion" -match 'GNU') {
            @('--owner=0', '--group=0', '--numeric-owner')
        }
        else {
            @('--uid', '0', '--gid', '0')
        }
        & tar @ownership -czf $package -C $staging $binaryName 'LICENSE'
        if ($LASTEXITCODE -ne 0) {
            throw "tar falhou ao criar $package (codigo $LASTEXITCODE)."
        }
    }

    Write-Output "Pacote: $package"
}

foreach ($targetRid in $Rid) {
    $code = if ($targetRid -like 'linux-*') {
        Invoke-LinuxPublish -TargetRid $targetRid
    }
    else {
        Invoke-NativePublish -TargetRid $targetRid
    }

    if ($code -ne 0) {
        [Console]::Error.WriteLine("Publicacao de $targetRid falhou (codigo $code).")
        exit $code
    }

    Build-Package -TargetRid $targetRid
}

Write-ChecksumFile -Directory $OutputDirectory
