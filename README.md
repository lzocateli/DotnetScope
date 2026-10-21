# DotnetScope

CLI Native AOT (`dotnetscope`) para instalar, listar, validar e remover SDKs e
runtimes .NET nos escopos `user` e `global`, com verificação de integridade,
extração segura e configuração de `DOTNET_ROOT` e `PATH` no perfil do usuário.

> **Estado:** em desenvolvimento (fase F5). Ainda não há release publicada.
> Implementado em Linux/macOS, escopo user: `list --available|--installed`,
> `install` (inclusive `--archive` e `--from-global-json`), `uninstall`, `env` e
> `doctor` ([referência da CLI](docs/cli.md)). O restante
> descrito abaixo é planejado; consulte o
> [plano de implementação](docs/plano-implementacao.md) para fases e decisões.

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

## Uso planejado

```bash
dotnetscope install sdk 10.0.400                      # escopo user, configura o ambiente
dotnetscope install SDK-10.0.400 --configure-env false # só imprime as instruções
dotnetscope install runtime 10.0 --scope global        # requer terminal elevado
dotnetscope list --installed
dotnetscope uninstall sdk 10.0.400
dotnetscope env show --shell bash
dotnetscope doctor
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
