# Processo de release

O processo comum de release (Release PR, versão, tag, `release-manifest.json`,
republicação e correção de release defeituosa) é o do Altivy.Groundwork, descrito
no runbook [release](runbooks/release.md) e em [CI-CD](../.github/CI-CD.md). Este
documento cobre o que é específico do DotnetScope: o
[`publish.yml`](../.github/workflows/publish.yml) publica o `dotnetscope` em
Native AOT para todos os RIDs suportados e o
[`post-release.yml`](../.github/workflows/post-release.yml) distribui pelo
Homebrew e pelo winget. Cada etapa tem um comando local equivalente em
`tools/scripts/`.

## Artefatos

| Arquivo | Conteúdo |
| --- | --- |
| `dotnetscope-<rid>.tar.gz` | `dotnetscope` (modo 0755) e `LICENSE`, dono `0:0`; Linux e macOS |
| `dotnetscope-<rid>.zip` | `dotnetscope.exe` e `LICENSE`; Windows |
| `dotnetscope-<rid>.spdx.json` | SBOM SPDX 2.2 do pacote do RID: binário, `LICENSE` e dependências NuGet do build |
| `dotnetscope-linux-x64.deb`, `dotnetscope-linux-arm64.deb` | `/usr/bin/dotnetscope` e `/usr/share/doc/dotnetscope/copyright`, dono `root`; `Depends: libc6 (>= 2.27)` |
| `install.sh` | instalador POSIX (Linux e macOS) |
| `install.ps1` | instalador Windows PowerShell 5.1+ e PowerShell 7 |
| `SHA256SUMS` | SHA-256 de todos os arquivos acima, formato `sha256sum` |

São 18 arquivos além do `SHA256SUMS` (7 pacotes, 7 SBOMs, 2 `.deb` e 2
instaladores); o job `Prepare release assets` falha se a contagem divergir. Os nomes não levam
versão para que `releases/latest/download/<arquivo>` seja estável; a versão fica
na tag e em `dotnetscope --version` (no `.deb`, sufixos usam `~`, por exemplo
`0.4.0~rc.1`, para ordenar antes da versão final).

Cada arquivo tem atestação de proveniência do GitHub (`gw-github-assets.yml`), a
release tem o `release-manifest.json` atestado com o SHA-256 de cada asset e cada pacote (`.tar.gz`,
`.zip` e `.deb`) tem atestação do SBOM do seu RID, ambas Sigstore keyless via
OIDC, sem custo. Os binários não têm assinatura Authenticode nem Developer ID
(exigem certificados pagos; ver [F9 sem custo](plano-implementacao.md#f9-sem-custo-proposta)).

## Verificação pelo usuário

```bash
sha256sum --check --ignore-missing SHA256SUMS
gh attestation verify dotnetscope-linux-x64.tar.gz --repo lzocateli/DotnetScope
gh attestation verify dotnetscope-linux-x64.tar.gz --repo lzocateli/DotnetScope \
  --predicate-type https://spdx.dev/Document/v2.2
```

## Canais de distribuição

| Canal | Comando | Atualização |
| --- | --- | --- |
| Instaladores | `install.sh` / `install.ps1` (ver [README](../README.md#instalação)) | `releases/latest` |
| `.deb` (asset, sem repositório APT) | `sudo apt install ./dotnetscope-linux-x64.deb` | manual, baixando a nova release |
| Homebrew (macOS e Linux) | `brew install lzocateli/tap/dotnetscope` | `brew upgrade`; fórmula atualizada pelo `post-release.yml` |
| winget (Windows) | `winget install lzocateli.DotnetScope` | `winget upgrade`; PR ao `microsoft/winget-pkgs` aberto pelo `post-release.yml`, publicado após a moderação da Microsoft |

A fórmula vive em [`lzocateli/homebrew-tap`](https://github.com/lzocateli/homebrew-tap)
e é gerada por [`New-HomebrewFormula.ps1`](../tools/scripts/New-HomebrewFormula.ps1)
a partir do `SHA256SUMS`; instala somente o binário (`.tar.gz` de
`osx-arm64`, `osx-x64`, `linux-arm64` e `linux-x64`) e o `brew test` confere
`--version`.

O manifesto winget (`lzocateli.DotnetScope`, schema 1.10.0) é gerado por
[`New-WingetManifest.ps1`](../tools/scripts/New-WingetManifest.ps1) a partir do
`SHA256SUMS`: `.zip` de `win-x64` e `win-arm64` com `dotnetscope.exe` portátil
(alias `dotnetscope`). O winget não altera `DOTNET_ROOT` nem perfis; isso
continua sendo feito pelo próprio `dotnetscope`.

## Build por RID

| RID | Onde compila | Toolchain |
| --- | --- | --- |
| `linux-x64`, `linux-arm64` | container em host Linux x64 | imagem oficial de cross-build `azurelinux-3.0-net10.0-cross-*` (sysroot glibc 2.27, clang, lld) |
| `linux-musl-x64` | container em host Linux x64 | `azurelinux-3.0-net10.0-cross-amd64-musl` (sysroot musl 1.2.3) |
| `win-x64`, `win-arm64` | `windows-2025`, `windows-11-arm` | SDK do `global.json` e Visual Studio C++ |
| `osx-arm64`, `osx-x64` | `macos-15`, `macos-15-intel` | SDK do `global.json` e Xcode |

Native AOT não compila entre sistemas operacionais. No Linux, o SDK oficial é
copiado sobre a imagem de cross-build por
[`linux-cross.Dockerfile`](../tools/build/linux-cross.Dockerfile); imagens são
fixadas por tag e digest em `Publish-Aot.ps1` e devem ser atualizadas juntas.

```bash
pwsh -c './tools/scripts/Publish-Aot.ps1 -Rid linux-x64, linux-arm64, linux-musl-x64'
pwsh ./tools/scripts/Test-AotPackage.ps1 -Rid linux-x64        # ubuntu:18.04, rockylinux:8, debian:12
pwsh ./tools/scripts/Test-AotPackage.ps1 -Rid linux-musl-x64   # alpine:3.17 e alpine:3.24
pwsh ./tools/scripts/Test-Installers.ps1                        # install.sh com servidor HTTP local
```

O SBOM é gerado por `microsoft/sbom-tool` (MIT), ferramenta local fixada em
[`.config/dotnet-tools.json`](../.config/dotnet-tools.json): no container de
cross-build para Linux e com o SDK do runner para Windows e macOS. O `.deb` é
montado com `dpkg-deb --root-owner-group` em `docker.io/library/debian:12`.

O smoke (`Test-AotPackage.ps1`) confere o SHA-256, o conteúdo do pacote e
executa `--version`, `--help` e `list --available --offline` com as fixtures
de metadata, em containers sem .NET e sem rede. Quando presentes, confere também
o SBOM (SPDX 2.2, nome `dotnetscope <versão>` e SHA-256 do binário) e instala,
executa e remove o `.deb` com `apt-get` em `ubuntu:18.04` e `debian:12`. `Test-Installers.ps1` serve os
pacotes em `127.0.0.1` e cobre ajuda, erros de uso, checksum adulterado sem
efeitos, download falho, instalação, reinstalação sem resíduos, aviso de
`PATH`, destino padrão e `--global`; no Windows, executa `install.ps1` em
Windows PowerShell 5.1 e PowerShell 7, por `-File` e como scriptblock.

## Publicação

Somente o mantenedor publica versões. A aprovação de um PR e a exceção para
seus próprios PRs não substituem a aprovação do Environment `release`; consulte
a [governança](../.github/GOVERNANCE.md).

### Releases de segurança e suporte

Versões pré-1.0 não têm suporte de segurança nem backports. A partir de
`1.0.0`, somente a minor `1.x` mais recente é suportada; a publicação de uma
minor nova encerra o suporte à anterior. O suporte da linha `1.x` termina com
`2.0.0`. Não há SLA contratual. Correções são publicadas na linha suportada;
consulte a [política de segurança](../.github/SECURITY.md#versões-suportadas).

### Fluxo

1. PRs `feat`, `fix` (ou com `!`) integrados na `main` atualizam o Release PR
   `chore(main): release X.Y.Z`, aberto pelo release-please com o GitHub App
   `Altivy Release`. O release-please calcula a versão, atualiza `VersionPrefix`
   em `Directory.Build.props`, `.release-please-manifest.json` e o `CHANGELOG.md`.
   Não edite a versão nem crie tags manualmente.
2. O merge do Release PR pelo mantenedor executa o `gw-release.yml`: o App cria a
   tag `vX.Y.Z` e a GitHub Release em rascunho, e o workflow confere que a tag
   corresponde às versões e que o commit é ancestral de `origin/main`.
3. O `publish.yml`, na tag, executa build, testes, publicação dos sete RIDs,
   smoke e testes dos instaladores em seis runners.
4. O job `attest-sbom` atesta o SBOM de cada RID (sujeitos: o pacote e, no
   Linux glibc, o `.deb`) antes dos assets, para que a release nunca fique
   pública sem essas atestações.
5. `Prepare release assets` regenera o `SHA256SUMS` com os instaladores e confere
   os 18 arquivos; `Publish release assets` (`gw-github-assets.yml`) aguarda
   aprovação no Environment `release`, gera a atestação de proveniência e anexa os
   arquivos à release em rascunho, sem sobrescrever asset já publicado.
6. O `gw-release.yml` gera e atesta o `release-manifest.json` e publica a
   release. As notas vêm do `CHANGELOG.md` gerado pelo release-please.
7. Com a release pública, o `post-release.yml`:
   - no Environment `release` (nova aprovação), executa
     [`Update-HomebrewTap.ps1`](../tools/scripts/Update-HomebrewTap.ps1) com o
     `SHA256SUMS` publicado e envia o commit `dotnetscope X.Y.Z` ao tap. Não faz
     nada se a fórmula já corresponder à versão; se o tap já publicar versão maior
     (correção de uma linha antiga), mantém o tap e registra um aviso;
   - quando o tap ficou na versão da release, `verify-homebrew` instala pelo tap
     em `macos-15` e confere a versão e o `brew test`;
   - chama [`winget.yml`](../.github/workflows/winget.yml): gera o manifesto,
     executa `winget validate`, instala pelo manifesto local em `windows-2025`,
     confere `dotnetscope --version`, desinstala e, no Environment `winget`,
     submete o PR com `wingetcreate submit` (v1.12.13.0, SHA-256 fixado). Não
     submete se a versão já estiver no `winget-pkgs` ou houver PR aberto para ela.

Pull requests que alteram a cadeia de release e `workflow_dispatch` do
`publish.yml` executam build, smoke e testes dos instaladores, sem publicar.
Prereleases (`vX.Y.Z-sufixo`) não fazem parte do fluxo automático.

Se a publicação falhar parcialmente, a release continua em rascunho: corrija a
causa e use o modo `republish` descrito no [runbook](runbooks/release.md).

### Winget

O `winget.yml` também roda em pull requests que o alteram (valida com a última
release, sem submeter) e por `workflow_dispatch` a partir de `main`, com
`version` e `submit`, para a primeira submissão ou para reenviar após falha:

```bash
gh workflow run winget.yml -f version=0.4.0 -f submit=true
```

No dispatch manual, `submit=true` é recusado fora de `main`. No fluxo
automático, o workflow reutilizável recebe o contexto do `gw-release.yml`:
`push` em `main` (post-release) ou `workflow_dispatch` na tag `vX.Y.Z`
correspondente (republish); qualquer outra origem é recusada.

O secret `WINGET_CREATE_GITHUB_TOKEN` fica somente no Environment `winget`
com `@lzocateli` como revisor obrigatório. `prevent_self_review` está
desativado para permitir a aprovação consciente do único mantenedor quando ele
inicia a execução; o GitHub também permite bypass administrativo, atualmente
limitado ao mesmo mantenedor. O `wingetcreate` aceita apenas PAT clássico com
escopo `public_repo` (tokens fine-grained não são suportados), que dá escrita a
todos os repositórios públicos da conta; por isso use expiração curta e
renove o secret ao expirar. O token é lido da variável de ambiente, nunca da
linha de comando. Cada PR passa pela validação automática e pela moderação do
`microsoft/winget-pkgs`; a versão fica disponível no `winget` após o merge.

### Token do tap Homebrew

O secret `HOMEBREW_TAP_TOKEN` fica somente no Environment `release`, usado pelo
job do tap no `post-release.yml`, e é um PAT
fine-grained com acesso apenas a `lzocateli/homebrew-tap` e permissão
`Contents: Read and write`. O script o lê da variável de ambiente e o repassa ao
`git` por `GIT_CONFIG_*`, fora da linha de comando. Ao expirar, gere um novo PAT
com o mesmo escopo e substitua o secret; sem ele, a release é publicada e o passo
do tap falha, podendo ser reexecutado (re-run do job) após a correção.

Para testar localmente sem publicar (commit só no clone temporário):

```bash
pwsh ./tools/scripts/Update-HomebrewTap.ps1 -Version 0.4.0 -ReleaseDirectory dist
```

## Limitações conhecidas

- O modo `republish` recompila os pacotes. Assets já anexados são mantidos com o
  digest publicado, mas um asset ausente é enviado a partir do novo build, que pode
  não reproduzir byte a byte o `SHA256SUMS` já publicado. Se o `SHA256SUMS` já
  estiver anexado, confira o resultado com `sha256sum --check` antes de divulgar.

- A configuração do GitHub permite bypass administrativo do Environment
  `winget` (`can_admins_bypass=true`). O único administrador consultado é
  `@lzocateli`; mudanças nesse grupo exigem revisão desta política.
- Recusa de aprovação e submissão real ao `microsoft/winget-pkgs` não foram
  exercitadas nesta remediação. A validação ficou limitada às regras consultadas
  pela API e a fixtures sintéticas; nenhum PR de submissão foi aberto.
