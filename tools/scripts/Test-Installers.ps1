<#
.SYNOPSIS
Testa install.sh e install.ps1 contra um servidor HTTP local com os pacotes de release.

.DESCRIPTION
Copia os pacotes e o SHA256SUMS de -ReleaseDirectory para um diretorio temporario
servido em http://127.0.0.1:<porta> (python -m http.server), com duas variantes:
/ok (artefatos validos) e /tampered (SHA256SUMS adulterado). Em seguida:

- Linux: executa tests/installers/install-sh.sh em containers limpos com
  --network host, como root e como uid 65534 (HOME temporario).
- macOS: executa tests/installers/install-sh.sh nativamente com HOME temporario.
- Windows: executa os cenarios de install/install.ps1 com Windows PowerShell 5.1
  e, se disponivel, PowerShell 7, por -File e como scriptblock, com
  LOCALAPPDATA temporario.

Cenarios: ajuda, erros de uso, checksum adulterado sem efeitos, download falho,
instalacao, reinstalacao sem residuos, aviso de PATH, destino padrao e --global.

.PARAMETER ReleaseDirectory
Diretorio com os pacotes e SHA256SUMS. Padrao: artifacts/release.

.PARAMETER Image
Imagens para o Linux. Padrao: rockylinux:8 (glibc, curl) e, em host x64,
alpine:3.17 (musl, wget do BusyBox).

.PARAMETER Engine
Engine de container no Linux: docker ou podman. Padrao:
DOTNETSCOPE_CONTAINER_ENGINE, depois Docker e, na ausencia dele, Podman.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/Test-Installers.ps1 -Engine podman

.EXAMPLE
./tools/scripts/Test-Installers.ps1 -ReleaseDirectory dist

.NOTES
Requer PowerShell 7+, Python 3 e os pacotes do RID do host gerados por
Publish-Aot.ps1. Linux exige Docker ou Podman. Nada e instalado fora de
diretorios temporarios ou containers descartaveis.
Codigos: 0 sucesso; 1 cenario reprovado; 2 erro de uso.

.LINK
./Publish-Aot.ps1
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $ReleaseDirectory,

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

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
    $ReleaseDirectory = Join-Path $repositoryRoot 'artifacts/release'
}
$ReleaseDirectory = [IO.Path]::GetFullPath($ReleaseDirectory, (Get-Location).Path)
$shellInstaller = Join-Path $repositoryRoot 'install/install.sh'
$powerShellInstaller = Join-Path $repositoryRoot 'install/install.ps1'
$shellScenario = Join-Path $repositoryRoot 'tests/installers/install-sh.sh'

$packages = @(Get-ChildItem -LiteralPath $ReleaseDirectory -File -Filter 'dotnetscope-*' -ErrorAction SilentlyContinue)
if ($packages.Count -eq 0 -or -not (Test-Path -LiteralPath (Join-Path $ReleaseDirectory 'SHA256SUMS') -PathType Leaf)) {
    [Console]::Error.WriteLine("Pacotes ou SHA256SUMS ausentes em $ReleaseDirectory. Execute Publish-Aot.ps1. Use --help.")
    exit 2
}

$architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture
$archSuffix = if ($architecture -eq [Runtime.InteropServices.Architecture]::Arm64) { 'arm64' } else { 'x64' }

# No Windows, python3 pode ser o atalho da Microsoft Store; prefira python.
$pythonCandidates = if ($IsWindows) { @('python', 'python3') } else { @('python3', 'python') }
$python = $pythonCandidates | Where-Object { $null -ne (Get-Command $_ -ErrorAction SilentlyContinue) } | Select-Object -First 1
if ($null -eq $python) {
    [Console]::Error.WriteLine('Python 3 nao foi encontrado no PATH. Use --help.')
    exit 2
}

$containerEngine = $null
if ($IsLinux) {
    if ($null -eq $Image -or $Image.Count -eq 0) {
        $Image = @('docker.io/library/rockylinux:8')
        if ($archSuffix -eq 'x64') {
            $Image += 'docker.io/library/alpine:3.17'
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
}

$workDirectory = Join-Path ([IO.Path]::GetTempPath()) "dotnetscope-installers-$([Guid]::NewGuid().ToString('N'))"
$serveRoot = Join-Path $workDirectory 'serve'
$server = $null
$failures = [Collections.Generic.List[string]]::new()

function Add-Failure {
    param([string] $Message)
    $failures.Add($Message)
    [Console]::Error.WriteLine("FALHA: $Message")
}

function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try {
        return ([Net.IPEndPoint] $listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

try {
    foreach ($variant in @('ok', 'tampered')) {
        $null = New-Item -ItemType Directory -Force -Path (Join-Path $serveRoot $variant)
        foreach ($package in $packages) {
            Copy-Item -LiteralPath $package.FullName -Destination (Join-Path $serveRoot $variant)
        }
    }
    Copy-Item -LiteralPath (Join-Path $ReleaseDirectory 'SHA256SUMS') -Destination (Join-Path $serveRoot 'ok/SHA256SUMS')
    $tampered = (Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'SHA256SUMS')) -replace '^[0-9a-f]{64}', ('0' * 64)
    [IO.File]::WriteAllText((Join-Path $serveRoot 'tampered/SHA256SUMS'), (($tampered -join "`n") + "`n"))

    $port = Get-FreePort
    $server = Start-Process -FilePath $python -PassThru -NoNewWindow `
        -ArgumentList @('-m', 'http.server', "$port", '--bind', '127.0.0.1', '--directory', $serveRoot) `
        -RedirectStandardError (Join-Path $workDirectory 'server.err') `
        -RedirectStandardOutput (Join-Path $workDirectory 'server.out')
    $baseUrl = "http://127.0.0.1:$port/ok"
    $tamperedUrl = "http://127.0.0.1:$port/tampered"

    $ready = $false
    foreach ($attempt in 1..50) {
        try {
            $null = Invoke-WebRequest -Uri "$baseUrl/SHA256SUMS" -TimeoutSec 2
            $ready = $true
            break
        }
        catch {
            Start-Sleep -Milliseconds 200
        }
    }
    if (-not $ready) {
        throw "Servidor HTTP local nao respondeu em $baseUrl."
    }
    Write-Output "Servindo $($packages.Count) pacote(s) em http://127.0.0.1:$port"

    if ($IsLinux) {
        foreach ($name in $Image) {
            foreach ($user in @('0:0', '65534:65534')) {
                Write-Output "`n== install.sh em $name (usuario $user)"
                $environment = @('--env', 'INSTALLER=/installer/install.sh', '--env', "BASE_URL=$baseUrl", '--env', "TAMPERED_URL=$tamperedUrl")
                # O servidor e local: anula proxies herdados do host (o wget do BusyBox ignora no_proxy).
                foreach ($proxyVariable in @('http_proxy', 'HTTP_PROXY', 'https_proxy', 'HTTPS_PROXY', 'all_proxy', 'ALL_PROXY')) {
                    $environment += @('--env', "$proxyVariable=")
                }
                if ($user -ne '0:0') {
                    $environment += @('--env', 'HOME=/tmp/home')
                }
                & $containerEngine run --rm --network host --user $user `
                    --volume "${shellInstaller}:/installer/install.sh:ro" `
                    --volume "${shellScenario}:/installer/test.sh:ro" `
                    @environment $name sh /installer/test.sh
                if ($LASTEXITCODE -ne 0) {
                    Add-Failure "install.sh em $name como $user (codigo $LASTEXITCODE)"
                }
            }
        }
    }
    elseif ($IsMacOS) {
        Write-Output "`n== install.sh no macOS"
        $fakeHome = Join-Path $workDirectory 'home'
        $null = New-Item -ItemType Directory -Path $fakeHome
        $saved = @{ HOME = $env:HOME; INSTALLER = $env:INSTALLER; BASE_URL = $env:BASE_URL; TAMPERED_URL = $env:TAMPERED_URL }
        try {
            $env:HOME = $fakeHome
            $env:INSTALLER = $shellInstaller
            $env:BASE_URL = $baseUrl
            $env:TAMPERED_URL = $tamperedUrl
            & sh $shellScenario
            if ($LASTEXITCODE -ne 0) {
                Add-Failure "install.sh no macOS (codigo $LASTEXITCODE)"
            }
        }
        finally {
            foreach ($key in $saved.Keys) {
                [Environment]::SetEnvironmentVariable($key, $saved[$key])
            }
        }
    }
    else {
        $shells = @('powershell.exe', 'pwsh.exe') | Where-Object { $null -ne (Get-Command $_ -ErrorAction SilentlyContinue) }
        foreach ($shell in $shells) {
            Write-Output "`n== install.ps1 com $shell"
            $scratch = Join-Path $workDirectory ([IO.Path]::GetFileNameWithoutExtension($shell))
            $null = New-Item -ItemType Directory -Path $scratch

            function Invoke-Installer {
                param([string[]] $Arguments)
                $output = & $shell -NoLogo -NoProfile -ExecutionPolicy Bypass -File $powerShellInstaller @Arguments 2>&1 | Out-String
                return [pscustomobject]@{ Code = $LASTEXITCODE; Output = $output }
            }

            function Test-Runs {
                param([string] $Path)
                if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
                    return $false
                }
                try {
                    $null = & $Path --version
                    return $LASTEXITCODE -eq 0
                }
                catch {
                    return $false
                }
            }

            function Assert-Case {
                param([string] $Name, [bool] $Condition, [object] $Result)
                if ($Condition) {
                    Write-Output "ok: $Name"
                }
                else {
                    Add-Failure "$shell - $Name (codigo $($Result.Code))`n$($Result.Output)"
                }
            }

            $result = Invoke-Installer @('-Help')
            Assert-Case -Name '-Help' -Condition ($result.Code -eq 0 -and $result.Output -match 'InstallDir') -Result $result
            $result = Invoke-Installer @('--help')
            Assert-Case -Name '--help' -Condition ($result.Code -eq 0) -Result $result
            $result = Invoke-Installer @('--bogus')
            Assert-Case -Name 'argumento desconhecido' -Condition ($result.Code -eq 2) -Result $result
            $result = Invoke-Installer @('-BaseUrl', 'http://example.com/x')
            Assert-Case -Name 'http fora do loopback' -Condition ($result.Code -eq 2) -Result $result
            $result = Invoke-Installer @('-BaseUrl', $baseUrl, '-Version', '0.1.0')
            Assert-Case -Name '-BaseUrl com -Version' -Condition ($result.Code -eq 2) -Result $result
            $result = Invoke-Installer @('-BaseUrl', $baseUrl, '-InstallDir', 'relativo')
            Assert-Case -Name 'diretorio relativo' -Condition ($result.Code -eq 2) -Result $result

            $tamperedDir = Join-Path $scratch 'tampered'
            $result = Invoke-Installer @('-BaseUrl', $tamperedUrl, '-InstallDir', $tamperedDir)
            Assert-Case -Name 'checksum adulterado rejeitado sem efeitos' -Condition ($result.Code -eq 1 -and $result.Output -match 'nao confere' -and -not (Test-Path -LiteralPath $tamperedDir)) -Result $result

            $result = Invoke-Installer @('-BaseUrl', "$baseUrl/inexistente", '-InstallDir', (Join-Path $scratch 'missing'))
            Assert-Case -Name 'download falho rejeitado' -Condition ($result.Code -eq 1 -and -not (Test-Path -LiteralPath (Join-Path $scratch 'missing'))) -Result $result

            $binDir = Join-Path $scratch 'bin'
            $result = Invoke-Installer @('-BaseUrl', $baseUrl, '-InstallDir', $binDir)
            $installed = Join-Path $binDir 'dotnetscope.exe'
            $runs = Test-Runs -Path $installed
            Assert-Case -Name 'instalacao em diretorio explicito' -Condition ($result.Code -eq 0 -and $runs -and $result.Output -match 'nao esta no PATH') -Result $result

            Set-Content -LiteralPath $installed -Value 'versao anterior'
            $result = Invoke-Installer @('-BaseUrl', $baseUrl, '-InstallDir', $binDir)
            $runs = Test-Runs -Path $installed
            $entries = @(Get-ChildItem -LiteralPath $binDir -Force | ForEach-Object Name)
            Assert-Case -Name 'reinstalacao sem residuos' -Condition ($result.Code -eq 0 -and $runs -and $entries.Count -eq 1) -Result $result

            $savedLocalAppData = $env:LOCALAPPDATA
            try {
                $env:LOCALAPPDATA = Join-Path $scratch 'localappdata'
                $result = Invoke-Installer @('-BaseUrl', $baseUrl)
                Assert-Case -Name 'diretorio padrao %LOCALAPPDATA%\Programs\dotnetscope' -Condition ($result.Code -eq 0 -and (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'Programs\dotnetscope\dotnetscope.exe'))) -Result $result
            }
            finally {
                $env:LOCALAPPDATA = $savedLocalAppData
            }

            # Como scriptblock (irm | Create), erros viram excecao e a sessao continua.
            $scriptDir = Join-Path $scratch 'scriptblock'
            $command = "`$sb = [scriptblock]::Create((Get-Content -Raw -LiteralPath '$powerShellInstaller')); " +
                "try { & `$sb -BaseUrl '$tamperedUrl' -InstallDir '$scriptDir' } catch { 'excecao capturada' }; " +
                "& `$sb -BaseUrl '$baseUrl' -InstallDir '$scriptDir'; 'sessao continua'"
            $output = & $shell -NoLogo -NoProfile -ExecutionPolicy Bypass -Command $command 2>&1 | Out-String
            $result = [pscustomobject]@{ Code = $LASTEXITCODE; Output = $output }
            Assert-Case -Name 'execucao como scriptblock' -Condition ($result.Code -eq 0 -and $output -match 'excecao capturada' -and $output -match 'sessao continua' -and (Test-Path -LiteralPath (Join-Path $scriptDir 'dotnetscope.exe'))) -Result $result
        }
    }
}
finally {
    if ($null -ne $server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        $server.WaitForExit()
    }
    Remove-Item -LiteralPath $workDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    [Console]::Error.WriteLine("`nInstaladores reprovados: $($failures.Count) falha(s).")
    exit 1
}

Write-Output "`nInstaladores aprovados."
