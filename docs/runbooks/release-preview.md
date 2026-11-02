# Runbook: release preview

Arquivo gerenciado pelo Altivy.Groundwork (componente `release-preview`). Não edite: mudanças vão
para o catálogo. A estratégia completa está em [../estrategia-de-release.md](../estrategia-de-release.md).

Repositório: `lzocateli/DotnetScope` · branch padrão: `main` ·
aceite: `approval`.

## 1. Gerar a candidata

1. Confirme que o Release PR está aberto e atualizado:

   ```bash
   gh pr list --repo lzocateli/DotnetScope --head release-please--branches--main
   ```

2. Dispare o workflow na branch padrão:

   ```bash
   gh workflow run gw-preview.yml --repo lzocateli/DotnetScope --ref main
   ```

3. Aprove o Environment `release` quando solicitado. O workflow calcula `vX.Y.Z-rc.N`, cria a tag
   pelo GitHub App, chama `publish.yml`, atesta o `release-manifest.json` e publica a prerelease.
4. Confira:

   ```bash
   gh release view vX.Y.Z-rc.N --repo lzocateli/DotnetScope
   ```

## 2. Validar em quality

O job `Dispatch preview environment` do `gw-preview.yml` dispara o `gw-deploy.yml` na tag da candidata
quando `deploy.autoDeployEnvironment` é `quality` ou o aceite é `approval`. Sem isso, dispare:

```bash
gh workflow run gw-deploy.yml --repo lzocateli/DotnetScope --ref vX.Y.Z-rc.N -f environment=quality
```

Teste a candidata em `quality` e **aprove o Environment** só quando ela puder virar a estável.

Com sucesso, o job `Record candidate acceptance` anexa `preview-acceptance.json` à prerelease e
reexecuta o CI do Release PR.

## 3. Promover

1. Verifique o check `Preview gate` no Release PR; ele mostra a candidata validada.
2. Faça o merge do Release PR (squash) e siga o [runbook de release](release.md).
3. Implante em `production` na tag estável:

   ```bash
   gh workflow run gw-deploy.yml --repo lzocateli/DotnetScope --ref vX.Y.Z -f environment=production
   ```

   O guard confere a candidata validada e o diff entre ela e a estável.

## 4. Falhas comuns

| Sintoma | Causa provável | Ação |
| --- | --- | --- |
| `Nenhum Release PR aberto` | não há commit releasable desde a última estável | integre um `feat`/`fix` ou use `Release-As:` |
| `O Release PR #N não está sobre <sha>` | o release-please ainda não atualizou o Release PR | aguarde o `gw-release.yml` na `main` e dispare de novo |
| `Preview gate` vermelho depois do aceite | a `main` andou e o Release PR mudou de commit-base | gere nova candidata (`rc.N+1`) e valide |
| `Preview gate` vermelho sem nova integração | o CI do Release PR não foi reexecutado | `gh run rerun <id>` na execução do CI do Release PR |
| Deploy da candidata recusado | Environment diferente de `quality` ou tag fora do padrão `-rc.N` | use `quality`; prereleases `preview.N` nunca são implantáveis |
| `production` recusado por diff | código integrado depois da candidata | nova versão com nova candidata |
| Emergência sem `quality` disponível | — | commit com rodapé `Preview-Waiver: <motivo>` desde a última estável |
