# Processo de release

O workflow [`release.yml`](../.github/workflows/release.yml) publica o
`dotnetscope` em Native AOT para todos os RIDs suportados. Cada etapa tem um
comando local equivalente em `tools/scripts/`.

## Artefatos

| Arquivo | Conteúdo |
| --- | --- |
| `dotnetscope-<rid>.tar.gz` | `dotnetscope` (modo 0755) e `LICENSE`, dono `0:0`; Linux e macOS |
| `dotnetscope-<rid>.zip` | `dotnetscope.exe` e `LICENSE`; Windows |
| `install.sh` | instalador POSIX (Linux e macOS) |
| `install.ps1` | instalador Windows PowerShell 5.1+ e PowerShell 7 |
| `SHA256SUMS` | SHA-256 de todos os arquivos acima, formato `sha256sum` |

Os nomes não levam versão para que `releases/latest/download/<arquivo>` seja
estável; a versão fica na tag e em `dotnetscope --version`. Cada arquivo tem
atestação de proveniência do GitHub (`gh attestation verify`). Assinatura e
SBOM chegam na F9.

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

O smoke (`Test-AotPackage.ps1`) confere o SHA-256, o conteúdo do pacote e
executa `--version`, `--help` e `list --available --offline` com as fixtures
de metadata, em containers sem .NET e sem rede. `Test-Installers.ps1` serve os
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
4. O job `publish` aguarda aprovação no Environment `release`, regenera o
   `SHA256SUMS` com os instaladores, gera a atestação e cria a release com
   notas automáticas (categorias em [`release.yml`](../.github/release.yml)).
   Tags com sufixo viram pre-release e não aparecem em `releases/latest`.

`workflow_dispatch` e pull requests que alteram a cadeia de release executam
tudo exceto o job `publish`.
