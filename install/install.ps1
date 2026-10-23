<#
.SYNOPSIS
Instala o dotnetscope no Windows a partir das releases do GitHub.

.DESCRIPTION
Baixa dotnetscope-win-<arch>.zip e SHA256SUMS da release, confere o SHA-256,
testa o binario no destino e o instala em -InstallDir, substituindo a versao
anterior somente depois que a nova executa. Compativel com Windows PowerShell
5.1 e PowerShell 7.

O instalador nao altera o PATH nem o registro: se o diretorio de destino nao
estiver no PATH, ele mostra os comandos a executar, um que grava o PATH do
usuario no registro e outro, para ambientes que bloqueiam o registro, que so
acrescenta o diretorio ao perfil do PowerShell.

.PARAMETER Version
Versao a instalar (X.Y.Z ou X.Y.Z-sufixo). Padrao: latest.

.PARAMETER InstallDir
Diretorio absoluto de destino. Padrao: %LOCALAPPDATA%\Programs\dotnetscope.

.PARAMETER BaseUrl
Diretorio com os pacotes e o SHA256SUMS (espelho ou testes). Exige https://,
exceto http://127.0.0.1 e http://localhost. Nao combina com -Version.

.PARAMETER Help
Mostra esta ajuda (equivalente a --help).

.EXAMPLE
powershell -ExecutionPolicy Bypass -File install.ps1

.EXAMPLE
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
& ([scriptblock]::Create((Invoke-RestMethod https://github.com/lzocateli/DotnetScope/releases/latest/download/install.ps1))) -Version 0.1.0

A primeira linha habilita TLS 1.2 (exigido pelo GitHub) no Windows PowerShell 5.1
antes de baixar o instalador.

.NOTES
Plataformas: win-x64 e win-arm64. Codigos de saida (execucao com -File):
0 sucesso; 1 falha; 2 erro de uso. Executado como scriptblock, erros sao
lancados como excecao para nao encerrar a sessao do PowerShell.
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Version = 'latest',

    [string] $InstallDir,

    [string] $BaseUrl,

    [switch] $Help,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $RemainingArguments
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$script:RunAsFile = -not [string]::IsNullOrEmpty($PSCommandPath)
$repositoryUrl = 'https://github.com/lzocateli/DotnetScope/releases'

# Antivirus pode manter o arquivo recem-gravado aberto por alguns instantes.
function Move-FileWithRetry {
    param([string] $Source, [string] $Destination)

    for ($attempt = 1; ; $attempt++) {
        try {
            [IO.File]::Move($Source, $Destination)
            return
        }
        catch [IO.IOException], [UnauthorizedAccessException] {
            if ($attempt -ge 10) {
                throw
            }
            Start-Sleep -Milliseconds 300
        }
    }
}

function Exit-Install {
    param([int] $Code, [string] $Message)

    if ($Code -eq 2) {
        $Message = "$Message. Use -Help."
    }
    if ($script:RunAsFile) {
        [Console]::Error.WriteLine("dotnetscope-install: $Message")
        exit $Code
    }
    throw "dotnetscope-install: $Message"
}

if ($Help -or ($null -ne $RemainingArguments -and ($RemainingArguments -contains '--help' -or $RemainingArguments -contains '-h'))) {
    if ($script:RunAsFile) {
        Get-Help $PSCommandPath -Full
    }
    else {
        Write-Output 'Uso: install.ps1 [-Version <X.Y.Z|latest>] [-InstallDir <dir>] [-BaseUrl <url>] [-Help]'
    }
    return
}

if ($null -ne $RemainingArguments -and $RemainingArguments.Count -gt 0) {
    Exit-Install 2 "argumento desconhecido: $($RemainingArguments -join ' ')"
}

$onWindows = ($PSVersionTable.PSEdition -ne 'Core') -or ((Get-Variable -Name IsWindows -ValueOnly -ErrorAction SilentlyContinue) -eq $true)
if (-not $onWindows) {
    Exit-Install 1 'este instalador e para Windows; no Linux e macOS use install.sh'
}

$versionSet = $PSBoundParameters.ContainsKey('Version')
if ($Version.StartsWith('v')) {
    $Version = $Version.Substring(1)
}
if ($Version -ne 'latest' -and $Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$') {
    Exit-Install 2 "versao invalida: $Version"
}

if (-not [string]::IsNullOrEmpty($BaseUrl)) {
    if ($versionSet) {
        Exit-Install 2 '-BaseUrl e -Version sao mutuamente exclusivos'
    }
    $parsed = $null
    if (-not [Uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref] $parsed)) {
        Exit-Install 2 "-BaseUrl invalida: $BaseUrl"
    }
    if (-not [string]::IsNullOrEmpty($parsed.UserInfo)) {
        Exit-Install 2 '-BaseUrl nao aceita credenciais'
    }
    $loopback = $parsed.Host -eq '127.0.0.1' -or $parsed.Host -eq 'localhost'
    if ($parsed.Scheme -ne 'https' -and -not ($parsed.Scheme -eq 'http' -and $loopback)) {
        Exit-Install 2 '-BaseUrl deve usar https:// (http:// somente para 127.0.0.1 ou localhost)'
    }
    $assetUrl = $BaseUrl.TrimEnd('/')
}
elseif ($Version -eq 'latest') {
    $assetUrl = "$repositoryUrl/latest/download"
}
else {
    $assetUrl = "$repositoryUrl/download/v$Version"
}

if ([string]::IsNullOrEmpty($InstallDir)) {
    if ([string]::IsNullOrEmpty($env:LOCALAPPDATA)) {
        Exit-Install 1 'LOCALAPPDATA nao definido; informe -InstallDir'
    }
    $InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\dotnetscope'
}
if ($InstallDir -notmatch '^([A-Za-z]:\\|\\\\[^\\]+\\[^\\]+)') {
    Exit-Install 2 '-InstallDir deve ser um caminho absoluto'
}
$InstallDir = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')

# Valor nativo do registro: nao e afetado pela emulacao x64/x86 em Windows ARM64.
$nativeArchitecture = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment' -Name PROCESSOR_ARCHITECTURE).PROCESSOR_ARCHITECTURE
switch ($nativeArchitecture) {
    'AMD64' { $rid = 'win-x64' }
    'ARM64' { $rid = 'win-arm64' }
    default { Exit-Install 1 "arquitetura nao suportada: $nativeArchitecture" }
}
$asset = "dotnetscope-$rid.zip"

if ($PSVersionTable.PSEdition -ne 'Core') {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

$workDirectory = Join-Path ([IO.Path]::GetTempPath()) ('dotnetscope-install-' + [Guid]::NewGuid().ToString('N'))
$stagedBinary = $null
$previousProgress = $ProgressPreference
try {
    $ProgressPreference = 'SilentlyContinue'
    $null = New-Item -ItemType Directory -Path $workDirectory

    Write-Output "Baixando $asset de $assetUrl"
    $checksums = Join-Path $workDirectory 'SHA256SUMS'
    $package = Join-Path $workDirectory $asset
    try {
        Invoke-WebRequest -Uri "$assetUrl/SHA256SUMS" -OutFile $checksums -UseBasicParsing
    }
    catch {
        Exit-Install 1 "falha ao baixar SHA256SUMS de ${assetUrl}: $($_.Exception.Message)"
    }
    try {
        Invoke-WebRequest -Uri "$assetUrl/$asset" -OutFile $package -UseBasicParsing
    }
    catch {
        Exit-Install 1 "falha ao baixar $asset (versao ou plataforma inexistente?): $($_.Exception.Message)"
    }

    $expected = @(Get-Content -LiteralPath $checksums |
        Where-Object { $_ -cmatch '^[0-9a-f]{64}  (.+)$' -and $Matches[1] -ceq $asset } |
        ForEach-Object { $_.Substring(0, 64) })
    if ($expected.Count -ne 1) {
        Exit-Install 1 "SHA256SUMS nao contem uma entrada valida para $asset"
    }
    $actual = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $expected[0]) {
        Exit-Install 1 "SHA-256 de $asset nao confere (esperado $($expected[0]), obtido $actual); nada foi instalado"
    }
    Write-Output "SHA-256 conferido: $actual"

    $extractDirectory = Join-Path $workDirectory 'extract'
    Expand-Archive -LiteralPath $package -DestinationPath $extractDirectory
    $extractedBinary = Join-Path $extractDirectory 'dotnetscope.exe'
    if (-not (Test-Path -LiteralPath $extractedBinary -PathType Leaf)) {
        Exit-Install 1 "$asset nao contem dotnetscope.exe"
    }

    $target = Join-Path $InstallDir 'dotnetscope.exe'
    if (Test-Path -LiteralPath $target -PathType Container) {
        Exit-Install 1 "$target e um diretorio; remova-o antes de instalar"
    }
    $null = New-Item -ItemType Directory -Force -Path $InstallDir
    $stagedBinary = Join-Path $InstallDir ".dotnetscope.install.$PID.exe"
    Copy-Item -LiteralPath $extractedBinary -Destination $stagedBinary -Force

    try {
        $installedVersion = & $stagedBinary --version
        $versionExitCode = $LASTEXITCODE
    }
    catch {
        $versionExitCode = -1
    }
    if ($versionExitCode -ne 0) {
        Exit-Install 1 "o binario baixado nao executa nesta plataforma ($rid); nada foi substituido"
    }

    # Renomear funciona mesmo com o executavel atual em uso; o backup restaura a versao
    # anterior se a troca falhar e e removido em seguida (ou na proxima instalacao).
    Get-ChildItem -LiteralPath $InstallDir -Filter '.dotnetscope.old.*.exe' -Force -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    $backup = $null
    try {
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            $backup = Join-Path $InstallDir ".dotnetscope.old.$PID.exe"
            Move-FileWithRetry -Source $target -Destination $backup
        }
        Move-FileWithRetry -Source $stagedBinary -Destination $target
    }
    catch {
        if ($null -ne $backup -and -not (Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $backup)) {
            [IO.File]::Move($backup, $target)
        }
        Exit-Install 1 "falha ao instalar em ${target}: $($_.Exception.Message)"
    }
    $stagedBinary = $null
    if ($null -ne $backup) {
        Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    }

    Write-Output "dotnetscope $installedVersion instalado em $target"

    $pathEntries = @(
        [Environment]::GetEnvironmentVariable('Path', 'User'),
        [Environment]::GetEnvironmentVariable('Path', 'Machine')
    ) -join ';' -split ';' | Where-Object { $_ } | ForEach-Object { $_.TrimEnd('\') }
    if ($pathEntries -notcontains $InstallDir) {
        Write-Output ''
        Write-Output "Aviso: $InstallDir nao esta no PATH. Para adicionar ao PATH do usuario:"
        Write-Output "  [Environment]::SetEnvironmentVariable('Path', '$InstallDir;' + [Environment]::GetEnvironmentVariable('Path', 'User'), 'User')"
        # A linha do perfil usa $env:LOCALAPPDATA quando possivel: fica ASCII mesmo com nome de usuario acentuado,
        # que o Add-Content do Windows PowerShell 5.1 gravaria em ANSI.
        $pathExpression = "'$($InstallDir.Replace("'", "''"))'"
        if (-not [string]::IsNullOrEmpty($env:LOCALAPPDATA)) {
            $localPrefix = $env:LOCALAPPDATA.TrimEnd('\') + '\'
            if ($InstallDir.StartsWith($localPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                $relative = $InstallDir.Substring($localPrefix.Length)
                $pathExpression = "(`$env:LOCALAPPDATA + '\$($relative.Replace("'", "''"))')"
            }
        }
        $profileLine = "`$env:PATH = $pathExpression + ';' + `$env:PATH"
        Write-Output 'Sem alterar o registro (somente terminais PowerShell):'
        Write-Output '  New-Item -ItemType Directory -Force -Path (Split-Path $PROFILE.CurrentUserAllHosts) | Out-Null'
        Write-Output "  Add-Content -LiteralPath `$PROFILE.CurrentUserAllHosts -Value @('', '$($profileLine.Replace("'", "''"))')"
    }

    Write-Output ''
    Write-Output 'Proximo passo: dotnetscope list --available'
}
finally {
    $ProgressPreference = $previousProgress
    if ($null -ne $stagedBinary -and (Test-Path -LiteralPath $stagedBinary)) {
        Remove-Item -LiteralPath $stagedBinary -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $workDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
