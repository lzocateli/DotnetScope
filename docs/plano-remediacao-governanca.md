# Plano de remediação de governança e contribuição pública

| Campo | Valor |
| --- | --- |
| Estado | proposto para revisão; execução das remediações ainda não autorizada |
| Origem | IA assistida |
| Revisor humano | Lincoln Zocateli, revisão das remediações pendente |
| Data | 2026-10-22 |
| Solicitação | publicar a revisão documental por PR, planejar pendências e abrir Issues; avaliar a origem dos templates no Altivy.Groundwork |
| Relação com o plano original | manutenção após F9, sem reabrir ou alterar aprovações históricas |

## Objetivo e limites

Eliminar as pendências verificadas na revisão de contribuição pública,
segurança operacional e release. O [plano de implementação](plano-implementacao.md)
continua sendo a fonte das fases do produto; este documento organiza a
remediação pós-F9, com uma Issue por resultado verificável.

Este pedido autoriza documentação, commit, push de branch, criação de PR e
Issues. Não autoriza merge, execução das remediações, publicação de versões,
alteração dos workflows de release ou implementação no Groundwork. Cada Issue
exige escopo revisado e autorização explícita antes da execução.

Proteções da `main`, canal privado de vulnerabilidades e autorização de CI
externo já estão ativos e descritos em [GOVERNANCE](../.github/GOVERNANCE.md).
Os documentos novos e templates só entram no fluxo público após merge do PR.

## Ordem de execução

Acompanhamento: [DotnetScope #2](https://github.com/lzocateli/DotnetScope/issues/2).
As Issues abaixo foram criadas a pedido do mantenedor; isso não altera seu
estado para implementação aprovada.

1. D6 estabelece rastreabilidade prospectiva e verifica controles operacionais.
2. D1, D2 e D3 endurecem a publicação; podem ser executadas em branches
   independentes após D6, mas alterações compartilhadas em release devem ser
   revalidadas juntas.
3. D4 define suporte e D5 completa a auditoria global; ambas dependem apenas
   de D6 e de decisões humanas próprias.
4. D8 avalia a confiança no metadata; qualquer implementação resultante exige
   decisão humana e nova tarefa, antes da revisão D7.
5. D7 é o gate de segurança anterior à 1.0, após D1 a D6 e a decisão de D8.
6. U1 a U3 pertencem ao Groundwork e não bloqueiam o PR documental do
   DotnetScope: a mitigação local já foi feita.

Prioridades são propostas, não SLAs. Nenhuma conclusão pode ser marcada sem
evidência; ambientes e plataformas indisponíveis ficam como `NOT RUN`.

## Remediações do DotnetScope

### D1 — Impor imutabilidade dos assets de release

- **Prioridade:** P1.
- **Issue:** [DotnetScope #3](https://github.com/lzocateli/DotnetScope/issues/3).
- **Evidência:** `release.yml` usa `gh release upload --clobber`, embora a
  política de CI exija artefatos imutáveis.
- **Escopo:** definir publicação inicial e reexecução idempotente; rejeitar
  divergência em bytes ou hashes de assets publicados, sem substituir conteúdo.
- **Aceite:**
  - reexecução com conteúdo idêntico não altera nem duplica assets;
  - conteúdo divergente falha explicitamente antes de sobrescrita;
  - testes cobrem release inexistente, existente idêntica, divergente e parcial;
  - regra para publicação parcial e recuperação é documentada e revisada;
  - validação usa fixtures ou repositório descartável, nunca release real.
- **Dependências:** D6.
- **Não escopo:** republicar versões existentes ou alterar assinatura de binários.

### D2 — Verificar origem das tags e proteger referências de release

- **Prioridade:** P1.
- **Issue:** [DotnetScope #4](https://github.com/lzocateli/DotnetScope/issues/4).
- **Evidência:** o workflow valida formato da tag e `VersionPrefix`, mas não
  verifica se seu commit pertence à história de `main`; não foi criado ruleset
  de tags nesta revisão.
- **Escopo:** validar ancestralidade antes de publicação e propor proteção de
  criação, atualização e exclusão de tags `v*`, restrita ao mantenedor.
- **Aceite:**
  - tag apontando para commit fora de `main` é recusada antes de publicação;
  - tag anotada válida é resolvida para o commit correto e aceita;
  - testes cobrem clone raso, tag inválida, versão divergente e commit externo;
  - configuração de proteção é revisada antes de aplicação remota;
  - procedimento de recuperação não exige mover silenciosamente tags públicas.
- **Dependências:** D6.
- **Não escopo:** criar tags, publicar versão ou mover referências existentes.

### D3 — Exigir autorização humana no caminho manual do winget

- **Prioridade:** P1.
- **Issue:** [DotnetScope #5](https://github.com/lzocateli/DotnetScope/issues/5).
- **Evidência:** `workflow_dispatch` com `submit=true` chega ao Environment
  `winget`, hoje sem revisor, sem passar por nova aprovação em `release`.
- **Escopo:** decidir entre aprovação própria do Environment e um gate
  equivalente para submissão manual, mantendo o fluxo automático coerente.
- **Aceite:**
  - submissão manual exige aprovação exclusiva do mantenedor;
  - PRs e execuções somente de validação não acessam o token de submissão;
  - gates, branches e tags permitidos são verificados pela API;
  - matriz cobre automático, manual com/sem submissão, PR e recusa de aprovação;
  - testes não submetem PR ao winget nem expõem credenciais;
  - se houver segunda aprovação no fluxo automático, o custo operacional é
    explicitamente aceito antes da implementação.
- **Dependências:** D6.
- **Não escopo:** renovar tokens ou publicar pacote.

### D4 — Definir política de suporte de segurança antes da 1.0

- **Prioridade:** P2, obrigatória antes da 1.0.
- **Issue:** [DotnetScope #6](https://github.com/lzocateli/DotnetScope/issues/6).
- **Evidência:** existem releases pré-1.0, mas não uma janela de suporte e
  backport por linha; SECURITY agora explicita essa limitação.
- **Escopo:** submeter ao mantenedor matriz de versões suportadas, início/fim
  do suporte, backports, descontinuação e comunicação.
- **Aceite:**
  - decisão humana define a política pré-1.0 e da linha 1.x;
  - SECURITY, README e processo de release usam a mesma política;
  - não há prazo inventado nem suporte retroativo implícito;
  - canal privado, responsável e objetivos de resposta são conferidos.
- **Dependências:** D6.
- **Não escopo:** prometer SLA ou aceitar riscos sem decisão humana.

### D5 — Completar R14 de rastreabilidade no escopo global

- **Prioridade:** P2.
- **Issue:** [DotnetScope #7](https://github.com/lzocateli/DotnetScope/issues/7).
- **Evidência:** o modelo de ameaças registra R14 parcial: falta a identidade
  de quem elevou, embora data, origem, hash e escopo já sejam registrados.
- **Escopo:** decidir e implementar identidade de auditoria adequada a
  Linux/macOS/Windows, distinguindo identidade verificada de variável declarada.
- **Aceite:**
  - decisão documenta confiança, privacidade e semântica de autoria;
  - `SUDO_USER` e outras entradas não confiáveis não são tratadas como prova
    de identidade nem usadas para autorizar ações;
  - manifestos antigos continuam legíveis ou têm migração explícita;
  - testes reais e de contrato cobrem sudo, root direto, Windows elevado,
    campo ausente, dado adulterado e redaction;
  - modelo de ameaças e CLI deixam claro o que é evidência e o que é declaração.
- **Dependências:** D6.
- **Não escopo:** coletar perfil completo, tokens, ambiente ou dados excessivos.

### D6 — Estabelecer rastreabilidade e verificar controles locais

- **Prioridade:** P1.
- **Issue:** [DotnetScope #8](https://github.com/lzocateli/DotnetScope/issues/8).
- **Evidência:** ausência de Issues de trabalho e milestones no histórico
  consultado; checklist do hook administrativo ainda não tinha comprovação
  por clone.
- **Escopo:** adotar Issue → decisão de escopo → branch → PR para manutenção
  pós-F9; organizar marcos sem inventar execução histórica; conferir hooks
  administrativos e os rulesets atuais.
- **Aceite:**
  - este plano e suas Issues têm vínculos e dependências verificáveis;
  - cada nova remediação tem autorização humana referenciada antes da execução;
  - mantenedor decide se usa milestone pós-F9 e quais entregas inclui;
  - clones administrativos em uso têm `core.hooksPath=.githooks` e smoke real
    aprovado, com data e ambiente; clones não acessíveis ficam `NOT RUN`;
  - regras de `main`, único administrador, CI, canal privado e workflows
    externos são conferidos sem push destrutivo de teste;
  - não são criados falsos registros retroativos de aprovação ou conclusão.
- **Dependências:** nenhuma.
- **Não escopo:** alterar história Git ou declarar todos os clones validados
  a partir da validação de um único ambiente.

### D7 — Concluir revisão de segurança anterior à 1.0

- **Prioridade:** P1, gate anterior à 1.0.
- **Issue:** [DotnetScope #9](https://github.com/lzocateli/DotnetScope/issues/9).
- **Evidência:** o plano exige revisão de segurança antes da 1.0; a revisão
  registrada da F7 não equivale a revisar todo o produto e sua distribuição.
- **Escopo:** revisão autorizada de user/global, metadata, extração,
  filesystem, ambiente, atualização e cadeia de release, com matriz por SO/RID.
- **Aceite:**
  - relatório identifica commit, escopo, método, evidências e limitações;
  - achados têm reprodução segura, severidade e remediações rastreáveis;
  - nenhum achado crítico/alto fica aberto sem decisão humana conforme a política;
  - testes negativos e gate de release são verificados em ambientes descartáveis;
  - dependências e licenças exatas são conferidas, sem inferir cobertura de
    plataformas que não foram executadas.
- **Dependências:** D1, D2, D3, D4, D5, D6 e D8; qualquer implementação
  aprovada a partir de D8 também deve terminar antes desta revisão final.
- **Não escopo:** testes destrutivos em máquinas de usuários ou divulgação
  pública de vulnerabilidades não corrigidas.

### D8 — Decidir sobre assinatura destacada do metadata

- **Prioridade:** P2.
- **Issue:** [DotnetScope #10](https://github.com/lzocateli/DotnetScope/issues/10).
- **Evidência:** a verificação PKCS#7 `.p7s` ficou em avaliação no plano,
  enquanto TLS e allowlist são os mecanismos de confiança atuais.
- **Escopo:** spike com evidências sobre formato e disponibilidade oficial,
  trust anchors, revogação, rotação, comportamento offline e compatibilidade AOT.
- **Aceite:**
  - alternativas, custo, risco e limites são apresentados ao mantenedor;
  - disponibilidade por canal e políticas de falha são verificadas;
  - prova técnica usa ambiente descartável e não altera a confiança de produção;
  - decisão humana registra implementar, adiar com risco conhecido ou descartar;
  - se aprovada implementação, criar tarefa própria, testes e migração antes
    de executar; não tratar a abertura deste spike como aprovação de dependência.
- **Dependências:** D6.
- **Não escopo:** adicionar biblioteca, mudar allowlist ou impor assinatura
  sem decisão humana e verificação de licença.

## Melhorias na origem dos templates

Avaliação read-only do Altivy.Groundwork: o manifesto local atribui os templates
ao componente `core`, catálogo `2026.10.0`. As correções locais preservam os
hashes originais do manifesto; não devem reescrevê-los para esconder adaptações.
As Issues upstream contêm a evidência técnica no repositório proprietário.

### U1 — Tornar labels geradas utilizáveis e verificáveis

- **Prioridade:** P1.
- **Issue upstream:** [Altivy.Groundwork #3](https://github.com/altivy-it/altivy-groundwork/issues/3).
- **Causa:** templates declaram labels que não pertencem ao conjunto padrão
  do GitHub, sem contrato de provisionamento ou validação remota.
- **Proposta:** definir um vocabulário canônico e um contrato de labels;
  oferecer diagnóstico explícito das ausentes e sincronização somente opt-in,
  idempotente e com dry-run, caso o mantenedor escolha manter labels customizadas.
- **Aceite:** todos os componentes, inclusive Spec Kit, usam labels do
  contrato; testes renderizam as combinações suportadas e comparam as labels;
  sem credencial não há mutação remota ou alegação de estado validado; labels
  locais não são removidas/renomeadas silenciosamente; documentação distingue
  aplicação de arquivos e configuração do GitHub.
- **Dependências:** nenhuma.
- **Não escopo:** trocar automaticamente o vocabulário de projetos consumidores.

### U2 — Condicionar instruções dos formulários ao processo instalado

- **Prioridade:** P1.
- **Issue upstream:** [Altivy.Groundwork #4](https://github.com/altivy-it/altivy-groundwork/issues/4).
- **Causa:** formulários do `core` encaminham ao Spec Kit mesmo sem o
  componente instalado; outros templates já condicionam essa orientação.
- **Proposta:** usar a composição de componentes para emitir o processo correto,
  preservando o requisito de autorização humana em ambos os casos.
- **Aceite:** `core`, `core + secret-scanning` e composições com `speckit`
  possuem testes positivos e negativos de conteúdo; nenhum formulário manda
  executar um fluxo indisponível; migração preserva customizações locais.
- **Dependências:** nenhuma; testes de composição podem compartilhar fixtures com U1.
- **Não escopo:** impor Spec Kit a todos os consumidores.

### U3 — Separar políticas genéricas de estado operacional do consumidor

- **Prioridade:** P2.
- **Issue upstream:** [Altivy.Groundwork #5](https://github.com/altivy-it/altivy-groundwork/issues/5).
- **Causa:** textos gerados afirmam estado de ciclo de vida, como canal de
  segurança em preparação e ausência de releases, sem consultar esse estado;
  exemplos de commits também divergem da regra de imperativo do próprio template.
- **Proposta:** textos neutros ou sementes explicitamente pertencentes ao
  projeto para estado operacional; corrigir exemplos e ampliar testes de
  coerência do catálogo sem fazer rede em testes unitários.
- **Aceite:** geração não declara canal habilitado, release suportada ou
  proteção de branch sem evidência; exemplos obedecem à regra textual;
  semântica seed/managed e atualização é documentada e testada;
  parametrização só é adicionada quando necessária, sem copiar governança
  exclusiva do DotnetScope para um gerador genérico.
- **Dependências:** nenhuma.
- **Não escopo:** mudar políticas de suporte ou regras remotas de consumidores.

## Validação e encerramento

- Documentação: links, âncoras, YAML, labels existentes e diff sem whitespace
  inválido; nenhum build de produto é necessário para este PR documental.
- Commit: hook Gitleaks obrigatório executado com a imagem fixada em container.
- Publicação: branch própria, PR para `main`, checks observados e sem merge.
- Remediações: testes mínimos do recorte, build/AOT quando aplicável e
  evidências proporcionais ao risco; publicação real exige autorização própria.
- Encerramento: cada Issue só fecha após aceite validado, documentação coerente
  e merge autorizado; manter itens adiados com decisão e motivo explícitos.
