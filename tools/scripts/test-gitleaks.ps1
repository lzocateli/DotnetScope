<#
.SYNOPSIS
Valida as regras Gitleaks sem persistir credenciais sinteticas.

.DESCRIPTION
Envia fixtures em memoria ao Gitleaks containerizado e verifica findings por
linha e regra. Testa literais positivos, referencias runtime negativas e
concatenacoes que nao podem ser ignoradas. Nao acessa secrets externos.

.PARAMETER RemainingArguments
Aceita somente --help. Sem argumentos, executa todos os casos.

.EXAMPLE
./tools/scripts/test-gitleaks.ps1 --help

.EXAMPLE
./tools/scripts/test-gitleaks.ps1

.NOTES
Requer PowerShell 7+, Docker ou Podman ativo e docker.io/lzocateli/gitleaks:8.30.1.
DOTNETSCOPE_CONTAINER_ENGINE força o engine (docker ou podman).
O scanner executa em container read-only, com configuracao montada read-only.
Fixtures sao enviadas por stdin; o relatorio temporario redigido e removido ao fim.

.LINK
../../.github/SECRET-SCANNING.md
#>
#Requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RemainingArguments = @($RemainingArguments | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

if ($RemainingArguments -contains '--help') {
    Get-Help $PSCommandPath -Full
    exit 0
}
if ($RemainingArguments.Count -gt 0) {
    [Console]::Error.WriteLine('Argumento desconhecido. Use --help.')
    exit 2
}

function New-CredentialAssignment {
    param([string] $Key, [string] $Value)
    return $Key + '=' + $Value
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$configuration = Join-Path $repositoryRoot '.gitleaks.toml'
$credential = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
$connectionRule = 'connection-string-credential'
$cases = @(
    @{ Name = 'unquoted password'; Text = New-CredentialAssignment 'Password' $credential; Rule = $connectionRule }
    @{ Name = 'single-quoted password'; Text = New-CredentialAssignment 'pwd' ("'" + $credential + "'"); Rule = $connectionRule }
    @{ Name = 'double-quoted secret'; Text = New-CredentialAssignment 'secret' ('"' + $credential + '"'); Rule = $connectionRule }
    @{ Name = 'environment assignment'; Text = New-CredentialAssignment 'DATABASE_PASSWORD' $credential; Rule = $connectionRule }
    @{ Name = 'runtime concatenation'; Text = New-CredentialAssignment 'Password' ('$runtime + "' + $credential + '"'); Rule = $connectionRule }
    @{ Name = 'interpolated literal suffix'; Text = New-CredentialAssignment 'Password' ('"$runtime-' + $credential + '"'); Rule = $connectionRule }
    @{ Name = 'Basic header'; Text = 'Authorization: Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('fixture:' + $credential)); Rule = 'basic-auth-header' }
    @{ Name = 'Bearer header'; Text = 'Authorization: Bearer ' + $credential; Rule = 'bearer-auth-header' }
    @{ Name = 'Docker auth'; Text = '{"auth":"' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('fixture:' + $credential)) + '"}'; Rule = 'docker-auth' }
    @{ Name = 'default GitHub rule'; Text = 'ghp_' + $credential.Substring(0, 36); Rule = 'github-pat' }
    @{ Name = 'PowerShell reference'; Text = New-CredentialAssignment 'Password' '$script:adminPasswordPlain }'; Rule = $null }
    @{ Name = 'Compose reference'; Text = New-CredentialAssignment 'Password' '${DATABASE_PASSWORD}'; Rule = $null }
    @{ Name = 'connection interpolation'; Text = New-CredentialAssignment 'Password' '$(ConvertTo-NpgsqlValue $postgresPassword)'; Rule = $null }
    @{ Name = 'secret helper'; Text = New-CredentialAssignment 'clientSecret' 'Get-EnvironmentSecret @("APP_CLIENT_SECRET")'; Rule = $null }
    @{ Name = 'helper with literal suffix'; Text = New-CredentialAssignment 'clientSecret' ('Get-EnvironmentSecret @("APP_CLIENT_SECRET") + "' + $credential + '"'); Rule = $connectionRule }
    @{ Name = 'variable secret helper'; Text = New-CredentialAssignment 'initialPassword' 'Get-EnvironmentSecret @($InitialUserPasswordVariable)'; Rule = $null }
    @{ Name = 'external file helper'; Text = New-CredentialAssignment 'postgresPassword' 'Get-EnvironmentFileValue -Path $IdentityEnvFile -Name "POSTGRES_PASSWORD"'; Rule = $null }
    @{ Name = 'SecureString reference'; Text = New-CredentialAssignment 'AdminPassword' 'ConvertTo-SecureString $adminPasswordValue -AsPlainText -Force'; Rule = $null }
    @{ Name = 'C# environment reference'; Text = New-CredentialAssignment 'certificatePassword' 'Environment.GetEnvironmentVariable("APP_CERTIFICATE_PASSWORD")'; Rule = $null }
    @{ Name = 'Python environment reference'; Text = New-CredentialAssignment 'PASSWORD' 'os.environ.get("APP_E2E_PASSWORD")'; Rule = $null }
    @{ Name = 'Python credentials reference'; Text = New-CredentialAssignment 'password' '_require_credentials()'; Rule = $null }
    @{ Name = 'empty single-quoted value'; Text = New-CredentialAssignment 'Password' "''"; Rule = $null }
    @{ Name = 'empty double-quoted value'; Text = New-CredentialAssignment 'Password' '""'; Rule = $null }
    @{ Name = 'documented placeholder'; Text = New-CredentialAssignment 'Password' '"<certificate-secret>"'; Rule = $null }
    @{ Name = 'C# await expression'; Text = 'using (var secret = await http.PostAsync("v4.0.0/libpod/secrets/create", content))'; Rule = $null }
    @{ Name = 'C# range indexing'; Text = 'new { username = decoded[..separator], password = decoded[(separator + 1)..] }'; Rule = $null }
    @{ Name = 'JS function call'; Text = "var password = h(Section, { title: 'Senha' })"; Rule = $null }
    @{ Name = 'JS boolean coercion'; Text = 'const secret = !!p.secret;'; Rule = $null }
    @{ Name = 'template placeholder'; Text = '<dc-import secret="{{v.secret}}" mono="{{yes}}">'; Rule = $null }
    @{ Name = 'documented key list'; Text = 'Mascaramento (`password=`, `secret=`, `token=`)'; Rule = $null }
    @{ Name = 'synthetic canary'; Text = '"DATABASE_PASSWORD={{Canary}}-env"'; Rule = $null }
    @{ Name = 'literal with parenthesis'; Text = New-CredentialAssignment 'Password' ('Ab' + $credential + '(1'); Rule = $connectionRule }
    @{ Name = 'literal with bracket'; Text = New-CredentialAssignment 'Password' ('Ab' + $credential + '[x'); Rule = $connectionRule }
)

$containerEngine = $env:DOTNETSCOPE_CONTAINER_ENGINE
if ([string]::IsNullOrWhiteSpace($containerEngine)) {
    $containerEngine = @('docker', 'podman') |
        Where-Object { $null -ne (Get-Command $_ -ErrorAction SilentlyContinue) } |
        Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($containerEngine) -or $null -eq (Get-Command $containerEngine -ErrorAction SilentlyContinue)) {
    throw 'Docker ou Podman nao foi encontrado no PATH. Use --help.'
}

$reportDirectory = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
$reportPath = Join-Path $reportDirectory 'findings.json'
if (Test-Path -LiteralPath $reportDirectory) {
    throw 'O diretorio temporario ja existe; nenhum arquivo sera sobrescrito.'
}
$previousErrorActionPreference = $ErrorActionPreference
$previousNativePreference = $PSNativeCommandUseErrorActionPreference
try {
    New-Item -ItemType Directory -Path $reportDirectory | Out-Null
    $ErrorActionPreference = 'Continue'
    $PSNativeCommandUseErrorActionPreference = $false
    $cases.Text | & $containerEngine run --rm -i --read-only `
        --volume "${configuration}:/gitleaks.toml:ro" `
        --volume "${reportDirectory}:/reports" `
        docker.io/lzocateli/gitleaks:8.30.1 stdin --config /gitleaks.toml `
        --redact=100 --no-banner --log-level error --exit-code 0 `
        --report-format json --report-path /reports/findings.json
    if ($LASTEXITCODE -ne 0) {
        throw "Gitleaks nao executou corretamente: codigo $LASTEXITCODE."
    }
    $ErrorActionPreference = $previousErrorActionPreference
    $findings = @(Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json | ForEach-Object { $_ } | Where-Object { $null -ne $_ })
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
    $PSNativeCommandUseErrorActionPreference = $previousNativePreference
    if (Test-Path -LiteralPath $reportDirectory) {
        Remove-Item -LiteralPath $reportDirectory -Recurse -Force
    }
}

$failures = @()
for ($index = 0; $index -lt $cases.Count; $index++) {
    $case = $cases[$index]
    $lineFindings = @($findings | Where-Object { $_.StartLine -eq $index + 1 })
    if ($null -eq $case.Rule) {
        if ($lineFindings.Count -ne 0) {
            $failures += $case.Name
        }
    }
    elseif (@($lineFindings | Where-Object { $_.RuleID -eq $case.Rule }).Count -eq 0) {
        $failures += $case.Name
    }
}

if ($failures.Count -gt 0) {
    throw "Falhas de regressao Gitleaks: $($failures -join ', ')."
}
Write-Output "Gitleaks: $($cases.Count) casos passaram; literais bloqueados e referencias runtime aceitas."
