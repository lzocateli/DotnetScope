#!/usr/bin/env pwsh
# Gerenciado pelo Altivy.Groundwork (componente deploy). Não edite: mudanças vão para o catálogo.

<#
.SYNOPSIS
Guard do deploy: aceita somente GitHub Release publicada, íntegra e compatível com rollback.

.DESCRIPTION
Primeiro job de gw-deploy.yml, executado no checkout da tag. Falha fechado se:
- a tag não for vX.Y.Z[-pre] ou o Environment não estiver em deploy.environments;
- for auto-deploy (ou ator bot) em production, ou prerelease em production;
- for prerelease que não seja candidata vX.Y.Z-rc.N, ou candidata fora do Environment de
  preview (deploy.previewEnvironment);
- em production, com preview.requiredForRelease, não houver candidata da mesma versão
  validada no Environment de preview cujo diff até a tag toque só versionFiles,
  CHANGELOG.md, .release-please-manifest.json e os changelog-path/extra-files do
  release-please-config.json, nem rodapé Preview-Waiver desde a última release estável
  (docs/estrategia-de-release.md);
- a GitHub Release não existir ou for rascunho;
- o commit da tag não pertencer a origin/<branch padrão>;
- o release-manifest.json estiver ausente, inválido, marcado com attestation none
  ("attestations": false em .github/ci-cd.json), sem atestação válida de
  gw-release.yml (gw-preview.yml para candidatas), ou alguma imagem listada não tiver
  atestação de proveniência;
- o alvo for menor que a versão implantada e menor que o rollbackMinimumVersion do
  manifesto da versão implantada (trava de rollback sem bypass).
Grava o manifesto validado e as saídas version, tag e rollback.

.PARAMETER Tag
Tag da release (GITHUB_REF_NAME).

.PARAMETER Environment
Environment de deploy solicitado.

.PARAMETER Trigger
manual ou release (auto-deploy).

.PARAMETER Actor
Ator que disparou o workflow (GITHUB_ACTOR).

.PARAMETER Repository
Repositório owner/nome.

.PARAMETER DefaultBranch
Branch padrão do repositório.

.PARAMETER ConfigPath
Configuração do pipeline na tag. Padrão: .github/ci-cd.json.

.PARAMETER ManifestPath
Destino do manifesto validado.

.PARAMETER OutputPath
Arquivo de saída chave=valor (por exemplo $env:GITHUB_OUTPUT).

.PARAMETER SummaryPath
Arquivo Markdown de resumo (por exemplo $env:GITHUB_STEP_SUMMARY).

.EXAMPLE
./tools/scripts/ci/Assert-DeployableRelease.ps1 -Tag v1.4.0 -Environment quality -Repository owner/repo

.EXAMPLE
./tools/scripts/ci/Assert-DeployableRelease.ps1 --help

.NOTES
Requer PowerShell 7+, Git e GitHub CLI autenticado (contents, deployments, attestations e packages com leitura). Código de saída 0 aceito, 1 recusado e 2 argumento desconhecido.

.LINK
../../../docs/runbooks/deploy-e-rollback.md
#>
#Requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Tag,
    [string] $Environment,
    [ValidateSet('manual', 'release')]
    [string] $Trigger = 'manual',
    [string] $Actor = $env:GITHUB_ACTOR,
    [string] $Repository = $env:GITHUB_REPOSITORY,
    [string] $DefaultBranch = 'main',
    [string] $ConfigPath = '.github/ci-cd.json',
    [string] $ManifestPath = 'artifacts/release-manifest.json',
    [string] $OutputPath = '',
    [string] $SummaryPath = '',
    [Alias('h')]
    [switch] $Help,
    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Help -or ($RemainingArguments | Where-Object { $_ -in @('--help', '-h', '/?', 'help') })) {
    Get-Help -Full $PSCommandPath
    exit 0
}

if ($RemainingArguments) {
    [Console]::Error.WriteLine("Argumento desconhecido: $($RemainingArguments -join ' '). Use --help.")
    exit 2
}

Import-Module (Join-Path $PSScriptRoot 'GroundworkRelease.psm1') -Force

function Invoke-Script([string] $Name, [hashtable] $Arguments) {
    $output = & (Join-Path $PSScriptRoot $Name) @Arguments
    $code = $LASTEXITCODE
    $output | ForEach-Object { Write-Output $_ }
    if ($code -ne 0) { throw "$Name recusou a operação." }
}

try {
    if (-not (Test-ReleaseTagName ($Tag ?? ''))) { throw "Deploy só é aceito em tag vX.Y.Z[-pre]; recebido '$Tag'." }
    if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Repositório inválido.' }
    if ($DefaultBranch -notmatch '^[A-Za-z0-9._/-]+$' -or $DefaultBranch.Contains('..')) { throw 'Branch padrão inválida.' }
    $version = $Tag.Substring(1)
    $isProduction = $Environment -ieq 'production'

    $config = Read-CiCdConfig -Path $ConfigPath
    $deploy = $config['deploy'] ?? @{}
    $environments = @($deploy['environments'] ?? @())
    if ($Environment -cnotin $environments) {
        throw "Environment '$Environment' não está em deploy.environments da release $Tag ($($environments -join ', '))."
    }

    if ($isProduction -and ($Trigger -eq 'release' -or ([string]$Actor).EndsWith('[bot]', [System.StringComparison]::Ordinal))) {
        throw 'Auto-deploy em production é proibido; dispare manualmente na tag, com aprovação no Environment.'
    }

    $release = Find-GitHubRelease -Repository $Repository -Tag $Tag
    if ($null -eq $release) { throw "GitHub Release $Tag inexistente." }
    if ($release['draft']) { throw "GitHub Release $Tag é rascunho; somente releases publicadas são implantáveis." }
    if ($isProduction -and $release['prerelease']) { throw "Prerelease $Tag não é aceita em production." }
    $isCandidate = Test-CandidateTag $Tag
    $preview = Get-PreviewPolicy -Config $config
    if ($release['prerelease'] -and -not $isCandidate) { throw "Prerelease $Tag não é candidata vX.Y.Z-rc.N; não é implantável." }
    if ($isCandidate -and $Environment -cne $preview.Environment) {
        throw "Candidata $Tag só é implantável no Environment de preview ($($preview.Environment))."
    }

    $commit = (git rev-parse --verify "refs/tags/$Tag^{commit}" 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw "Tag $Tag ausente no checkout." }
    $head = (git rev-parse HEAD | Out-String).Trim()
    if ($head -ne $commit) { throw 'O checkout não corresponde à tag; execute o workflow na tag.' }
    git rev-parse --verify --quiet "refs/remotes/origin/$DefaultBranch" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "origin/$DefaultBranch ausente; o checkout precisa de fetch-depth 0." }
    git merge-base --is-ancestor $commit "refs/remotes/origin/$DefaultBranch"
    if ($LASTEXITCODE -ne 0) { throw "O commit da tag $Tag não pertence a origin/$DefaultBranch." }

    Invoke-Script 'Get-ReleaseManifest.ps1' @{ Tag = $Tag; Repository = $Repository; OutputPath = $ManifestPath; ExpectedCommit = $commit }
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -AsHashtable -Depth 32
    if ($manifest['attestation'] -eq 'none') {
        throw "Release $Tag publicada sem atestação (attestations: false); releases sem atestação não são implantáveis."
    }

    $signer = $isCandidate ? 'gw-preview.yml' : 'gw-release.yml'
    & gh attestation verify $ManifestPath --repo $Repository --signer-workflow "$Repository/.github/workflows/$signer" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Atestação do release-manifest.json ausente ou inválida (assinada por $signer)." }

    foreach ($artifact in @($manifest['artifacts'] | Where-Object { $_['kind'] -eq 'container' })) {
        $subject = "oci://$($artifact['reference'])@$($artifact['digest'])"
        & gh attestation verify $subject --repo $Repository --signer-workflow "$Repository/.github/workflows/gw-container-images.yml" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Atestação ausente ou inválida para $($artifact['reference'])@$($artifact['digest'])." }
    }

    $validation = ''
    if ($isProduction -and $preview.Required) {
        $candidate = Find-ValidatedCandidate -Repository $Repository -Version $version -Environment $preview.Environment
        if ($null -ne $candidate) {
            $candidateCommit = Get-TagCommit $candidate.Tag
            $allowed = @(Get-ProjectVersionFiles -Config $config) + @(Get-ReleasePleaseFiles)
            $changed = @(git diff --name-only $candidateCommit $commit 2>$null | Where-Object { $_ })
            if ($LASTEXITCODE -ne 0) { throw "Não foi possível comparar $($candidate.Tag) com $Tag; o checkout precisa de fetch-depth 0." }
            $other = @($changed | Where-Object { $_ -cnotin $allowed })
            if ($other.Count -gt 0) {
                throw "$Tag difere da candidata validada $($candidate.Tag) além dos arquivos de versão: $(($other | Select-Object -First 10) -join ', ')."
            }
            $validation = "candidata $($candidate.Tag) validada em $($preview.Environment)"
        }
        else {
            $previous = (git describe --tags --abbrev=0 --match 'v[0-9]*' --exclude '*-*' "$commit^" 2>$null | Out-String).Trim()
            $waiver = Find-PreviewWaiver -Since $previous -Until $commit
            if (-not $waiver) {
                throw "Nenhuma candidata v$version-rc.N validada em $($preview.Environment) e nenhum Preview-Waiver; production exige a release preview."
            }
            Write-Output "::warning title=Preview-Waiver::$Tag sem candidata validada: $waiver"
            $validation = "waiver: $waiver"
        }
    }

    $deployedFile = New-TemporaryFile
    try {
        Invoke-Script 'Get-DeployedRelease.ps1' @{ Environment = $Environment; Repository = $Repository; OutputPath = $deployedFile.FullName }
        $deployed = @{}
        foreach ($line in Get-Content -LiteralPath $deployedFile.FullName) {
            $key, $value = $line.Split('=', 2)
            $deployed[$key] = $value
        }
    }
    finally {
        Remove-Item -LiteralPath $deployedFile.FullName -Force -ErrorAction SilentlyContinue
    }

    $current = [string]($deployed['version'] ?? '')
    $currentMinimum = ''
    if ($current -and (Compare-SemVer $version $current) -lt 0) {
        $currentManifestPath = Join-Path ([System.IO.Path]::GetTempPath()) "release-manifest-current-$([guid]::NewGuid().ToString('N')).json"
        try {
            Invoke-Script 'Get-ReleaseManifest.ps1' @{ Tag = "v$current"; Repository = $Repository; OutputPath = $currentManifestPath }
            $currentMinimum = [string]((Get-Content -LiteralPath $currentManifestPath -Raw | ConvertFrom-Json -AsHashtable)['rollbackMinimumVersion'])
        }
        finally {
            Remove-Item -LiteralPath $currentManifestPath -Force -ErrorAction SilentlyContinue
        }
    }

    $decision = Test-RollbackAllowed -Target $version -Current $current -CurrentMinimum $currentMinimum
    if (-not $decision.Allowed) { throw $decision.Reason }
}
catch {
    Write-Output "::error title=Deploy recusado::$($_.Exception.Message)"
    if ($SummaryPath) { "## Deploy recusado`n`n$($_.Exception.Message)" | Add-Content -LiteralPath $SummaryPath -Encoding utf8 }
    exit 1
}

$kind = $decision.Rollback ? "rollback de v$current" : ($current -eq $version ? 'redeploy' : 'deploy')
Write-GitHubOutput -Path $OutputPath -Values @{ tag = $Tag; version = $version; rollback = $decision.Rollback.ToString().ToLowerInvariant() } | Out-Null
$message = "Release $Tag aceita para $Environment ($kind; versão implantada: $($current ? "v$current" : 'nenhuma'))."
if ($validation) { $message += " Preview: $validation." }
Write-Output $message
if ($SummaryPath) { "## Guard do deploy`n`n$message" | Add-Content -LiteralPath $SummaryPath -Encoding utf8 }
exit 0
