<#
.SYNOPSIS
Testa validação de origem das tags de release com repositórios Git descartáveis.

.DESCRIPTION
Cria fixtures locais para tag anotada válida, clone raso, tag inválida,
VersionPrefix divergente e commit externo à história de origin/main. Não usa
rede nem altera tags ou repositórios reais.

.PARAMETER RemainingArguments
Aceita somente --help; outros valores retornam erro de uso.

.EXAMPLE
./tools/scripts/Test-ReleaseTag.ps1
#>
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

$ErrorActionPreference = 'Stop'

if ($RemainingArguments -contains '--help') {
    Get-Help $PSCommandPath -Full
    exit 0
}

if ($RemainingArguments.Count -gt 0) {
    [Console]::Error.WriteLine('Argumento desconhecido. Use --help.')
    exit 2
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$validator = Join-Path $PSScriptRoot 'Assert-ReleaseTag.ps1'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "dotnetscope-tag-tests-$([guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

function Invoke-GitFixture {
    param(
        [string] $Directory,
        [string[]] $Arguments
    )

    $output = @(& git -C $Directory @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments[0]) falhou na fixture: $(($output | ForEach-Object { $_.ToString() }) -join ' ')"
    }

    $output
}

function Invoke-TagValidator {
    param(
        [string] $Directory,
        [string] $Tag,
        [string] $VersionPrefix
    )

    $output = @(& pwsh -NoLogo -NoProfile -File $validator -RepositoryRoot $Directory -Tag $Tag -VersionPrefix $VersionPrefix 2>&1)
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | ForEach-Object { $_.ToString() }) -join "`n" }
}

try {
    $fixture = Join-Path $temporaryRoot 'full'
    [IO.Directory]::CreateDirectory($fixture) | Out-Null
    $null = Invoke-GitFixture $fixture @('init', '--initial-branch=main')
    $null = Invoke-GitFixture $fixture @('config', 'user.name', 'DotnetScope Fixture')
    $null = Invoke-GitFixture $fixture @('config', 'user.email', 'fixture@example.invalid')
    Set-Content -LiteralPath (Join-Path $fixture 'source.txt') -Value 'main-1' -NoNewline
    $null = Invoke-GitFixture $fixture @('add', 'source.txt')
    $null = Invoke-GitFixture $fixture @('commit', '-m', 'main 1')
    $mainCommit = (Invoke-GitFixture $fixture @('rev-parse', 'HEAD') | Select-Object -First 1).ToString().Trim()
    $null = Invoke-GitFixture $fixture @('update-ref', 'refs/remotes/origin/main', $mainCommit)
    $null = Invoke-GitFixture $fixture @('tag', '-a', 'v1.2.3', '-m', 'release 1.2.3')

    $valid = Invoke-TagValidator $fixture 'v1.2.3' '1.2.3'
    if ($valid.ExitCode -ne 0 -or $valid.Output -notmatch [regex]::Escape($mainCommit)) {
        throw "Tag anotada válida não resolveu para o commit esperado (exit $($valid.ExitCode)): $($valid.Output)"
    }

    $invalid = Invoke-TagValidator $fixture 'latest' '1.2.3'
    if ($invalid.ExitCode -eq 0) {
        throw 'Tag inválida foi aceita.'
    }

    $mismatch = Invoke-TagValidator $fixture 'v1.2.3' '1.2.4'
    if ($mismatch.ExitCode -eq 0) {
        throw 'Tag com VersionPrefix divergente foi aceita.'
    }

    Set-Content -LiteralPath (Join-Path $fixture 'source.txt') -Value 'main-2' -NoNewline
    $null = Invoke-GitFixture $fixture @('commit', '-am', 'main 2')
    $shallowPath = Join-Path $temporaryRoot 'shallow'
    $sourceUri = ([Uri]::new($fixture)).AbsoluteUri
    $clone = @(& git clone --quiet --depth 1 -- $sourceUri $shallowPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Não foi possível criar clone raso de fixture: $(($clone | ForEach-Object { $_.ToString() }) -join ' ')"
    }

    $shallow = Invoke-TagValidator $shallowPath 'v1.2.3' '1.2.3'
    if ($shallow.ExitCode -eq 0 -or $shallow.Output -notmatch 'Clone raso') {
        throw 'Clone raso não foi recusado explicitamente.'
    }

    $null = Invoke-GitFixture $fixture @('switch', '--orphan', 'outside-main')
    Set-Content -LiteralPath (Join-Path $fixture 'source.txt') -Value 'outside' -NoNewline
    $null = Invoke-GitFixture $fixture @('add', 'source.txt')
    $null = Invoke-GitFixture $fixture @('commit', '-m', 'outside main')
    $null = Invoke-GitFixture $fixture @('tag', '-a', 'v1.2.4', '-m', 'release outside')
    $external = Invoke-TagValidator $fixture 'v1.2.4' '1.2.4'
    if ($external.ExitCode -eq 0 -or $external.Output -notmatch 'não pertence à história') {
        throw 'Commit externo à história de main foi aceito.'
    }

    Write-Output 'Aprovado: tag anotada, clone raso, formato, versão e commit externo.'
}
finally {
    if ([IO.Directory]::Exists($temporaryRoot)) {
        [IO.Directory]::Delete($temporaryRoot, $true)
    }
}
