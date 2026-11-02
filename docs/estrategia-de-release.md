# Estratégia de release

Arquivo gerenciado pelo Altivy.Groundwork (componente `release-preview`). Não edite: mudanças vão
para o catálogo. O passo a passo operacional está em
[runbooks/release-preview.md](runbooks/release-preview.md) e
[runbooks/release.md](runbooks/release.md).

Repositório: `lzocateli/DotnetScope` · branch padrão: `main` ·
aceite da candidata: `approval`.

## 1. Resumo

Toda versão estável `vX.Y.Z` é precedida por uma **candidata** `vX.Y.Z-rc.N`, publicada com os
mesmos workflows, validada no Environment `quality` e só então promovida. A estável é o mesmo
código da candidata validada: entre as duas só mudam arquivos de versão, `CHANGELOG.md` e
`.release-please-manifest.json`.

```text
PR (squash, Conventional Commit)
  └─▶ main ─▶ release-please atualiza o Release PR (versão + CHANGELOG)
          └─▶ gw-preview.yml (manual) ─▶ tag vX.Y.Z-rc.N ─▶ publish ─▶ prerelease atestada
                  └─▶ quality: deploy/smoke + aprovação humana ─▶ preview-acceptance.json
                          └─▶ Preview gate verde no Release PR
                                  └─▶ merge humano ─▶ tag vX.Y.Z ─▶ publish ─▶ release estável
                                          └─▶ production (manual, com aprovação)
```

## 2. Branches

A única branch de longa duração é a `main`. Candidatas e estáveis são **tags**, não
branches: não existem branches de homologação (`qas`, `develop`, `release/*`) nem de feed
(`nugettest/*`), e o Environment `quality` recebe a candidata pela tag `vX.Y.Z-rc.N`.

| Branch | Dono | Vida | Regra |
| --- | --- | --- | --- |
| `main` | humano | permanente | rulesets `gw-*`; só recebe merge squash de PR com `Result` verde |
| `release-please--branches--main` | release-please (GitHub App) | até o merge do Release PR | gerada; nunca edite à mão |
| `dependabot/*` | Dependabot | até o merge ou fechamento do PR | gerada |
| `feat/*`, `fix/*`, `docs/*`, `ci/*` e afins | autor do PR | até o merge | apague no merge (`--delete-branch`) |

Branch sem PR aberto e com conteúdo já integrado na `main` é resto e deve ser apagada. Antes,
confira com `git merge-base --is-ancestor origin/<branch> origin/main` ou, para squash,
`git diff origin/main origin/<branch>` sem diferença nos arquivos dela.

## 3. Versões e canais

| Canal | Versão | Origem | Tag e GitHub Release | Implantável |
| --- | --- | --- | --- | --- |
| snapshot | `X.Y.Z-preview.N` | cada push na `main` (se `publishSnapshots`) | não | nunca |
| candidata | `X.Y.Z-rc.N` | `gw-preview.yml`, versão do Release PR aberto | tag + prerelease sem *Latest* | só em `quality` |
| estável | `X.Y.Z` | merge do Release PR | tag + release *Latest* | `quality` e `production` |

- `N` é sequencial por versão: a primeira candidata de `X.Y.Z` é `rc.1`; uma correção depois dela
  gera `rc.2` no próximo disparo.
- A candidata marca a tag no **commit-base do Release PR**, isto é, o HEAD da `main` em
  que o release-please calculou a versão. Se a `main` andar, o Release PR é atualizado e
  é preciso uma nova candidata.
- Prereleases que não seguem `vX.Y.Z-rc.N` (por exemplo `preview.N`) nunca são implantáveis nem
  promovíveis.

## 4. CHANGELOG e Conventional Commits

O `CHANGELOG.md` é escrito pelo release-please a partir dos títulos dos PRs (merge squash). Nenhum
humano edita o CHANGELOG fora do Release PR.

| Título do PR | Entra no CHANGELOG | Bump |
| --- | --- | --- |
| `feat` | sim | minor |
| `fix`, `fix(deps)`, `deps`, `perf`, `revert` | sim | patch |
| `!` ou rodapé `BREAKING CHANGE:` | sim | major (antes de 1.0: minor) |
| `docs`, `chore`, `ci`, `build`, `test`, `refactor`, `style` | não | — |

O job `Conventional title` reprova títulos fora do padrão e mostra no resumo do PR o impacto na
release. As notas da candidata reproduzem a seção do Release PR com o aviso de que não é a versão
estável; as notas da estável ganham a linha "Validada na `vX.Y.Z-rc.N`".

## 5. Environments

| Environment | Refs aceitas | Revisores | Uso |
| --- | --- | --- | --- |
| `release` | `main`, `v*` | `@lzocateli` | credenciais de publicação, da candidata e da estável |
| `quality` | `v*` | obrigatórios | deploy e aceite da candidata; também aceita estáveis |
| `production` | `v*` | obrigatórios | somente estáveis com candidata validada |
| `preview` | `main` | — | snapshots no feed interno |

Os Environments, as políticas de refs e os revisores vêm de `.github/repository-policy.json` e são
aplicados por `groundwork github --sync`.

## 6. Aceite da candidata

Modo `approval` (aplicativo): o deploy em `quality` executa o smoke automatizado do `deploy.yml` e
espera a **aprovação humana** no Environment `quality`. Aprovar significa: "testei a candidata e ela
pode virar a estável".

Com o deploy bem-sucedido, o job `Record candidate acceptance` do `gw-deploy.yml`:

1. anexa à prerelease o asset `preview-acceptance.json` (`tag`, `version`, `commit`, `environment`,
   `result`, `acceptedAt`, `actor`, `run`);
2. reexecuta o CI do Release PR aberto, para o `Preview gate` reavaliar.

## 7. Gates

| Gate | Onde | Regra |
| --- | --- | --- |
| `Preview gate` | job do `gw-pr-hygiene.yml`, dentro do `Result` | o Release PR só passa com candidata `vX.Y.Z-rc.N` publicada no commit-base e aceita em `quality`, ou com waiver |
| Guard de deploy | `Assert-DeployableRelease.ps1` | candidata só em `quality`; `production` exige a candidata validada da mesma versão, com diff só em arquivos de versão e CHANGELOG, ou waiver |
| Tag | `Assert-ReleaseTag.ps1` | candidata com versão maior que a registrada; estável igual aos arquivos de versão |
| Assinatura | `release-manifest.json` atestado | candidata assinada por `gw-preview.yml`; estável por `gw-release.yml` |

### Waiver

Em emergência (correção de segurança, indisponibilidade), um commit desde a última estável pode
dispensar a candidata com o rodapé:

```text
fix: corrija a falha de autenticação

Preview-Waiver: correção de segurança publicada sem candidata por indisponibilidade de quality
```

O waiver libera o `Preview gate` e o guard de `production`, aparece no resumo do deploy e nas notas
da release. É exceção auditável, não atalho: registre o motivo real.

## 8. Automático e humano

| Etapa | Automático | Humano |
| --- | --- | --- |
| PR comum | CI, título, Gitleaks, snapshot | revisão e merge |
| Release PR | versão e CHANGELOG | — |
| Candidata | N, tag, publish, atestação, prerelease, notas | disparar `gw-preview.yml`; aprovar `release` |
| `quality` | deploy, smoke e registro do aceite | testar e aprovar `quality` |
| `Preview gate` | verificação completa | waiver, se for o caso |
| Estável | tag, publish, release, notas com a candidata | merge do Release PR; aprovar `release` |
| `production` | guard | disparar e aprovar `production` |
| Repositório | Environments, variáveis e rulesets (`groundwork github --sync`) | secrets ausentes (`--secrets`) |

## 9. Falhas e rollback

- **Candidata reprovada em `quality`:** corrija por PR comum; o release-please atualiza o Release PR
  e o próximo disparo gera `rc.N+1`. A candidata reprovada fica como prerelease, sem aceite.
- **`Preview gate` vermelho:** não há candidata aceita no commit-base atual (a `main` andou,
  o aceite falhou ou o Environment não confere). Gere e valide nova candidata.
- **`production` recusado:** a estável difere da candidata além dos arquivos de versão (alguém
  integrou código depois da validação). Lance uma nova versão com candidata.
- **Rollback:** deploy de uma estável anterior publicada, com a trava `rollbackMinimumVersion`; nunca
  apague tags nem releases (ver [runbooks/deploy-e-rollback.md](runbooks/deploy-e-rollback.md)).
