# Governança do DotnetScope

## Responsabilidade

`@lzocateli` é o proprietário e único mantenedor com autoridade para aceitar
escopo, aprovar contribuições e integrar alterações na `main`. Contribuições
externas chegam por fork e PR, conforme [CONTRIBUTING](CONTRIBUTING.md).
Reviews de terceiros são consultivos: não substituem a decisão do mantenedor.

## Proteção da `main`

Configuração aplicada e consultada pela API do GitHub em 2026-10-22, com
autorização humana nesta revisão de governança:

| Ruleset | Controles | Exceções |
| --- | --- | --- |
| [`main-integrity`](https://github.com/lzocateli/DotnetScope/rules/25638208) | Exige PR, conversas resolvidas, branch atualizada e os quatro checks abaixo; bloqueia exclusão e force-push | Nenhuma, nem para o proprietário |
| [`main-maintainer-only`](https://github.com/lzocateli/DotnetScope/rules/25638218) | Restringe atualizações e exige uma aprovação do code owner, descartada após novos commits | Papel de administrador, somente por PR |

Checks exigidos da integração GitHub Actions, pelos nomes exatos dos jobs:

- `Full History` (workflow `Gitleaks`);
- `Build, tests and Native AOT (linux-x64)`;
- `Build, tests and Native AOT (win-x64)`;
- `Build, tests and Native AOT (osx-arm64)`.

O [CODEOWNERS](CODEOWNERS) cobre todos os arquivos com `@lzocateli`. Ele
sozinho não bloqueia merge: a imposição depende dos rulesets ativos no GitHub.
Novos nomes de checks precisam ser atualizados nos rulesets junto com os
workflows, para não bloquear PRs indefinidamente.

### PRs de terceiros

1. O mantenedor autoriza a execução dos workflows de colaboradores externos
   após inspecionar o conteúdo; autorização para rodar CI não é aprovação do PR.
2. O CI deve passar e as conversas devem ser resolvidas.
3. O mantenedor revisa e registra sua aprovação antes de integrar.
4. Somente o mantenedor pode efetuar o merge.

### PRs do próprio mantenedor

O GitHub não permite registrar uma review `Approve` no próprio PR. O mantenedor
pode integrar seu PR pela exceção de revisão de `main-maintainer-only`,
registrando no PR a decisão e as evidências. A exceção **não** dispensa os
checks, a atualização da branch, a resolução das conversas ou a exigência de PR
de `main-integrity`; ela não permite push direto, force-push ou exclusão.

Tecnicamente, a exceção também permite ao mantenedor integrar um PR de terceiro
sem review formal; a política do projeto é registrar a aprovação nesses casos.
Ela não é uma autoaprovação formal nem uma verificação automática de autoria.

### Limites da garantia

A exceção está vinculada ao papel `RepositoryRole: Admin` (ID `5`), e não a
um usuário individual. Na consulta de 2026-10-22, `@lzocateli` era o único
colaborador administrador. **Não adicione outro administrador** sem revisar
essa política: ele herdaria a exceção e poderia alterar os rulesets. Nenhum
bot ou GitHub App foi incluído na lista de bypass.

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
merge. Consulte o [processo de release](../docs/release.md); aprovação de um PR
não autoriza criação de tag ou publicação.

## Verificação e recuperação

Sinais de divergência: push direto aceito, merge por terceiro, checks ausentes
ou exceção administrativa ampliada. Na raiz de um clone, com `gh` autenticado
como proprietário, os comandos abaixo são somente leitura:

```bash
gh api repos/lzocateli/DotnetScope/rulesets
gh api repos/lzocateli/DotnetScope/rulesets/25638208
gh api repos/lzocateli/DotnetScope/rulesets/25638218
gh api repos/lzocateli/DotnetScope/rules/branches/main
gh api repos/lzocateli/DotnetScope/collaborators
gh api repos/lzocateli/DotnetScope/private-vulnerability-reporting
gh api repos/lzocateli/DotnetScope/actions/permissions/fork-pr-contributor-approval
```

Resultado esperado: ambos os rulesets ativos sobre `refs/heads/main`, primeiro
sem bypass, segundo com bypass administrativo somente por PR, quatro checks
vinculados ao GitHub Actions, apenas o proprietário como administrador,
relatos privados habilitados e `all_external_contributors` para workflows.

Se houver divergência, suspenda merges e publicações, remova acessos indevidos,
restaure as regras em **Settings > Rules > Rulesets** e repita as consultas.
Se houver suspeita de comprometimento, revogue tokens e sessões afetados e
acione o proprietário; vulnerabilidades seguem a política de segurança.
Não desative regras para contornar CI quebrado: corrija o workflow ou atualize
o nome do check com uma decisão explícita e rastreável.
