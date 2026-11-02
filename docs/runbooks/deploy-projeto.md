# Deploy do DotnetScope

Semente do Altivy.Groundwork (componente `deploy`): este arquivo pertence ao projeto e
complementa o procedimento padrão de [deploy-e-rollback.md](deploy-e-rollback.md).

O DotnetScope é uma CLI distribuída por GitHub Releases, Homebrew e winget, sem servidor.
Por isso, o "deploy" não instala nada em um host: confere que a release publicada é
exatamente a do manifesto atestado e executa o pacote publicado. Ele é o aceite da
candidata `vX.Y.Z-rc.N` em `quality` ([release-preview](release-preview.md)).

## Destinos

| Environment | Destino | Runner | Revisores |
| --- | --- | --- | --- |
| `quality` | Assets da GitHub Release (candidata ou estável) e máquinas de teste do mantenedor | `ubuntu-24.04` do GitHub | `@lzocateli` (obrigatório: aceite `approval`) |
| `production` | Assets da GitHub Release estável; a distribuição por Homebrew e winget acontece no `post-release.yml` | `ubuntu-24.04` do GitHub | `@lzocateli` |

## Execução

`.github/workflows/deploy.yml`, chamado pelo `gw-deploy.yml` depois do guard:

1. **Aprovação do Environment.** Em `quality`, aprove somente depois de testar a
   candidata nas máquinas de teste ([processo de release](../release.md)).
2. **`Apply release`:** baixa todos os assets da tag e compara o SHA-256 de cada um com
   o `release-manifest.json` validado e atestado. Não altera nada; repetir dá o mesmo
   resultado. Usa somente o `GITHUB_TOKEN` com `contents: read`; não há credencial de
   Environment.
3. **`Smoke test`:** executa `tools/scripts/Test-AotPackage.ps1 -Rid linux-x64` sobre o
   pacote **publicado** (não um build novo), em containers limpos sem .NET: `--version`
   com a versão da release, `--help`, SBOM e `.deb`.

Com sucesso em `quality` numa candidata, o `gw-deploy.yml` anexa o
`preview-acceptance.json` à prerelease e reexecuta o CI do Release PR.

## Migrações e compatibilidade

Não há migração de dados nem protocolo entre versões; `rollbackMinimumVersion` continua
`0.0.0`. Rollback é instalar uma release anterior publicada.

## Smoke test e verificações após o deploy

O smoke automatizado cobre só `linux-x64`. `linux-musl-x64`, `linux-arm64`, Windows e
macOS, além dos comandos reais da CLI ([cli](../cli.md)), são verificados à mão pelo
mantenedor antes de aprovar `quality`.

## Contatos

Aprovação, operação e incidentes: `@lzocateli`.
