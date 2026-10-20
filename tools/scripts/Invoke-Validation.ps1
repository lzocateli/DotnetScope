<#
.SYNOPSIS
Valida o DotnetScope em container: restore, formatacao, build, testes e, opcionalmente, Native AOT.

.DESCRIPTION
Executa a mesma validacao usada no CI dentro da imagem fixada
docker.io/lzocateli/dotnet-sdk:10.0.402-noble-v1, montando o repositorio em
/workspace. Etapas: dotnet restore, dotnet format --verify-no-changes,
dotnet build -c Release, dotnet test -c Release e, com -Aot, instala clang e
zlib1g-dev no container, publica linux-x64 com PublishAot e executa
'dotnetscope --version' como smoke test.

O container roda como root: no Podman rootless isso corresponde ao usuario do
host; no Docker, os diretorios gerados (bin, obj, artifacts) sao devolvidos ao
usuario do host ao final. O script nunca altera perfil, registro ou PATH do host.

.PARAMETER Aot
Inclui a publicacao Native AOT linux-x64 e o smoke test do binario.

.PARAMETER Engine
Engine de container: docker ou podman. Padrao: DOTNETSCOPE_CONTAINER_ENGINE,
depois Docker e, na ausencia dele, Podman.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/Invoke-Validation.ps1

.EXAMPLE
./tools/scripts/Invoke-Validation.ps1 -Aot -Engine podman

.NOTES
Requer PowerShell 7+, Docker ou Podman e acesso a NuGet (e ao APT do Ubuntu com -Aot).
Pacotes NuGet ficam em cache em artifacts/.nuget, ignorado pelo Git.
Codigos: 0 sucesso; 2 erro de uso; demais valores vem da etapa que falhou.

.LINK
../../docs/plano-implementacao.md
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [switch] $Aot,

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

$image = 'docker.io/lzocateli/dotnet-sdk:10.0.402-noble-v1'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

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

$steps = [System.Collections.Generic.List[string]]::new()
$steps.Add('set -eu')
$steps.Add('dotnet restore DotnetScope.slnx')
$steps.Add('dotnet format DotnetScope.slnx --verify-no-changes --no-restore')
$steps.Add('dotnet build DotnetScope.slnx --configuration Release --no-restore')
$steps.Add('dotnet test DotnetScope.slnx --configuration Release --no-build')
if ($Aot) {
    # Sem faixa subordinada (Podman rootless), o APT nao pode usar o usuario _apt nem seus diretorios;
    # sandbox, listas e cache passam a usar root e /tmp dentro do container descartavel.
    $steps.Add('mkdir -p /tmp/apt/lists/partial /tmp/apt/archives/partial')
    $steps.Add('apt_opts="-o APT::Sandbox::User=root -o Dir::State::Lists=/tmp/apt/lists -o Dir::Cache::Archives=/tmp/apt/archives"')
    $steps.Add('apt-get $apt_opts update -qq')
    $steps.Add('DEBIAN_FRONTEND=noninteractive apt-get $apt_opts install -y -qq --no-install-recommends clang zlib1g-dev >/dev/null')
    $steps.Add('dotnet publish src/DotnetScope.Cli/DotnetScope.Cli.csproj --configuration Release --runtime linux-x64 --output artifacts/aot/linux-x64')
    $steps.Add('./artifacts/aot/linux-x64/dotnetscope --version')
}

$ownerFixup = $null
if ($containerEngine -eq 'docker' -and ($IsLinux -or $IsMacOS)) {
    $hostUid = (& id -u).Trim()
    $hostGid = (& id -g).Trim()
    $ownerFixup = "chown -R ${hostUid}:${hostGid} /workspace/artifacts; find /workspace/src /workspace/tests -type d \( -name bin -o -name obj \) -prune -exec chown -R ${hostUid}:${hostGid} {} +"
}

$script = ($steps -join "`n")
if ($null -ne $ownerFixup) {
    $script = "status=0`n( $($steps -join "`n") ) || status=`$?`n$ownerFixup`nexit `$status"
}

$arguments = @(
    'run', '--rm', '--user', '0:0',
    '--volume', "${repositoryRoot}:/workspace",
    '--workdir', '/workspace',
    '--env', 'DOTNET_CLI_TELEMETRY_OPTOUT=1',
    '--env', 'DOTNET_NOLOGO=1',
    '--env', 'NUGET_PACKAGES=/workspace/artifacts/.nuget/packages',
    $image, 'bash', '-c', $script
)

Write-Output "Validando com $containerEngine em $image$(if ($Aot) { ' (com Native AOT linux-x64)' })"
& $containerEngine @arguments
exit $LASTEXITCODE
