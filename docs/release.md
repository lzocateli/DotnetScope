# Processo de release

O workflow [`release.yml`](../.github/workflows/release.yml) publica o
`dotnetscope` em Native AOT para todos os RIDs suportados. Cada etapa tem um
comando local equivalente em `tools/scripts/`.

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
instaladores); o job `publish` falha se a contagem divergir. Os nomes não levam
versão para que `releases/latest/download/<arquivo>` seja estável; a versão fica
na tag e em `dotnetscope --version` (no `.deb`, sufixos usam `~`, por exemplo
`0.4.0~rc.1`, para ordenar antes da versão final).

Cada arquivo tem atestação de proveniência do GitHub e cada pacote (`.tar.gz`,
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
| Homebrew (macOS e Linux) | `brew install lzocateli/tap/dotnetscope` | `brew upgrade`; fórmula atualizada pelo `release.yml` |
| winget (Windows) | `winget install lzocateli.DotnetScope` | `winget upgrade`; PR ao `microsoft/winget-pkgs` aberto pelo `release.yml`, publicado após a moderação da Microsoft |

A fórmula vive em [`lzocateli/homebrew-tap`](https://github.com/lzocateli/homebrew-tap)
e é gerada por [`New-HomebrewFormula.ps1`](../tools/scripts/New-HomebrewFormula.ps1)
a partir do `SHA256SUMS`; instala somente o binário (`.tar.gz` de
`osx-arm64`, `osx-x64`, `linux-arm64` e `linux-x64`) e o `brew test` confere
`--version`. Pre-releases não atualizam o tap.

O manifesto winget (`lzocateli.DotnetScope`, schema 1.12.0) é gerado por
[`New-WingetManifest.ps1`](../tools/scripts/New-WingetManifest.ps1) a partir do
`SHA256SUMS`: `.zip` de `win-x64` e `win-arm64` com `dotnetscope.exe` portátil
(alias `dotnetscope`). O winget não altera `DOTNET_ROOT` nem perfis; isso
continua sendo feito pelo próprio `dotnetscope`. Pre-releases não vão ao winget.

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

1. Ajuste `VersionPrefix` em `Directory.Build.props` por PR e aguarde os checks.
2. Crie e envie a tag anotada `vX.Y.Z` (ou `vX.Y.Z-sufixo` para pre-release)
   no commit de `main`:

   ```bash
   git tag -a v0.1.0 -m "DotnetScope 0.1.0"
   git push origin v0.1.0
   ```

3. O workflow valida que a tag corresponde a `VersionPrefix`, executa build,
   testes, publicação dos sete RIDs, smoke e testes dos instaladores em seis
   runners.
4. O job `attest-sbom` atesta o SBOM de cada RID (sujeitos: o pacote e, no
   Linux glibc, o `.deb`) antes da aprovação, para que a release nunca fique
   pública sem essas atestações.
5. O job `publish` aguarda aprovação no Environment `release`, regenera o
   `SHA256SUMS` com os instaladores, confere os 18 arquivos, gera a atestação
   de proveniência e cria a release com notas automáticas (categorias em
   [`release.yml`](../.github/release.yml)). Tags com sufixo viram pre-release
   e não aparecem em `releases/latest`. Reexecutar o job substitui os assets da
   release existente.
6. No mesmo job (sem segunda aprovação) e somente sem sufixo,
   [`Update-HomebrewTap.ps1`](../tools/scripts/Update-HomebrewTap.ps1) gera a
   fórmula e envia o commit `dotnetscope X.Y.Z` ao tap. Não faz nada se a
   fórmula já corresponder à versão; se o tap já publicar versão maior
   (correção de uma linha antiga), mantém o tap e registra um aviso.
7. Quando o tap ficou na versão da tag, `verify-homebrew` instala pelo tap em
   `macos-15` e confere a versão e o `brew test`.
8. Sem sufixo, o job `winget` chama [`winget.yml`](../.github/workflows/winget.yml):
   gera o manifesto, executa `winget validate`, instala pelo manifesto local em
   `windows-2025`, confere `dotnetscope --version`, desinstala e, no Environment
   `winget`, submete o PR com `wingetcreate submit` (v1.12.13.0, SHA-256
   fixado). Não submete se a versão já estiver no `winget-pkgs` ou houver PR
   aberto para ela.

`workflow_dispatch` e pull requests que alteram a cadeia de release executam
tudo exceto `attest-sbom`, `publish`, `verify-homebrew` e `winget`.

### Winget

O `winget.yml` também roda em pull requests que o alteram (valida com a última
release, sem submeter) e por `workflow_dispatch` a partir de `main`, com
`version` e `submit`, para a primeira submissão ou para reenviar após falha:

```bash
gh workflow run winget.yml -f version=0.4.0 -f submit=true
```

O secret `WINGET_CREATE_GITHUB_TOKEN` fica somente no Environment `winget`
(implantação restrita a tags `v*` e ao ramo `main`, sem revisor: a aprovação
humana já ocorre no `publish`). O `wingetcreate` aceita apenas PAT clássico com
escopo `public_repo` (tokens fine-grained não são suportados), que dá escrita a
todos os repositórios públicos da conta; por isso use expiração curta e
renove o secret ao expirar. O token é lido da variável de ambiente, nunca da
linha de comando. Cada PR passa pela validação automática e pela moderação do
`microsoft/winget-pkgs`; a versão fica disponível no `winget` após o merge.

### Token do tap Homebrew

O secret `HOMEBREW_TAP_TOKEN` fica somente no Environment `release` e é um PAT
fine-grained com acesso apenas a `lzocateli/homebrew-tap` e permissão
`Contents: Read and write`. O script o lê da variável de ambiente e o repassa ao
`git` por `GIT_CONFIG_*`, fora da linha de comando. Ao expirar, gere um novo PAT
com o mesmo escopo e substitua o secret; sem ele, a release é criada e o passo
do tap falha, podendo ser reexecutado após a correção.

Para testar localmente sem publicar (commit só no clone temporário):

```bash
pwsh ./tools/scripts/Update-HomebrewTap.ps1 -Version 0.4.0 -ReleaseDirectory dist
```
