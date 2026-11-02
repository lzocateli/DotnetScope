# Runbook: deploy, redeploy e rollback

Arquivo gerenciado pelo Altivy.Groundwork (componente `deploy`). Não edite: mudanças vão
para o catálogo. Destinos, runner, migrações e contatos ficam em
[deploy-projeto.md](deploy-projeto.md).

Repositório: `lzocateli/DotnetScope` · Environments de deploy:
`quality`, `production` ·
auto-deploy: `quality` ·
revisores: `@lzocateli`.

## 1. Princípios

- Deploy somente de GitHub Release **publicada**: `gw-deploy.yml` roda na tag `vX.Y.Z` e o guard
  `Assert-DeployableRelease.ps1` recusa branch, rascunho, tag fora de `main`,
  manifesto ausente ou sem atestação e prerelease em `production`.
- Prerelease só é implantável se for candidata `vX.Y.Z-rc.N` e apenas no Environment de preview
  (`deploy.previewEnvironment`, padrão `quality`); o sucesso lá registra o aceite da candidata.
- Com `preview.requiredForRelease`, `production` exige a candidata validada da mesma versão, sem
  código diferente dela, ou um commit com `Preview-Waiver:` desde a estável anterior
  (ver [../estrategia-de-release.md](../estrategia-de-release.md), quando instalada).
- Os Environments de deploy aceitam somente refs `tag:v*`; um job de deploy em branch é
  recusado pelo próprio GitHub.
- Artefatos são imutáveis e identificados por digest no `release-manifest.json`; o deploy nunca
  recompila nada.
- Rollback é deploy de uma release anterior já publicada. Nunca apague ou mova tags, nunca
  exclua GitHub Releases e nunca recompile artefatos.
- Deploys no mesmo Environment não se sobrepõem (`concurrency: deploy-<environment>`).

## 2. Deploy de nova release

1. Após o merge do Release PR, a release é publicada por `gw-release.yml`.
2. O job `Auto deploy` dispara automaticamente o deploy em `quality` na tag.
   Aprove no Environment e acompanhe o smoke test.
3. Promoção para `production` é sempre manual, na **mesma tag** validada, com aprovação:

   ```bash
   gh workflow run gw-deploy.yml --repo lzocateli/DotnetScope --ref vX.Y.Z -f environment=production
   ```

   Pela interface: Actions → Deploy → Run workflow → *Use workflow from: Tags / vX.Y.Z*.

## 3. Redeploy da mesma release

Use quando o host foi recriado, houve falha transitória ou é preciso reaplicar a configuração.

```bash
gh workflow run gw-deploy.yml --repo lzocateli/DotnetScope --ref vX.Y.Z -f environment=<environment>
```

O guard valida a release, o Environment pede aprovação e `deploy.yml` aplica os **mesmos
digests**. Verifique o smoke test e a versão em execução. Corrigir o procedimento de deploy de
uma versão exige nova release: executar na tag usa o `gw-deploy.yml` e o `deploy.yml` daquela
release.

## 4. Rollback

1. **Decida entre rollback e roll-forward.** Prefira rollback quando o defeito é grave, a causa
   não está clara e a versão anterior é compatível com os dados atuais. Prefira roll-forward
   quando a correção é simples ou a versão anterior não lê os dados/protocolo atuais.
2. **Identifique a versão atual e a alvo:**

   ```bash
   gh api "repos/lzocateli/DotnetScope/deployments?environment=<environment>" --jq '.[0:5][] | {ref, created_at, creator: .creator.login}'
   pwsh ./tools/scripts/ci/Get-DeployedRelease.ps1 -Environment <environment> -Repository lzocateli/DotnetScope
   gh release list --repo lzocateli/DotnetScope --exclude-drafts
   ```

3. **Confira a trava `rollbackMinimumVersion`** no manifesto da versão atual:

   ```bash
   pwsh ./tools/scripts/ci/Get-ReleaseManifest.ps1 -Tag v<atual> -Repository lzocateli/DotnetScope -OutputPath /tmp/release-manifest.json
   jq -r .rollbackMinimumVersion /tmp/release-manifest.json
   ```

   A versão alvo não pode ser menor que esse valor.
4. **Execute na tag anterior:**

   ```bash
   gh workflow run gw-deploy.yml --repo lzocateli/DotnetScope --ref v<anterior> -f environment=<environment>
   ```

5. **Aprove** no Environment, acompanhe o smoke test e confirme a versão em execução.
6. **Comunique** o rollback, registre o motivo (o histórico de deployments registra quem, quando
   e qual tag) e abra a correção por Issue ou pelo fluxo de bug do projeto.
7. **Volte à versão corrigida por nova release** (`fix:` ou `revert:` → Release PR → deploy).
   Nunca reimplante a release defeituosa.

## 5. Quando o rollback é recusado

O guard recusa alvos abaixo do `rollbackMinimumVersion` gravado no manifesto da versão
implantada. Isso significa que a versão atual fez uma mudança sem volta (migração *contract*,
remoção de protocolo N-1, formato de dado incompatível) e que a versão anterior não funcionaria
com o estado atual. Não há bypass pelo workflow: faça **roll-forward** (correção → Release PR →
nova release → deploy). Se a trava estiver errada, corrija `rollbackMinimumVersion` em
`.github/ci-cd.json` e publique nova release.

## 6. Falhas durante o deploy

| Situação | Ação |
| --- | --- |
| Falha transitória no deploy | Reexecute o mesmo comando na mesma tag; `deploy.yml` é idempotente |
| Aprovação expirou | Dispare novamente na tag e aprove dentro do prazo |
| Smoke test falhou | Avalie rollback (seção 4) ou redeploy (seção 3); não altere o ambiente manualmente |
| Deploy concorrente bloqueado | Aguarde o deploy em andamento terminar; não cancele no meio da aplicação |
| Guard recusou | Leia o motivo no resumo do job `Guard`; branch, rascunho, prerelease em `production` e release sem atestação (`"attestation": "none"`) são recusados por desenho |

## 7. Republicação de artefatos ausentes

Republicar (`gw-release.yml` com `mode=republish` na tag) completa uma **publicação** parcial:
envia só os artefatos que faltam e finaliza a release. Redeploy (seção 3) reaplica uma release
já publicada em um Environment. Uma release em rascunho não é implantável até a republicação
terminar.

```bash
gh workflow run gw-release.yml --repo lzocateli/DotnetScope --ref vX.Y.Z -f mode=republish
```

## 8. Auditoria

- Histórico de deployments por Environment (quem, quando, qual tag):
  `gh api "repos/lzocateli/DotnetScope/deployments?environment=<environment>"`.
- Logs e resumos do workflow `Deploy` (job `Guard` registra deploy, redeploy ou rollback).
- `release-manifest.json` atestado de cada release:
  `gh attestation verify release-manifest.json --repo lzocateli/DotnetScope`.

## 9. Checklist rápido de rollback

- [ ] Incidente registrado e responsável definido.
- [ ] Versão atual e alvo identificadas (`Get-DeployedRelease.ps1`, `gh release list`).
- [ ] Alvo maior ou igual ao `rollbackMinimumVersion` da versão atual.
- [ ] `gh workflow run gw-deploy.yml --ref v<anterior> -f environment=<environment>` disparado.
- [ ] Aprovação concedida no Environment e smoke test verde.
- [ ] Versão em execução confirmada.
- [ ] Comunicação enviada e correção aberta.
- [ ] Correção lançada por nova release, sem reimplantar a versão defeituosa.
