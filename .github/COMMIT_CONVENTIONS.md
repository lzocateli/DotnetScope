# Convenção de commits

Use [Conventional Commits 1.0.0](https://www.conventionalcommits.org/pt-br/v1.0.0/).

```text
<tipo>[escopo opcional][!]: <descrição>
```

A descrição é em português brasileiro, no imperativo, começa com minúscula e não termina com ponto.

## Tipos

| Tipo | Uso |
| --- | --- |
| `feat` | comportamento novo ou ampliado |
| `fix` | correção de defeito ou vulnerabilidade |
| `docs` | somente documentação |
| `style` | somente formatação |
| `refactor` | reorganização sem alteração funcional |
| `perf` | melhoria de desempenho |
| `test` | testes sem alteração de produção |
| `build` | build, dependências e imagens |
| `ci` | workflows e automação |
| `chore` | manutenção restante |
| `revert` | reversão formal |

## Escopos

`cli`, `core`, `metadata`, `install`, `env`, `platform`, `linux`, `windows`, `macos`, `packaging`, `tests`, `docs`, `tooling`, `deps`, `actions`, `repo`.

Exemplos:

```text
feat(install): adicione instalação de runtime aspnetcore no escopo user
fix(env): preserve bloco de terceiros ao detectar DOTNET_ROOT divergente
docs(cli): documente códigos de saída do comando uninstall
```

Mudança incompatível usa `!` e rodapé com impacto e migração:

```text
feat(env)!: mova o bloco gerenciado para ~/.config/dotnetscope/env.sh

BREAKING CHANGE: perfis configurados por versões anteriores devem executar
`dotnetscope env apply` para migrar o bloco antigo.
```