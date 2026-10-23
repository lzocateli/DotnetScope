# Governança do DotnetScope

## Responsabilidade

`@lzocateli` é o proprietário e único mantenedor com autoridade para aceitar
escopo, aprovar contribuições e integrar alterações na `main`. Contribuições
externas chegam por fork e PR, conforme [CONTRIBUTING](CONTRIBUTING.md).
Reviews de terceiros são consultivos: não substituem a decisão do mantenedor.

## Proteção da `main`

As políticas do repositório são um contrato versionado em
[`repository-policy.json`](repository-policy.json), gerado pelo Altivy.Groundwork
e aplicado com `groundwork github --sync` pelo mantenedor (ver
[REPOSITORY-POLICY](REPOSITORY-POLICY.md)). `groundwork github` confere a
configuração remota sem alterá-la e retorna `0` quando ela corresponde ao contrato.

| Ruleset | Controles | Exceções |
| --- | --- | --- |
| `gw-main-integrity` | Bloqueia exclusão e force-push, exige histórico linear e o check `Result` com a branch atualizada | Nenhuma, nem para o proprietário |
| `gw-main-review` | Exige PR com uma aprovação, descarta aprovações após novos commits, exige conversas resolvidas e merge somente por squash | Papel de administrador, somente por PR |
| `gw-release-tags` | Restringe criação, atualização e exclusão de `refs/tags/v*` | Somente o GitHub App `Altivy Release` |

O check `Result` (job do [`ci.yml`](workflows/ci.yml)) agrega higiene do PR
(título Conventional Commit, actionlint e arquivos gerenciados),
`Gitleaks / Full History` e os jobs `Build, tests and Native AOT` de
`linux-x64`, `win-x64` e `osx-arm64`; falha se qualquer um deles não terminar com
sucesso. Novos jobs entram em `needs` do `Result`, sem alterar o ruleset.

O [CODEOWNERS](CODEOWNERS) cobre todos os arquivos com `@lzocateli`. Ele
sozinho não bloqueia merge: a imposição depende dos rulesets ativos no GitHub.

### Tags de release e Environments

Tags `v*` são criadas somente pelo GitHub App `Altivy Release`, no merge do
Release PR aberto pelo release-please; nem o administrador cria tags manualmente.
Antes de publicar, `gw-release.yml` confere que a tag corresponde às versões do
repositório e que o commit é ancestral de `origin/main`.

O Environment `release` exige aprovação de `@lzocateli` para anexar os assets à
release em rascunho e, de novo, para atualizar o tap Homebrew no
`post-release.yml` (o `HOMEBREW_TAP_TOKEN` existe somente nele). O Environment
`winget` exige aprovação de `@lzocateli` tanto para submissões automáticas quanto
manuais e só permite as referências `main` e `v*`. O `prevent_self_review` está
desativado para permitir que o único mantenedor aprove conscientemente uma
execução que ele próprio iniciou; o GitHub também permite bypass administrativo
(`can_admins_bypass=true`), atualmente limitado ao mesmo mantenedor. PRs e
validações sem `submit=true` não executam o job nem acessam
`WINGET_CREATE_GITHUB_TOKEN`.

### PRs de terceiros

1. O mantenedor autoriza a execução dos workflows de colaboradores externos
   após inspecionar o conteúdo; autorização para rodar CI não é aprovação do PR.
2. O CI deve passar e as conversas devem ser resolvidas.
3. O mantenedor revisa e registra sua aprovação antes de integrar.
4. Somente o mantenedor pode efetuar o merge.

### PRs do próprio mantenedor

O GitHub não permite registrar uma review `Approve` no próprio PR. O mantenedor
pode integrar seu PR pela exceção de revisão de `gw-main-review`,
registrando no PR a decisão e as evidências. A exceção **não** dispensa o
check `Result`, a atualização da branch ou as regras de `gw-main-integrity`;
ela não permite push direto, force-push ou exclusão.

Tecnicamente, a exceção também permite ao mantenedor integrar um PR de terceiro
sem review formal; a política do projeto é registrar a aprovação nesses casos.
Ela não é uma autoaprovação formal nem uma verificação automática de autoria.

### Limites da garantia

A exceção está vinculada ao papel `RepositoryRole: Admin` (ID `5`), e não a
um usuário individual. Na consulta de 2026-10-22, `@lzocateli` era o único
colaborador administrador. **Não adicione outro administrador** sem revisar
essa política: ele herdaria a exceção e poderia alterar os rulesets. Nenhum
bot ou GitHub App foi incluído no bypass dos rulesets da `main`; o GitHub App
`Altivy Release` tem bypass somente em `gw-release-tags`.

O proprietário ainda pode editar ou remover regras nas configurações. Portanto,
essa é uma proteção operacional, não uma barreira contra comprometimento da
conta administrativa. Proteja a conta com autenticação forte e revise tokens,
Apps e permissões periodicamente. Terceiros podem deixar reviews públicos;
o GitHub não permite impedir toda opinião de revisão em um projeto público,
mas essas reviews não concedem autorização para merge.

## Issues, decisões e versões

- Bugs: reprodução, versão, plataforma e teste de regressão.
- Melhorias: problema, resultado esperado, escopo e compatibilidade.
- Tarefas técnicas e spikes: critérios verificáveis e vínculo com o plano.
- Dúvidas: use o formulário de suporte; não publique dados sensíveis.
- Vulnerabilidades: somente pelo [canal privado](SECURITY.md).

O mantenedor classifica as Issues com as labels existentes, decide prioridade
e vincula fases do [plano](../docs/plano-implementacao.md). Issues aceitas não
concedem permissão de escrita. ADRs começam como `proposed`; aceite e mudança
de escopo exigem decisão humana registrada.

Na consulta de 2026-10-22 não havia Issues de trabalho nem milestones, apenas
um PR no histórico de Issues/PRs. Portanto, a regra Issue → fase → PR não é
rastreável para todas as entregas anteriores. Não foram criadas Issues
retroativas nem inventadas aprovações; novas mudanças devem seguir o fluxo
documentado. Pendências já identificadas incluem as
[limitações de release](../docs/release.md#limitações-conhecidas), a janela de
[suporte de segurança](SECURITY.md#versões-suportadas) anterior à 1.0 e R14
parcial no [modelo de ameaças](../docs/modelo-de-ameacas-f7.md#6-requisitos-derivados-para-a-f7).
Sua implementação exige Issues com escopo e autorização próprios.
O [plano de remediação pós-F9](../docs/plano-remediacao-governanca.md) organiza
essas pendências e seus critérios de aceite.

Publicação e aprovação do Environment `release` são controles separados do
merge de PRs comuns: somente o merge do Release PR inicia uma release. Consulte o
[processo de release](../docs/release.md).

## Verificação e recuperação

Sinais de divergência: push direto aceito, merge por terceiro, checks ausentes
ou exceção administrativa ampliada. Na raiz de um clone, com `gh` autenticado
como proprietário, os comandos abaixo são somente leitura:

```bash
groundwork github
gh api repos/lzocateli/DotnetScope/rulesets
gh api repos/lzocateli/DotnetScope/rules/branches/main
gh api repos/lzocateli/DotnetScope/rules/tags/v0.0.0
gh api repos/lzocateli/DotnetScope/environments/winget
gh api repos/lzocateli/DotnetScope/environments/winget/deployment-branch-policies
gh api repos/lzocateli/DotnetScope/collaborators
gh api repos/lzocateli/DotnetScope/private-vulnerability-reporting
gh api repos/lzocateli/DotnetScope/actions/permissions/fork-pr-contributor-approval
```

Resultado esperado: `groundwork github` com código `0`, os dois rulesets `gw-*` de
branch ativos sobre `refs/heads/main` (o primeiro sem bypass, o segundo com bypass
administrativo somente por PR), o check `Result` vinculado ao GitHub Actions,
apenas o proprietário como administrador, `gw-release-tags` ativo com bypass
somente do App, Environment `winget` com aprovação do mantenedor e
branches/tags `main`/`v*`, relatos privados habilitados e
`all_external_contributors` para workflows.

Se houver divergência, suspenda merges e publicações, remova acessos indevidos,
restaure as regras em **Settings > Rules > Rulesets** e repita as consultas.
Se houver suspeita de comprometimento, revogue tokens e sessões afetados e
acione o proprietário; vulnerabilidades seguem a política de segurança.
Não desative regras para contornar CI quebrado: corrija o workflow ou atualize
o nome do check com uma decisão explícita e rastreável.
