<#
.SYNOPSIS
Cenario E2E do escopo user no Windows: instala SDK e runtime oficiais, valida o registro e os perfis do PowerShell e reverte tudo.

.DESCRIPTION
Executado por tools/scripts/Invoke-WindowsE2E.ps1 somente em runner efemero, porque altera
HKCU\Environment, o Path da maquina (HKLM) e os perfis do PowerShell do usuario. Etapas:

1. DOTNET_ROOT de terceiro no registro: install falha com codigo 6 sem alterar nada.
2. Com o dotnet da maquina no Path: install configura o registro e avisa que o Path da maquina
   tem precedencia (verificacao de terminal novo nao corresponde).
3. Sem o dotnet da maquina: env apply fica inalterado e o terminal novo (ambiente montado a partir
   do registro, como o Explorer faz) resolve dotnet para a raiz do usuario e lista o SDK.
4. Idempotencia, runtime adicional, list --installed com origens e doctor sem erros.
5. env remove restaura HKCU\Environment exatamente; env apply reaplica.
6. --windows-env-target profile configura os perfis do PowerShell sem alterar o registro; um pwsh novo
   resolve dotnet pela raiz do usuario; o registro e reaplicado e coexiste com os perfis.
7. uninstall dos dois componentes remove as duas configuracoes e restaura o registro e os perfis
   exatamente.

.PARAMETER Binary
Caminho do dotnetscope.exe publicado.

.PARAMETER Sdk
Versao do SDK a instalar. Padrao: lts.

.PARAMETER Runtime
Versao do runtime a instalar. Padrao: 9.0.

.NOTES
Codigos: 0 sucesso; 1 falha.
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory)]
    [string] $Binary,

    [string] $Sdk = 'lts',

    [string] $Runtime = '9.0'
)

$ErrorActionPreference = 'Stop'
$userKeyPath = 'Environment'
$machineKeyPath = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
$root = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$work = Join-Path ([IO.Path]::GetTempPath()) "dotnetscope-e2e-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $work

function Fail([string] $Message) {
    throw "FALHA: $Message"
}

function Pass([string] $Message) {
    Write-Output "ok: $Message"
}

function Get-RegistryVariable([Microsoft.Win32.RegistryKey] $Hive, [string] $Key, [string] $Name) {
    $handle = $Hive.OpenSubKey($Key)
    try {
        $value = $handle.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if ($null -eq $value) { return $null }
        return [pscustomobject]@{ Value = $value; Kind = $handle.GetValueKind($Name).ToString() }
    }
    finally {
        $handle.Dispose()
    }
}

function Get-UserSnapshot {
    $result = [ordered]@{}
    foreach ($name in 'Path', 'DOTNET_ROOT', 'DOTNET_CLI_TELEMETRY_OPTOUT') {
        $result[$name] = Get-RegistryVariable ([Microsoft.Win32.Registry]::CurrentUser) $userKeyPath $name
    }
    return ($result | ConvertTo-Json -Compress)
}

function Set-UserVariable([string] $Name, $Value) {
    $handle = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($userKeyPath, $true)
    try {
        if ($null -eq $Value) { $handle.DeleteValue($Name, $false) }
        else { $handle.SetValue($Name, $Value.Value, [Microsoft.Win32.RegistryValueKind]$Value.Kind) }
    }
    finally {
        $handle.Dispose()
    }
}

function Invoke-Scope([string[]] $Arguments, [int] $Expected = 0) {
    $errorFile = Join-Path $work 'stderr.txt'
    $output = & $Binary @Arguments 2> $errorFile
    $code = $LASTEXITCODE
    $stderr = Get-Content -LiteralPath $errorFile -Raw
    if ($code -ne $Expected) {
        Fail "dotnetscope $($Arguments -join ' '): codigo $code, esperado $Expected`n$($output -join "`n")`n$stderr"
    }
    return [pscustomobject]@{ Output = ($output -join "`n"); Error = "$stderr" }
}

# Ambiente de um terminal novo aberto pelo Explorer: Path da maquina seguido do Path do usuario, expandidos.
function Invoke-NewTerminal([string] $Command) {
    $machine = (Get-RegistryVariable ([Microsoft.Win32.Registry]::LocalMachine) $machineKeyPath 'Path').Value
    $user = Get-RegistryVariable ([Microsoft.Win32.Registry]::CurrentUser) $userKeyPath 'Path'
    $dotnetRoot = Get-RegistryVariable ([Microsoft.Win32.Registry]::CurrentUser) $userKeyPath 'DOTNET_ROOT'
    $startInfo = [Diagnostics.ProcessStartInfo]::new('cmd.exe', "/d /c $Command")
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.Environment['PATH'] = [Environment]::ExpandEnvironmentVariables("$machine;$($user.Value)")
    $startInfo.Environment.Remove('DOTNET_ROOT') | Out-Null
    if ($null -ne $dotnetRoot) {
        $startInfo.Environment['DOTNET_ROOT'] = [Environment]::ExpandEnvironmentVariables($dotnetRoot.Value)
    }
    $process = [Diagnostics.Process]::Start($startInfo)
    $output = $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    return [pscustomobject]@{ Code = $process.ExitCode; Output = $output }
}

# Terminal PowerShell novo: mesmo Path de Invoke-NewTerminal, carregando os perfis do usuario.
function Invoke-NewPowerShell([string] $Command) {
    $machine = (Get-RegistryVariable ([Microsoft.Win32.Registry]::LocalMachine) $machineKeyPath 'Path').Value
    $user = Get-RegistryVariable ([Microsoft.Win32.Registry]::CurrentUser) $userKeyPath 'Path'
    $startInfo = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    foreach ($argument in '-NoLogo', '-NonInteractive', '-Command', $Command) { $startInfo.ArgumentList.Add($argument) }
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.Environment['PATH'] = [Environment]::ExpandEnvironmentVariables("$machine;$($user.Value)")
    $startInfo.Environment.Remove('DOTNET_ROOT') | Out-Null
    $startInfo.Environment.Remove('PSExecutionPolicyPreference') | Out-Null
    $process = [Diagnostics.Process]::Start($startInfo)
    $output = $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    return [pscustomobject]@{ Code = $process.ExitCode; Output = $output }
}

$documents = [Environment]::GetFolderPath('MyDocuments')
$profileFiles = @(
    (Join-Path $documents 'WindowsPowerShell\profile.ps1'),
    (Join-Path $documents 'PowerShell\profile.ps1')
)

function Get-ProfileSnapshot {
    return ($profileFiles | ForEach-Object {
            if (Test-Path -LiteralPath $_) { [Convert]::ToBase64String([IO.File]::ReadAllBytes($_)) } else { '<ausente>' }
        }) -join '|'
}

$originalProfiles = Get-ProfileSnapshot
$envScript = Join-Path $env:LOCALAPPDATA 'dotnetscope\env.ps1'
$originalUser = Get-UserSnapshot
$originalRootValue = Get-RegistryVariable ([Microsoft.Win32.Registry]::CurrentUser) $userKeyPath 'DOTNET_ROOT'
$machineVariable = Get-RegistryVariable ([Microsoft.Win32.Registry]::LocalMachine) $machineKeyPath 'Path'
$rootExisted = Test-Path -LiteralPath (Join-Path $root 'dotnet.exe')
Write-Output "Raiz: $root (dotnet.exe pre-existente: $rootExisted)"

try {
    # 1. Conflito com DOTNET_ROOT de terceiro.
    Set-UserVariable 'DOTNET_ROOT' ([pscustomobject]@{ Value = 'C:\terceiro\dotnet'; Kind = 'String' })
    $conflict = Invoke-Scope @('install', 'sdk', $Sdk, '--yes') -Expected 6
    if ($conflict.Error -notmatch 'DOTNET_ROOT') { Fail 'conflito sem mensagem de DOTNET_ROOT' }
    if (Test-Path -LiteralPath (Join-Path $root 'sdk')) { if (-not $rootExisted) { Fail 'conflito instalou o SDK' } }
    Set-UserVariable 'DOTNET_ROOT' $originalRootValue
    if ((Get-UserSnapshot) -ne $originalUser) { Fail 'conflito alterou o registro' }
    Pass 'DOTNET_ROOT de terceiro falha com codigo 6 sem alterar nada'

    # 2. Instalacao com o dotnet da maquina no Path.
    $install = Invoke-Scope @('install', 'sdk', $Sdk, '--yes', '--output', 'json')
    $json = $install.Output | ConvertFrom-Json
    if ($json.status -ne 'installed') { Fail "status $($json.status)" }
    if ($json.artifact.fileName -notlike '*.zip') { Fail "artefato $($json.artifact.fileName)" }
    if ($json.environment.status -ne 'applied') { Fail "ambiente $($json.environment.status)" }
    $sdkVersion = $json.version
    $userPath = Get-RegistryVariable ([Microsoft.Win32.Registry]::CurrentUser) $userKeyPath 'Path'
    if (-not $userPath.Value.StartsWith("$root;", [StringComparison]::OrdinalIgnoreCase)) { Fail "Path do usuario: $($userPath.Value)" }
    if ($userPath.Kind -ne 'ExpandString' -and $null -eq ($originalUser | ConvertFrom-Json).Path) { Fail "tipo do Path: $($userPath.Kind)" }
    if ((Get-RegistryVariable ([Microsoft.Win32.Registry]::CurrentUser) $userKeyPath 'DOTNET_ROOT').Value -ne $root) { Fail 'DOTNET_ROOT nao gravado' }
    $machineHasDotnet = @(([Environment]::ExpandEnvironmentVariables($machineVariable.Value) -split ';') | Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $_ 'dotnet.exe')) }).Count -gt 0
    if ($machineHasDotnet) {
        if ($json.environment.loginShell.matches) { Fail 'verificacao deveria indicar o dotnet da maquina' }
        if ($install.Error -notmatch 'Path da m') { Fail "aviso de precedencia ausente: $($install.Error)" }
        Pass "SDK $sdkVersion instalado; aviso de precedencia do Path da maquina"
    }
    else {
        Pass "SDK $sdkVersion instalado (sem dotnet no Path da maquina)"
    }

    # 3. Sem o dotnet da maquina, um terminal novo resolve para a raiz do usuario.
    $filtered = (($machineVariable.Value -split ';') | Where-Object { -not ($_ -and (Test-Path -LiteralPath (Join-Path ([Environment]::ExpandEnvironmentVariables($_)) 'dotnet.exe'))) }) -join ';'
    $machineHandle = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($machineKeyPath, $true)
    $machineHandle.SetValue('Path', $filtered, [Microsoft.Win32.RegistryValueKind]$machineVariable.Kind)
    $machineHandle.Dispose()
    $apply = (Invoke-Scope @('env', 'apply', '--output', 'json')).Output | ConvertFrom-Json
    if ($apply.environment.status -ne 'unchanged') { Fail "env apply: $($apply.environment.status)" }
    if (-not $apply.environment.loginShell.matches) { Fail "terminal novo resolve para $($apply.environment.loginShell.dotnet)" }
    $where = Invoke-NewTerminal 'where dotnet'
    $first = ($where.Output -split "`r?`n")[0]
    if ($first -ne (Join-Path $root 'dotnet.exe')) { Fail "where dotnet: $($where.Output)" }
    $sdks = Invoke-NewTerminal 'dotnet --list-sdks'
    if ($sdks.Code -ne 0 -or $sdks.Output -notmatch [regex]::Escape("$sdkVersion [$root\sdk]")) { Fail "dotnet --list-sdks: $($sdks.Output)" }
    Pass 'terminal novo resolve dotnet para a raiz do usuario e lista o SDK'

    # 4. Idempotencia, runtime, inventario e doctor.
    $again = (Invoke-Scope @('install', 'sdk', $sdkVersion, '--yes', '--output', 'json')).Output | ConvertFrom-Json
    if ($again.status -ne 'alreadyInstalled' -or $again.environment.status -ne 'unchanged') { Fail "reinstalacao: $($again.status)/$($again.environment.status)" }
    $runtimeJson = (Invoke-Scope @('install', 'runtime', $Runtime, '--yes', '--output', 'json')).Output | ConvertFrom-Json
    $runtimeVersion = $runtimeJson.version
    $inventory = (Invoke-Scope @('list', '--installed', '--output', 'json')).Output | ConvertFrom-Json
    $own = @($inventory.installations | Where-Object { $_.origin -eq 'dotnetscope' } | ForEach-Object { "$($_.component) $($_.version)" })
    foreach ($expected in "sdk $sdkVersion", "runtime $runtimeVersion") {
        if ($own -notcontains $expected) { Fail "list --installed sem $expected (origem dotnetscope): $($own -join ', ')" }
    }
    $null = Invoke-Scope @('doctor')
    Pass "idempotente; runtime $runtimeVersion; list --installed e doctor"

    # 5. env remove restaura o registro; env apply reaplica.
    $null = Invoke-Scope @('env', 'remove')
    if ((Get-UserSnapshot) -ne $originalUser) { Fail "env remove nao restaurou o registro: $(Get-UserSnapshot)" }
    $reapply = (Invoke-Scope @('env', 'apply', '--output', 'json')).Output | ConvertFrom-Json
    if ($reapply.environment.status -ne 'applied') { Fail "env apply: $($reapply.environment.status)" }
    Pass 'env remove restaura HKCU\Environment exatamente; env apply reaplica'

    # 6. Destino profile: perfis do PowerShell, sem alterar o registro.
    $null = Invoke-Scope @('env', 'remove')
    $profileApply = (Invoke-Scope @('env', 'apply', '--windows-env-target', 'profile', '--output', 'json')).Output | ConvertFrom-Json
    if ($profileApply.environment.status -ne 'applied') { Fail "env apply profile: $($profileApply.environment.status)" }
    if ($profileApply.environment.script -ne $envScript) { Fail "script: $($profileApply.environment.script)" }
    if ((Get-UserSnapshot) -ne $originalUser) { Fail "o destino profile alterou o registro: $(Get-UserSnapshot)" }
    if (-not $profileApply.environment.loginShell.matches) { Fail "PowerShell novo resolve para $($profileApply.environment.loginShell.dotnet)" }
    $pwshSdks = Invoke-NewPowerShell 'dotnet --list-sdks'
    if ($pwshSdks.Code -ne 0 -or $pwshSdks.Output -notmatch [regex]::Escape("$sdkVersion [$root\sdk]")) { Fail "pwsh novo: $($pwshSdks.Output)" }
    $null = Invoke-Scope @('doctor')
    $coexist = Invoke-Scope @('env', 'apply', '--output', 'json')
    if (($coexist.Output | ConvertFrom-Json).environment.status -ne 'applied') { Fail 'env apply no registro apos o destino profile' }
    if ($coexist.Error -notmatch 'env remove') { Fail "aviso de coexistencia ausente: $($coexist.Error)" }
    Pass 'destino profile configura o PowerShell sem registro; registro reaplicado coexiste'

    # 7. Desinstalacao completa.
    $null = Invoke-Scope @('uninstall', 'runtime', $runtimeVersion)
    $last = (Invoke-Scope @('uninstall', 'sdk', $sdkVersion, '--output', 'json')).Output | ConvertFrom-Json
    if ($last.environment -ne 'removed') { Fail "uninstall nao removeu o ambiente: $($last.environment)" }
    if ((Get-UserSnapshot) -ne $originalUser) { Fail "uninstall nao restaurou o registro: $(Get-UserSnapshot)" }
    foreach ($leaf in "sdk\$sdkVersion", "shared\Microsoft.NETCore.App\$runtimeVersion") {
        if (Test-Path -LiteralPath (Join-Path $root $leaf)) { Fail "$leaf permaneceu" }
    }
    if (-not $rootExisted -and (Test-Path -LiteralPath (Join-Path $root 'dotnet.exe'))) { Fail 'dotnet.exe permaneceu' }
    if ((Get-ProfileSnapshot) -ne $originalProfiles) { Fail 'uninstall nao restaurou os perfis do PowerShell' }
    if (Test-Path -LiteralPath $envScript) { Fail 'env.ps1 permaneceu' }
    Pass 'uninstall remove os componentes e restaura o registro e os perfis exatamente'
}
finally {
    $machineHandle = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($machineKeyPath, $true)
    $machineHandle.SetValue('Path', $machineVariable.Value, [Microsoft.Win32.RegistryValueKind]$machineVariable.Kind)
    $machineHandle.Dispose()
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'E2E Windows aprovado.'
