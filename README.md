# DotnetScope

CLI Native AOT (`dotnetscope`) para instalar, listar, validar e remover SDKs e
runtimes .NET nos escopos `user` e `global`, com verificação de integridade,
extração segura e configuração de `DOTNET_ROOT` e `PATH` no perfil do usuário.

> **Estado:** em desenvolvimento (fase F7). Release atual:
> [v0.3.0](https://github.com/lzocateli/DotnetScope/releases/tag/v0.3.0).
> Implementado em Linux, Windows e macOS, nos escopos user e global:
> `list --available|--installed`,
> `install` (inclusive `--archive` e `--from-global-json`), `uninstall`, `env` e
> `doctor` ([referência da CLI](docs/cli.md)). O restante
> descrito abaixo é planejado; consulte o
> [plano de implementação](docs/plano-implementacao.md) para fases e decisões.

## Instalação

Binários Native AOT, sem dependência de .NET, para `linux-x64`, `linux-arm64`,
`linux-musl-x64` (glibc 2.27+ ou musl 1.2.3+), `win-x64`, `win-arm64`,
`osx-arm64` e `osx-x64`. Os instaladores conferem o SHA-256 do pacote com o
`SHA256SUMS` da release, não se autoelevam e não alteram perfis nem o `PATH`:

```bash
# Linux e macOS: instala em ~/.local/bin (--global: /usr/local/bin, exige sudo)
curl -fsSL https://github.com/lzocateli/DotnetScope/releases/latest/download/install.sh | sh
```

```powershell
# Windows (PowerShell 5.1+): instala em %LOCALAPPDATA%\Programs\dotnetscope
# A primeira linha habilita TLS 1.2, exigido pelo GitHub, no Windows PowerShell 5.1.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
& ([scriptblock]::Create((Invoke-RestMethod https://github.com/lzocateli/DotnetScope/releases/latest/download/install.ps1)))
```

Para instalar uma versão específica, use `--version 0.1.0` (`install.sh`) ou
`-Version 0.1.0` (`install.ps1`). Também é possível baixar o pacote
`dotnetscope-<rid>.tar.gz`/`.zip` da [página de releases](https://github.com/lzocateli/DotnetScope/releases)
e conferir a atestação de proveniência com
`gh attestation verify <arquivo> --repo lzocateli/DotnetScope`. No macOS, o
escopo user configura `~/.zprofile` (e `~/.profile` para bash de login) e o
escopo global usa o `.pkg` oficial verificado
([referência](docs/cli.md#escopo-global-no-macos)).

Atrás de proxy HTTP, prefira `curl`: o `wget` do BusyBox (Alpine) não usa
`CONNECT` para HTTPS e costuma ser recusado pelo proxy.

## Uso disponível

```bash
dotnetscope list --available                      # canais, fase de suporte e fim do suporte
dotnetscope list --available 10.0.4xx             # versões da feature band e a que seria resolvida
dotnetscope list --available lts -c runtime       # runtime do canal LTS mais recente
dotnetscope list --available SDK-10.0 --output json
dotnetscope install sdk 10.0.402                  # escopo user em ~/.dotnet, SHA-512 verificado
dotnetscope install runtime lts --yes             # confirma a versão resolvida sem perguntar
dotnetscope install SDK-10.0.4xx --dry-run        # mostra o plano sem baixar
dotnetscope install sdk lts --yes --configure-env false   # não altera perfis
dotnetscope env apply                             # DOTNET_ROOT/PATH nos perfis (bloco gerenciado)
dotnetscope env remove                            # restaura os perfis originais
eval "$(dotnetscope env show)"                    # somente para a sessão atual
dotnetscope install --from-global-json --yes      # SDK exigido pelo global.json do projeto
dotnetscope install sdk 10.0.402 --archive ./dotnet-sdk-10.0.402-linux-x64.tar.gz --sha512 <hash>
dotnetscope list --installed                      # origem: dotnetscope, external ou package
dotnetscope uninstall sdk 10.0.402                # preserva o que outras instalações usam
dotnetscope doctor --repair                       # recupera operações interrompidas
```

No Linux, `--scope global` instala em `/usr/local/share/dotnet` para todos os
usuários, com `/etc/profile.d/dotnetscope.sh` e `/etc/dotnet/install_location_<arch>`
([referência](docs/cli.md#escopo-global-linux)):

```bash
sudo dotnetscope install sdk lts --scope global --yes
sudo dotnetscope uninstall sdk 10.0.402 --scope global   # restaura /etc
sudo dotnetscope install sdk 10.0 --scope global --source apt --install-deps --yes   # pacote da distribuição
```

No Windows, a raiz padrão é `%LOCALAPPDATA%\Microsoft\dotnet` e o ambiente vai
para as variáveis do usuário (`HKCU\Environment`), valendo em terminais novos:

```powershell
dotnetscope install sdk lts --yes                 # zip oficial, DOTNET_ROOT e Path do usuário
dotnetscope env show | Out-String | Invoke-Expression   # somente para a sessão atual
dotnetscope env remove                            # restaura os valores anteriores do registro
dotnetscope install runtime 9.0 --scope global --yes   # terminal elevado: instalador oficial verificado (Authenticode)
```

`--configure-env` tem padrão `true`: a CLI grava um bloco gerenciado
(`# >>> dotnetscope >>>`) nos perfis do shell ou no registro do Windows. Com
`false`, nenhum arquivo, chave de registro ou variável persistente é alterado.
Blocos e instalações de terceiros nunca são sobrescritos.

## Desenvolvimento

- .NET 10 (SDK 10.0.400+) com Native AOT; build e testes executam em containers.
- Valide como no CI (requer PowerShell 7 e Docker ou Podman; `-Aot` publica e
  testa o binário nativo `linux-x64` em `artifacts/aot/linux-x64/`):

  ```bash
  pwsh ./tools/scripts/Invoke-Validation.ps1 -Aot
  ```

- E2E real em containers limpos (Ubuntu 24.04 e Fedora 41), após o comando
  acima; baixa os artefatos oficiais:

  ```bash
  pwsh ./tools/scripts/Invoke-E2E.ps1
  ```

- Pacotes de release (Native AOT por RID, `SHA256SUMS`) e testes dos
  instaladores: veja o [processo de release](docs/release.md).

- No Windows e no macOS, build, testes e AOT rodam no host com o SDK do
  `global.json` (`-Native`; não executa `dotnet format`). O E2E do Windows altera
  o registro e só roda em runner efêmero do GitHub
  (`tools/scripts/Invoke-WindowsE2E.ps1`):

  ```powershell
  pwsh ./tools/scripts/Invoke-Validation.ps1 -Native -Aot
  ```

- Instale o hook obrigatório de detecção de secrets (requer PowerShell 7 e
  Docker ou Podman):

  ```bash
  pwsh ./tools/scripts/install-gitleaks-hook.ps1
  git hook run pre-commit
  ```

- Leia [CONTRIBUTING](.github/CONTRIBUTING.md), a
  [convenção de commits](.github/COMMIT_CONVENTIONS.md) e a
  [política de segurança](.github/SECURITY.md).

A infraestrutura de `.github/` e `.githooks/` foi gerada pelo Altivy.Groundwork
(componentes `core` e `secret-scanning`) e adaptada a este projeto;
`.groundwork/manifest.json` registra a origem de cada arquivo.

## Licença

[MIT](LICENSE).
