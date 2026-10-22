# Plano de remediação de governança e contribuição pública

| Campo | Valor |
| --- | --- |
| Estado | execução parcial: D1, D2 e D4 implementados; D3, D5 e D6 com validações pendentes; D8 adiado com risco conhecido sem bloquear novas versões; D7 bloqueado |
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

A solicitação inicial autorizou a revisão documental, o planejamento e a
abertura de Issues. Em 2026-10-22, o mantenedor autorizou explicitamente D1–D8
e U1–U3 e solicitou a atualização do status. As decisões humanas abaixo foram
registradas no tracker e nas Issues correspondentes. Em seguida, o mantenedor
autorizou commit, push e abertura de PR para revisão; merge, publicação de
versões e implementação adicional no Groundwork permanecem fora do escopo.

## Decisões humanas registradas

- **D3:** usar o Environment compartilhado `winget`; `@lzocateli` aprova
  submissões automáticas e manuais, aceitando a aprovação adicional no fluxo
  automático.
- **D4:** versões pré-1.0 sem suporte ou backports; na linha 1.x, somente a
  minor mais recente é suportada até `2.0.0`; sem SLA.
- **D5:** preservar `SUDO_USER` somente como declaração do ambiente, nunca como
  identidade verificada ou autorização; omitir em root direto sem a variável,
  Windows elevado e escopo `user`; não registrar dados confidenciais.
- **D6:** não criar milestone pós-F9.
- **D8:** adiar a assinatura com risco conhecido; a ausência de assinatura
  verificada não impede novas versões. Manter TLS, allowlist e SHA-512 até nova
  decisão e evidência verificável.

Proteções da `main`, tags de release, canal privado de vulnerabilidades e
autorização de CI externo estão descritos em
[GOVERNANCE](../.github/GOVERNANCE.md). As mudanças locais ainda não foram
integradas; não foram criadas releases nem submetidos pacotes externos.

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
4. D8 avaliou a confiança no metadata; o mantenedor decidiu adiar a assinatura
   com risco conhecido. Essa decisão não bloqueia novas versões.
5. D7 é o gate de segurança anterior à 1.0, após D1 a D6 e uma decisão humana
   registrada para D8.
6. U1 a U3 pertencem ao Groundwork e não bloqueiam o PR documental do
   DotnetScope: a mitigação local já foi feita.

Prioridades são propostas, não SLAs. Nenhuma conclusão pode ser marcada sem
evidência; ambientes e plataformas indisponíveis ficam como `NOT RUN`.

## Remediações do DotnetScope

### D1 — Impor imutabilidade dos assets de release

- **Prioridade:** P1.
- **Issue:** [DotnetScope #3](https://github.com/lzocateli/DotnetScope/issues/3).
- **Evidência inicial:** `release.yml` usava `gh release upload --clobber`,
  embora a política de CI exigisse artefatos imutáveis.
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
- **Andamento em 2026-10-22:** implementação local concluída. A reexecução agora
  compara SHA-256, preserva assets idênticos, recusa divergências antes de
  qualquer upload e envia somente assets ausentes. Fixtures cobrem release
  inexistente, conjunto idêntico, divergência e recuperação parcial. Os testes
  passaram; nenhuma release real foi alterada.

### D2 — Verificar origem das tags e proteger referências de release

- **Prioridade:** P1.
- **Issue:** [DotnetScope #4](https://github.com/lzocateli/DotnetScope/issues/4).
- **Evidência inicial:** o workflow validava formato da tag e `VersionPrefix`,
  mas não verificava se seu commit pertencia à história de `main`; não havia
  ruleset de tags.
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
- **Andamento em 2026-10-22:** implementação local concluída. A validação de
  tag e ancestralidade passou nas fixtures (tag anotada, clone raso, formato,
  versão divergente e commit externo). O ruleset ativo `release-tags-protected`
  foi aplicado e consultado via API (ID `25666467`); nenhuma tag foi criada ou
  movida.

### D3 — Exigir autorização humana no caminho manual do winget

- **Prioridade:** P1.
- **Issue:** [DotnetScope #5](https://github.com/lzocateli/DotnetScope/issues/5).
- **Evidência inicial:** `workflow_dispatch` com `submit=true` chegava ao
  Environment `winget` sem revisor e sem passar por nova aprovação em `release`.
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
- **Andamento em 2026-10-22:** o gate de origem e a matriz sintética passaram.
  O Environment `winget` agora exige revisão de `@lzocateli`, está limitado a
  `main` e `v*`, e as regras de bypass/self-review foram documentadas. Recusa
  real de aprovação e submissão externa não foram exercitadas; D3 permanece
  parcial quanto a esse teste operacional.

### D4 — Definir política de suporte de segurança antes da 1.0

- **Prioridade:** P2, obrigatória antes da 1.0.
- **Issue:** [DotnetScope #6](https://github.com/lzocateli/DotnetScope/issues/6).
- **Evidência:** existiam releases pré-1.0 sem janela de suporte ou backport
  por linha.
- **Escopo:** submeter ao mantenedor matriz de versões suportadas, início/fim
  do suporte, backports, descontinuação e comunicação.
- **Aceite:**
  - decisão humana define a política pré-1.0 e da linha 1.x;
  - SECURITY, README e processo de release usam a mesma política;
  - não há prazo inventado nem suporte retroativo implícito;
  - canal privado, responsável e objetivos de resposta são conferidos.
- **Dependências:** D6.
- **Não escopo:** prometer SLA ou aceitar riscos sem decisão humana.
- **Andamento em 2026-10-22:** decisão humana registrada e documentação alinhada
  no [README](../README.md), [SECURITY](../.github/SECURITY.md) e
  [processo de release](release.md). A política não cria SLA nem suporte
  retroativo.

### D5 — Completar R14 de rastreabilidade no escopo global

- **Prioridade:** P2.
- **Issue:** [DotnetScope #7](https://github.com/lzocateli/DotnetScope/issues/7).
- **Evidência:** o R14 original previa identidade do usuário que elevou,
  embora o projeto não pudesse comprovar identidade multiplataforma.
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
- **Andamento em 2026-10-22:** o manifesto recebeu o campo opcional
  `declaredInvoker` para `SUDO_USER` limitado em instalações globais Linux/macOS.
  Ele é documentado como declaração não verificada, nunca como prova ou
  autorização; escopo user e Windows omitem o campo. Testes unitários cobrem
  política, persistência, redaction e compatibilidade com manifestos antigos.
  Build, format, testes e Native AOT passaram no container. Testes E2E elevados
  em macOS/Windows e o cenário Linux com `sudo` ainda não foram executados; D5
  permanece parcial quanto à matriz de plataformas.

### D6 — Estabelecer rastreabilidade e verificar controles locais

- **Prioridade:** P1.
- **Issue:** [DotnetScope #8](https://github.com/lzocateli/DotnetScope/issues/8).
- **Evidência:** Issues de trabalho e vínculo de rastreabilidade foram
  estabelecidos durante esta remediação; o hook precisava de smoke no clone
  administrativo disponível.
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
- **Andamento em 2026-10-22:** parcial por cobertura de clones. A API do GitHub
  confirmou os rulesets `main-integrity` (sem bypass),
  `main-maintainer-only` (bypass administrativo somente por PR) e
  `release-tags-protected` (ID `25666467`). Também foram conferidos os quatro
  checks, `@lzocateli` como único administrador, relatos privados habilitados,
  aprovação `all_external_contributors` e o Environment `winget` com revisão
  obrigatória e referências `main`/`v*`. O mantenedor decidiu não criar
  milestone.
- No clone disponível, `core.hooksPath=.githooks` foi confirmado e
  `git hook run pre-commit` passou em 2026-10-22 com Podman rootless e
  `cgroupfs` selecionado por wrapper temporário; Gitleaks concluiu sem findings
  no diff staged vazio. Outros clones administrativos não estavam disponíveis
  e permanecem `NOT RUN`; não se infere a configuração deles. O D6 segue parcial
  até identificar/validar os clones em uso.

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
- **Andamento em 2026-10-22:** bloqueada pela validação operacional pendente de
  D3 e pela matriz de plataformas de D5/D6; a decisão humana D8 foi registrada
  como adiamento com risco conhecido e não é gate para novas versões. A revisão
  independente final não foi iniciada nem alegada.

### D8 — Decidir sobre assinatura destacada do metadata

- **Prioridade:** P2.
- **Issue:** [DotnetScope #10](https://github.com/lzocateli/DotnetScope/issues/10).
- **Evidência inicial:** TLS, allowlist e hash SHA-512 eram os controles; as
  assinaturas destacadas dos manifests ainda não tinham sido validadas contra
  uma chave oficial.
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
- **Andamento do estudo em 2026-10-22:** os endpoints oficiais consultados
  ([index `.sig`](https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json.sig)
  e [manifest 10.0 `.sig`](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json.sig))
  fornecem assinaturas em formato OpenPGP; os endpoints `.p7s` retornaram 404.
  A chave
  [release-key-2023.asc](https://dotnet.microsoft.com/download/dotnet/release-key-2023.asc)
  foi importada em keyring temporário, fingerprint
  `A30E1CAFB4BBD279FEC7E47CFDBF53C24DB4872E`. O GPG retornou `BADSIG` para os
  dois manifests atuais (assinaturas datadas de 2026-03-25, conteúdo observado
  com releases de 2026-10-13); nenhuma assinatura foi aceita. A documentação
  pública de [`dotnetup`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnetup/concepts/download-verification)
  descreve `.p7s`, raízes X.509 incluídas e revogação online, incompatível com
  o formato `.sig` observado; além de estar em preview, essa diferença exige
  esclarecimento antes de confiar no mecanismo. As discussões oficiais
  [dotnet/sdk #51093](https://github.com/dotnet/sdk/issues/51093) e
  [dotnet/sdk #51104](https://github.com/dotnet/sdk/issues/51104) acompanham
  verificação e assinatura de conteúdo. A chave consultada não declara
  expiração, mas não foi encontrada política operacional verificável de rotação
  ou revogação para as assinaturas OpenPGP atuais.
- **Recomendação técnica:** adiar a integração de assinatura no DotnetScope.
  Manter TLS, allowlist e SHA-512, registrar o comprometimento do host de
  metadata como risco residual e reabrir a implementação somente quando as
  assinaturas dos endpoints usados forem verificáveis, houver contrato oficial
  de formato/chaves/rotação/revogação, o comportamento offline estiver definido
  e uma solução PGP tiver licença exata, manutenção e compatibilidade Native
  AOT verificadas. Nenhuma biblioteca foi selecionada ou adicionada e nenhuma
  confiança de produção foi alterada. A decisão humana de adiar ou descartar
  foi registrada pelo mantenedor em 2026-10-22 como adiamento com risco
  conhecido, sob a condição de não impedir novas versões. A assinatura não será
  gate de release; por não recomendar a implementação agora, nenhuma Issue de
  implementação foi criada.

## Melhorias na origem dos templates

Avaliação read-only do Altivy.Groundwork: o manifesto local atribui os templates
ao componente `core`, catálogo `2026.10.0`. As correções locais preservam os
hashes originais do manifesto; não devem reescrevê-los para esconder adaptações.
As Issues upstream contêm a evidência técnica no repositório proprietário.
**Andamento:** U1–U3 já foram integradas no Groundwork pelo PR #6, merge commit
`be8ab0f8…`; as Issues upstream #3–#5 estão fechadas. Não foram duplicadas
alterações no DotnetScope.

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
