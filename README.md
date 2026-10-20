# DotnetScope

CLI Native AOT (`dotnetscope`) para instalar, listar, validar e remover SDKs e
runtimes .NET nos escopos `user` e `global`, com verificação de integridade,
extração segura e configuração de `DOTNET_ROOT` e `PATH` no perfil do usuário.

> **Estado:** em desenvolvimento (fase F1). Ainda não há release publicada.
> O comportamento descrito abaixo é o planejado; consulte o
> [plano de implementação](docs/plano-implementacao.md) para fases e decisões.

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
