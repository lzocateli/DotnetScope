# Plano de internacionalização, descoberta e presença pública

| Campo | Valor |
| --- | --- |
| Estado | proposto para revisão; execução das fases não autorizada |
| Origem | IA assistida |
| Revisor humano | Lincoln Zocateli; revisão pendente |
| Decisão posterior | arquitetura de manutenção das traduções da seção 3.4 confirmada pelo mantenedor em 2026-10-22; implementação não iniciada |
| Data da avaliação | 2026-10-22 |
| Solicitação | planejar documentação em pt-BR, inglês e espanhol, visibilidade na comunidade .NET e melhorias para descoberta no Google e Bing |
| Repositório | <https://github.com/lzocateli/DotnetScope> |
| Relação com o produto | evolução documental e de presença pública após F9; sem alterar aprovações históricas |

## 1. Objetivo, autorização e limites

Preservar a documentação original em português brasileiro e ampliar o acesso ao
DotnetScope por usuários internacionais, com traduções sustentáveis, apresentação
clara do produto e um site de documentação rastreável por mecanismos de busca.

A solicitação autoriza a criação e atualização deste plano, não sua execução. Não autoriza
traduções, alteração de políticas, configuração remota, abertura de Issues ou PRs,
commit, push, publicação de site, submissão a buscadores ou lançamento de versão.
Cada fase segue Issue → escopo revisado → autorização do mantenedor → branch →
PR, conforme [CONTRIBUTING](../.github/CONTRIBUTING.md).

Em 2026-10-22, após a avaliação das alternativas de automação, o mantenedor
solicitou: “Inclua suas recomendações no plano, sera feito exatamente como você
sugeriu”. Essa confirmação estabelece a arquitetura descrita na seção 3.4;
não constitui autorização para criar ou executar a skill, os scripts, os hooks
ou os workflows neste momento.

O [plano de implementação](plano-implementacao.md) continua sendo a fonte das
fases do produto. As [remediações de governança](plano-remediacao-governanca.md)
não são substituídas por este plano. Tradução e divulgação não comprovam
segurança, suporte ou conclusão das remediações.

### Resultados esperados

- Português brasileiro preservado como idioma de autoria e referência.
- Jornadas essenciais acessíveis em inglês e espanhol.
- Traduções com revisão, versão de referência e detecção de desatualização.
- Repositório com descrição, tópicos e apresentação úteis para descoberta.
- Site estático com páginas por idioma, conteúdo HTML acessível, URLs estáveis,
  links internos, metadados e sitemap consistentes.
- Google e Bing capazes de descobrir e processar as páginas públicas, com
  evidências de submissão e acompanhamento sob contas do mantenedor.
- Divulgação baseada em utilidade demonstrável, sem alegar endosso da Microsoft.

### Não objetivos

- Converter os documentos originais para inglês ou substituir o README pt-BR.
- Traduzir retrospectivamente todos os planos, ADRs, Issues, PRs ou commits.
- Reescrever o produto, alterar contratos da CLI ou criar funções apenas para SEO.
- Prometer posição, prazo de indexação, quantidade de estrelas ou atenção da Microsoft.
- Comprar links, avaliações, estrelas, anúncios ou serviços de tradução.
- Adicionar analytics com rastreamento, CMS, banco de dados ou backend ao site.
- Transferir o projeto à .NET Foundation ou publicar no NuGet sem decisão própria.

## 2. Diagnóstico e hipóteses

### Evidências verificadas em 2026-10-22

| Superfície | Situação observada | Consequência |
| --- | --- | --- |
| Produto | CLI Native AOT, pré-1.0; release v0.4.0 indicada no README | comunicar maturidade e limitações sem apresentar o produto como 1.0 |
| Plataformas | Linux, Windows e macOS; escopos user e global documentados | material internacional deve preservar diferenças entre SOs e estratégias |
| Distribuição | assets GitHub, instaladores, Homebrew, `.deb` e fluxo winget documentados | aproveitar os canais existentes antes de acrescentar outros |
| Confiança | checksums, SBOM e atestações documentados | explicar verificação sem confundir integridade com garantia absoluta de segurança |
| Conteúdo | README, referência da CLI e documentos de contribuição em pt-BR | barreira de compreensão para usuários internacionais |
| CLI | ajuda e diversas mensagens em pt-BR; comandos e chaves JSON em inglês | documentação traduzida não equivale a produto integralmente localizado |
| Política | instruções globais e documentais exigem pt-BR | traduções precisam de exceção explícita e revisada |
| GitHub público | descrição e homepage ausentes, tópicos vazios, Pages desativado | falta uma apresentação curta e um endereço de documentação |
| Adoção pública | zero estrelas e forks na consulta; repositório criado em 2026-10-20 | ponto inicial recente, não evidência de falta de demanda |

Fontes locais: [README](../README.md), [CLI](cli.md),
[release](release.md), [instruções globais](../.github/copilot-instructions.md)
e [instruções documentais](../.github/instructions/documentation.instructions.md).
Metadados remotos são um retrato da data, não um estado permanente.

### Hipóteses e formas de refutação

| Hipótese | Validação |
| --- | --- |
| apresentação curta e primeiros passos em inglês reduzem a barreira de entrada | usuário que não lê português entende a finalidade, instala e executa o primeiro comando sem orientação privada |
| um conjunto essencial nos três idiomas é sustentável | uma mudança real de documentação percorre atualização, revisão e publicação das três versões sem divergência funcional |
| site estático melhora o controle técnico de descoberta | HTML entregue contém conteúdo, links, canonical e alternates corretos; inspeções dos buscadores conseguem processá-lo |
| a proposta desperta interesse por resolver uma necessidade real | relatos de uso, perguntas e contribuições descrevem casos concretos; não apenas crescimento de estrelas |

Ausência de indexação logo após publicação não refuta isoladamente a terceira
hipótese: analisar descoberta, bloqueios, processamento e qualidade separadamente.

## 3. Modelo editorial e idiomas

### 3.1 Fonte de referência e organização

Proposta a aprovar em I0:

- Manter `README.md` e os documentos atuais em pt-BR nos caminhos existentes.
- Acrescentar `README.en.md` e `README.es.md` na raiz.
- Acrescentar traduções dos guias em `docs/en/` e `docs/es/`, com os mesmos
  identificadores lógicos de página e sem mover os originais.
- Colocar seletor com os nomes Português (Brasil), English e Español no início
  dos READMEs. Não depender de bandeiras nem de seleção automática do GitHub.
- Usar inglês internacional (`en`), sem restringir o público aos EUA, e espanhol
  de alcance geral (`es`). Usar `pt-BR` para a versão brasileira.
- Preservar opções, comandos, nomes de arquivo, chaves JSON, hashes, URLs de
  download e códigos de saída. Traduzir a explicação e os comentários didáticos.
- Resolver conflitos pelo comportamento implementado e pelo documento
  proprietário pt-BR; corrigir a origem antes de propagar traduções.
- Não manter manualmente uma segunda cópia integral dos guias para o site:
  o build deve consumir os Markdown proprietários e mapear seus links.

O site pode ter páginas introdutórias próprias, mas deve reaproveitar os guias
de referência. Conteúdo promocional não pode divergir do contrato da CLI.

### 3.2 Cobertura por prioridade

| Grupo | Conteúdo | Política |
| --- | --- | --- |
| A — entrada | visão, instalação, primeiros passos, plataformas e limitações | obrigatório nos três idiomas |
| B — uso seguro | CLI, escopos, efeitos em PATH/perfis/registro, reversão, coexistência, verificação e solução de problemas | obrigatório antes de anunciar documentação completa em um idioma |
| C — colaboração | contribuição, suporte, segurança e código de conduta | resumos de entrada e traduções revisadas; preservar caminhos especiais do GitHub |
| D — engenharia | release operacional, governança detalhada e decisões | traduzir conforme demanda; resumo e link pt-BR quando necessário |
| E — histórico | planos, remediações, ADRs e relatórios anteriores | permanecer em pt-BR por padrão, sem alterar decisões históricas |

Separar a orientação de verificação para usuários do runbook de publicação do
mantenedor, reutilizando conteúdo quando possível. Não exigir que usuários
internacionais leiam um plano histórico para instalar ou remover o produto.

### 3.3 Atualização e revisão

- Cada tradução registra o documento-fonte, commit de referência e revisão humana.
  O commit permite rastrear a base; não significa que toda mudança exige retradução.
- Manter glossário pequeno para escopo, runtime, SDK, feature band, instalação
  gerenciada, reversão e termos de segurança.
- Registrar cobertura por página e idioma: ausente, em revisão, atual ou
  desatualizada. Não usar apenas a data do arquivo como prova de equivalência.
- Mudanças de comportamento ou avisos críticos atualizam o grupo afetado no mesmo
  PR; sem revisor, adiar a publicação da tradução afetada ou exibir aviso claro
  com link à fonte atual. Não bloquear correção urgente do produto.
- No site, retirar páginas materialmente incorretas da navegação, alternates e
  sitemap até correção; decidir redirecionamento ou retirada conforme o caso.
  Aviso editorial não torna uma instrução perigosa aceitável.
- Revisão assistida por IA é opcional; não substitui validação técnica e linguística.
  Não enviar conteúdo privado ou credenciais a serviços externos.
- Arquivos de política não ganham obrigações ou promessas diferentes por idioma.
  Não traduzir nem modificar o texto jurídico de `LICENSE`.

### 3.4 Manutenção assistida pelo Copilot e validação determinística

Arquitetura confirmada pelo mantenedor: **instruções orientam, skill produz
traduções assistidas e validador verifica regras objetivas no pre-push e no CI**.
Geração por IA não integra o caminho obrigatório de push.

| Componente planejado | Responsabilidade | Limite |
| --- | --- | --- |
| Instruções globais e documentais | preservar pt-BR como referência, delimitar destinos, elementos literais e permissões | orientam o agente; não comprovam cumprimento nem substituem testes |
| Skill de projeto | analisar mudanças, atualizar traduções incrementalmente e apresentar pendências | invocação explícita inicialmente; sem commit, push ou publicação |
| Registro versionado de traduções | mapear fontes, destinos, base de conteúdo e revisão | hash atualizado não comprova equivalência linguística |
| Validador compartilhado | conferir cobertura exigida, hashes, estrutura, links e elementos invariáveis | não usa IA, rede ou credenciais para validar traduções locais |
| Hook Git `pre-push` | executar o validador sobre o conteúdo dos commits enviados | somente leitura; não traduz, edita, cria commits ou inicia outro push |
| Check de CI do PR | repetir as regras independentemente da instalação local | não invoca Copilot nem depende de assinatura de IA do colaborador |

#### Instruções e skill

- Reconciliar a exceção de idioma nas instruções globais e documentais em I0;
  evitar regras contraditórias e duplicação extensa do procedimento.
- Criar em I2 uma skill de workspace em `.github/skills/`, com nome e descrição
  que permitam encontrá-la para tradução, localização e manutenção documental.
  Validar descoberta e invocação na experiência Copilot efetivamente utilizada.
- Manter as regras permanentes nas instruções e o procedimento na skill.
  Não criar agente dedicado ou hooks de ciclo de vida do Copilot sem necessidade.
- O fluxo da skill identifica fontes e destinos, consulta glossário e base
  anterior, compara o pt-BR antigo com o atual e integra apenas as mudanças
  pertinentes, preservando melhorias manuais nas traduções.
- Se a base anterior não estiver disponível, sinalizar a limitação e comparar
  integralmente fonte e tradução; nunca presumir que a tradução está atual.
- Validar comandos, opções, chaves JSON, códigos de saída, links e marcadores
  literais. A skill não altera produto, fonte pt-BR ou documentos fora do escopo
  para contornar falhas de validação.
- Ao concluir, apresentar arquivos alterados, validações, dúvidas linguísticas e
  necessidade de revisão. IA não atribui a si revisão humana nem declara
  equivalência apenas porque o validador passou.
- O carregamento automático da skill não é um gate confiável: sua execução
  inicial será solicitada explicitamente antes da revisão e do commit.

#### Registro de tradução e política de bloqueio

- Definir em I0 um registro versionado pequeno, com documento-fonte pt-BR,
  destinos por idioma, hash do conteúdo-fonte usado, estado e referência da
  revisão humana, quando existente.
- Manter também o commit de referência previsto na seção 3.3 para auditoria.
  O hash do conteúdo é a base objetiva da detecção e evita depender exclusivamente
  de IDs de commit que podem mudar após squash ou rebase.
- Especificar algoritmo e normalização de encoding/finais de linha, sem ignorar
  alterações relevantes. A localização do registro e seu schema são definidos
  em I0; não espalhar metadados duplicados sem necessidade.
- Atualizar a base pela skill não promove automaticamente o estado para revisado.
  Revisão só é válida para o conteúdo-fonte e a tradução efetivamente examinados;
  mudanças posteriores na tradução também invalidam a revisão anterior.
- Aplicar bloqueios conforme grupos, idiomas lançados e cobertura exigida.
  Documentos históricos excluídos e idiomas ainda não disponibilizados não
  bloqueiam contribuições por ausência de tradução.
- Fonte alterada com destino obrigatório exige atualização e revisão ou
  mitigação editorial explícita, rastreável e aceita pela política da seção 3.3.
  Traduções críticas materialmente incorretas não permanecem publicadas apenas
  com um aviso; correções urgentes do produto não dependem de IA disponível.

#### Pre-push, CI e segurança operacional

- O hook Git `pre-push` é distinto dos hooks de sessão/ferramentas do Copilot.
  Ele executa somente a validação determinística, sem iniciar Copilot CLI,
  modelos remotos, prompts interativos ou geração de arquivos.
- Embora seja tecnicamente possível iniciar Copilot CLI programaticamente em
  um hook, isso adicionaria autenticação, latência, consumo, permissões e
  resultados não determinísticos. Arquivos gerados no diretório de trabalho
  não entram automaticamente nos commits que o push transmite.
- Usar o conteúdo dos objetos Git enviados, não a árvore de trabalho, para
  decidir se o push está consistente. Tratar todas as atualizações recebidas
  pelo hook, branches novas, exclusões, múltiplas referências e referências
  não abrangidas pela política; não assumir apenas `HEAD~1`.
- Definir a base de comparação e o comportamento com histórico insuficiente.
  Quando não for possível validar o recorte, retornar erro acionável ou validar
  integralmente o snapshot aplicável, sem sucesso silencioso.
- Preservar alterações locais e não executar `git add`, commit ou push dentro
  do hook. Falhas listam documentos/idiomas afetados e indicam o fluxo de correção.
- Coexistir com o [pre-commit de Gitleaks](../.githooks/pre-commit), preservando
  sua instalação e finalidade. Documentar instalação por clone, pré-requisitos
  e comportamento em Windows, Linux e macOS.
- Compartilhar o núcleo do validador entre execução manual, hook e CI;
  automações globais pertencem a `tools/scripts/`, com orquestração PowerShell
  e testes em containers ou runners apropriados.
- Hooks locais podem ser contornados; o check de CI deve ser obrigatório no
  fluxo de merge quando a proteção correspondente for autorizada e configurada.
  PRs externos não recebem credenciais nem precisam de acesso ao Copilot.
- Não usar aprovação irrestrita de ferramentas ou permissões de escrita remota
  para contornar interação. A implementação não precisa de execução headless
  de IA; a skill assistida não recebe autorização para commit/push.

Fluxo operacional: alterar pt-BR → invocar skill → revisar traduções → validar →
criar commit explicitamente → pre-push valida commits → CI valida PR → revisão
e publicação autorizadas. Contribuidores também podem traduzir manualmente,
respeitando os mesmos gates; Copilot não é requisito de contribuição.

## 4. Arquitetura de descoberta e SEO

### 4.1 O que cabe ao GitHub

Descrição concisa, tópicos pertinentes, README legível, releases informativas,
links de idioma e links reais de outras comunidades ajudam a descoberta.
Não controlamos o `<head>`, canonical, sitemap, robots ou verificação de
propriedade de `github.com/lzocateli/DotnetScope`. Acrescentar esses arquivos ao
repositório não modifica a configuração SEO do domínio GitHub.

Os READMEs continuam úteis e completos para a primeira experiência. A referência
publicável do site deve ser a mesma dos guias no repositório. Não tentar corrigir
a duplicação entre GitHub e site com canonical fictício dentro do Markdown.

### 4.2 Site recomendado e decisões de hospedagem

Recomendação inicial: site estático no mesmo repositório, publicado em GitHub
Pages, sem framework de aplicação ou servidor próprio. O endereço candidato,
se ativado e validado, é `https://lzocateli.github.io/DotnetScope/`.

Um domínio próprio é opcional e envolve custo, renovação, DNS e responsabilidade
operacional. Decidir antes da primeira divulgação ampla, quando possível.
Não registrar domínio nem contratar serviço neste plano. Se houver migração,
preservar caminhos, configurar redirecionamentos onde houver controle e atualizar
canonical, alternates, sitemap, homepage e ferramentas de busca.

Comparar em I3 geração HTML mínima e geradores estáticos com suporte multilíngue.
Preferir a menor solução mantida que cumpra os critérios. Não aprovar dependência
por popularidade: verificar licença permissiva da versão exata, manutenção,
plugins, tema, fontes e assets; fixar versões e ações de CI. Documentar decisão
e, se necessário, ADR `proposed`, sem marcar aceitação pelo mantenedor.

### 4.3 Requisitos de URLs e páginas

- Usar rotas separadas, por exemplo `<base>/pt-br/`, `<base>/en/` e `<base>/es/`.
  No Pages de projeto, `<base>` inclui `/DotnetScope/`, inclusive nos assets.
- A raiz oferece seleção de idioma acessível e uma descrição breve, sem exigir
  JavaScript ou redirecionar compulsoriamente por idioma/IP.
- Cada página tem conteúdo principal em um idioma, título descritivo, descrição,
  um H1 principal, navegação, hierarquia de headings e link à fonte no GitHub.
- Incluir termos que correspondam a tarefas reais: instalar SDK .NET,
  administrar runtimes, usar `global.json`, configurar PATH e remover versões.
  Localizar naturalmente os títulos, sem repetição artificial de palavras-chave.
- Não alegar troca automática por diretório, isolamento por projeto ou outras
  funcionalidades que não estejam implementadas.
- Definir `lang` correto no HTML para acessibilidade; não tratá-lo como garantia
  de detecção de idioma ou classificação nos buscadores.
- Cada tradução indexável tem canonical absoluto para sua própria URL final;
  não canonicalizar todas as traduções para a página em português ou inglês.
- Gerar `hreflang` em um único mecanismo, preferencialmente o `<head>` HTML:
  referências absolutas, recíprocas e para si própria, apenas entre páginas
  equivalentes publicadas; usar `pt-BR`, `en`, `es` e `x-default` quando aplicável.
- O seletor troca para a página equivalente. Se ela não existe, oferecer
  explicitamente o índice do idioma ou a fonte pt-BR, sem simular tradução.
- Não criar uma página inglesa duplicada em português só para preencher uma rota.
- URLs antigas publicadas exigem mapa de migração; links de fragments traduzidos
  devem ser verificados. Usar anchors estáveis nas novas páginas quando suportado.

### 4.4 Rastreamento e processamento

- Entregar HTML com o conteúdo e links mesmo sem executar JavaScript.
- Servir páginas públicas por HTTPS, sem login e sem bloqueio involuntário de bots.
- Validar status reais: páginas existentes `200`, redirecionamentos intencionais
  e páginas inexistentes `404`, sem transformar todo erro em página de sucesso.
- Gerar sitemap XML com URLs canônicas, publicadas e indexáveis; excluir previews,
  rascunhos, redirects e páginas removidas. `lastmod`, se usado, reflete mudança
  relevante de conteúdo, não toda execução de build.
- Definir robots de acordo com o host/base disponível e anunciar o sitemap onde
  possível. Em Pages de projeto, um arquivo sob `/DotnetScope/robots.txt` não
  substitui o robots da raiz do host. Sitemap pode ser submetido diretamente.
- Não usar `robots.txt` como proteção de informações privadas nem bloquear uma
  página que precise ser rastreada para processar `noindex`.
- Previews não recebem submissão nem entram no sitemap de produção. Preferir
  artefatos privados/localmente validados; `noindex` não é controle de acesso.
- Garantir navegação interna até todo guia público, com links de retorno.
- Evitar arquivos grandes, fontes remotas desnecessárias, imagens sem dimensões,
  scripts de marketing e conteúdo essencial embutido apenas em imagens.
- Adicionar Open Graph e imagem social legível para compartilhamento; isso melhora
  a apresentação de links, não constitui garantia de ranking.
- Avaliar dados estruturados `SoftwareApplication` ou `SoftwareSourceCode` apenas
  se representarem conteúdo visível e verificável. Não inventar avaliações,
  reviews, usuários ou prêmios para obter resultados enriquecidos.

### 4.5 Google, Bing e IndexNow

- Google: verificar propriedade sob controle do mantenedor no Search Console.
  Em Pages compartilhado, avaliar propriedade por prefixo de URL e arquivo HTML
  ou meta tag; propriedade por domínio requer controle DNS.
- Bing: verificar o prefixo/site no Webmaster Tools pelo método disponível,
  inclusive eventual importação da propriedade Google, se suportada.
- Submeter o sitemap de produção em ambos e inspecionar uma página de entrada
  e uma página interna de cada idioma.
- Não tentar verificar a propriedade de `github.com` nem submeter URLs do
  repositório como se o mantenedor controlasse o host.
- IndexNow é complementar e opcional para participantes como Bing. Se adotado,
  hospedar o arquivo de verificação no prefixo controlado e usar `keyLocation`
  compatível com esse escopo; notificar apenas URLs próprias alteradas/removidas.
- Não tratar IndexNow como canal de submissão ao Google. Não usar a Google
  Indexing API para documentação comum.
- Resposta HTTP de submissão confirma recebimento, não indexação. Sitemap,
  dados estruturados e inspeções também não garantem ranking ou inclusão.
- Contas e verificações pertencem ao mantenedor; tokens de APIs e credenciais
  ficam fora do repositório e só são usados se a automação for autorizada.

## 5. Fases executáveis propostas

Os IDs I0 a I9 identificam este plano, não renumeram F0 a F9 do produto.
Todos os itens estão pendentes. Cada fase pode originar mais de uma Issue
coesa; criá-las exige solicitação própria.

### I0 — Aprovar a política e estabelecer a linha de base

- **Prioridade:** P0; **dependências:** nenhuma.
- **Entregas:** inventário de páginas, grupos A–E, proprietário de cada conteúdo,
  glossário inicial, matriz de cobertura e decisões editoriais da seção 3.
- Submeter exceção explícita para traduções nas instruções globais, documentais
  e demais personalizações aplicáveis. Manter documentos internos e mensagens
  atuais pt-BR, salvo autorização específica posterior.
- Incorporar a arquitetura confirmada na seção 3.4: definir registro/schema,
  hash e normalização, estados de revisão e política de bloqueio por cobertura,
  distinguindo decisão arquitetural de autorização para implementar.
- Identificar revisores técnicos e linguísticos; sem revisor de espanhol,
  registrar bloqueio em vez de declarar tradução validada.
- Registrar baseline de metadados, releases, tráfego disponível e acesso às
  contas, sem copiar dados pessoais ou credenciais.
- **Aceite:** mantenedor confirma escopo, política e revisores; documentos-fonte
  e diferenças entre tradução documental e localização da CLI estão explícitos.

### I1 — Melhorar a descoberta imediata no GitHub

- **Prioridade:** P1; **dependências:** I0.
- **Entregas:** proposta de descrição curta em inglês, tópicos, apresentação
  inicial do README pt-BR e imagem social acessível.
- Candidatos a tópicos: `dotnet`, `dotnet-sdk`, `dotnet-runtime`, `cli`,
  `native-aot`, `cross-platform` e `sdk-manager`; selecionar apenas os pertinentes.
  Não usar `dotnet-tool` como alegação de pacote NuGet ainda inexistente.
- Explicar rapidamente público, problema resolvido, matriz suportada e estado
  pré-1.0. Separar instalar o DotnetScope de instalar um SDK usando o DotnetScope.
- Acrescentar demonstração curta com versão, plataforma e comportamento reais,
  sem caminhos pessoais ou material sensível; oferecer equivalente textual.
- Revisar links de instalação e o estado efetivo dos pacotes. Não apresentar
  submissão ao winget como versão já aprovada; diferenciar tap próprio de
  Homebrew core e `.deb` avulso de repositório APT.
- **Aceite:** metadados aprovados e, se autorizada a aplicação remota, conferidos
  pela API; instruções e links correspondem a canais existentes. Homepage só
  aponta ao site depois de ele estar publicado e validado.

### I2 — Publicar o núcleo de entrada em inglês

- **Prioridade:** P1; **dependências:** I0 e I1.
- **Entregas:** `README.en.md`, links entre idiomas disponíveis e jornada do grupo A.
- Criar a skill mínima da seção 3.4, com invocação explícita, glossário e
  atualização incremental do registro de tradução. Exercitar atualização de
  uma fonte sem sobrescrever melhorias manuais do destino.
- Preservar `README.md` em português como documento principal. Não trocar o
  idioma padrão do repositório para atender divulgação.
- Explicar que a CLI ainda tem mensagens pt-BR; exemplos de saída reais não
  podem ser traduzidos como se o binário já os emitisse em inglês.
- **Aceite:** revisão técnica e linguística; leitor de inglês conclui a jornada
  inicial; links e comandos equivalentes aos originais; sem promessa de
  documentação integral enquanto o grupo B estiver incompleto.

### I3 — Preparar o site estático e o pipeline de qualidade

- **Prioridade:** P1; **dependências:** I0 e I2.
- **Entregas:** decisão de gerador/hospedagem/base URL, estrutura de idiomas,
  templates SEO, build reproduzível e verificações documentais.
- Implementar o validador compartilhado da seção 3.4, o hook `pre-push`, sua
  instalação por clone e o check de CI, sem geração por IA. Solicitar autorização
  específica para configurar o check obrigatório na proteção de merge.
- Consumir guias proprietários sem segunda cópia manual. Manter o site fora da
  saída de build do produto; não versionar HTML gerado sem justificativa.
- Validar build, links, anchors, rotas com subdiretório, navegação por teclado,
  telas estreitas, contraste e conteúdo sem JavaScript.
- Build e testes em containers ou CI. Preview local, quando necessário, em
  container servido por borda nginx containerizada, nunca servidor de
  desenvolvimento exposto diretamente ao navegador.
- Separar PR de build e deploy. Forks não publicam nem recebem credenciais;
  publicação usa permissões mínimas e gate de autorização do mantenedor.
- **Aceite:** artefato HTML validado, testes negativos de URLs/links exercitados,
  licenças verificadas, nenhuma publicação remota antecipada; hook e CI detectam
  pendências nos commits corretos sem editar a árvore de trabalho.

### I4 — Completar guias de uso e colaboração em inglês

- **Prioridade:** P1; **dependências:** I2 e I3.
- **Entregas:** grupos B e C em inglês, páginas de tarefas e navegação do site.
- Cobrir instalação por SO, `global.json`, seleção de versão, ambientes
  persistentes, uso offline, desinstalação, coexistência e `doctor`, sem
  acrescentar semântica que o produto não implementa.
- Oferecer comparação factual com `dotnet-install` e mecanismos oficiais,
  incluindo diferenças, casos em que o DotnetScope não é necessário e links às
  fontes. Não se apresentar como instalador oficial ou sucessor da Microsoft.
- Manter arquivos especiais de comunidade do GitHub nos caminhos reconhecidos,
  com links às traduções; traduzir formulários/templates somente após avaliar
  manutenção, acessibilidade e fluxo de triagem.
- **Aceite:** todas as páginas A–C declaradas disponíveis têm revisão e
  equivalência; avisos de privilégio, ambiente, remoção e suporte preservados.

### I5 — Disponibilizar a jornada em espanhol

- **Prioridade:** P2; **dependências:** I4.
- **Entregas:** `README.es.md`, grupos A–C em espanhol e integração ao site.
- Traduzir a partir da fonte pt-BR, usando o glossário; inglês não se torna
  fonte intermediária obrigatória.
- Expandir a mesma skill, registro e validador para espanhol, sem criar fluxo
  paralelo nem exigir Copilot de quem contribui manualmente.
- **Aceite:** revisor linguístico identificado, experiência inicial validada e
  cobertura publicada explicitamente. Alternates só apontam a páginas existentes.

### I6 — Publicar o site e habilitar a descoberta nos buscadores

- **Prioridade:** P1 para lançamento completo; **dependências:** I3, I4 e I5.
- **Entregas:** deploy autorizado, URL estável, homepage do GitHub, sitemap,
  verificações de propriedade e registros de submissão Google/Bing.
- Para antecipar um lançamento pt-BR/en, o mantenedor pode aprovar um recorte
  de I6 após I4; I5 continua pendente e `es` não entra nos alternates/sitemap
  antes da publicação real. Registrar esse recorte na Issue.
- Validar páginas e assets pela URL pública, inclusive canonical absoluto,
  hreflang recíproco, `lang`, sitemap, indexabilidade e `404`.
- Registrar nas ferramentas dos buscadores as URLs inspecionadas, data e estado;
  distinguir submissão, rastreamento, indexação e exibição.
- Avaliar IndexNow apenas depois do site funcionar, com escopo de URL validado.
- **Aceite:** páginas A–C do recorte acessíveis, site sem bloqueios acidentais,
  propriedades verificadas e submissões registradas. Indexação observada é
  acompanhamento posterior, não condição com prazo garantido.

### I7 — Divulgar com evidências e acompanhar resultados

- **Prioridade:** P2; **dependências:** I1, I2 e I6.
- **Entregas:** apresentação curta, demonstração, guia por caso de uso e proposta
  de divulgação em comunidades .NET em português, inglês e, após I5, espanhol.
- Começar por relatos concretos de problemas e soluções: ambiente sem .NET,
  SDK de `global.json`, diferenças de escopo e reversão do ambiente.
- Buscar newsletters, grupos, eventos e listas mantidas; verificar regras e
  atividade antes de submeter. Não abrir PRs promocionais em repositórios oficiais
  de produto nem assumir que uma lista antiga ainda aceita projetos.
- Avaliar participação em eventos .NET, artigos e contato com mantenedores/DevRel
  por canais adequados. Não exigir exposição nem associar identidade Microsoft ao produto.
- Melhorar notas de releases futuras com resumo em inglês e links de idioma,
  mantendo explicações em pt-BR. Não reescrever release histórica sem decisão.
- **Aceite:** cada divulgação tem audiência e caso de uso pertinentes; links
  funcionam; não há spam, métricas fabricadas ou alegação de parceria.
- Candidatura à .NET Foundation fica como avaliação posterior, com critérios,
  governança, CLA e implicações revisados pelo mantenedor; não equivale a endosso
  da Microsoft e não é requisito para o site.

### I8 — Avaliar localização da CLI e distribuição NuGet

- **Prioridade:** P3; **dependências:** I4 e feedback de I7.
- **Natureza:** duas avaliações independentes; implementar apenas por Issues
  específicas autorizadas, sem bloquear documentação ou SEO.
- **CLI:** inventariar ajuda, prompts, mensagens e diagnósticos em `Cli`, `Core`,
  `Infrastructure` e instaladores. Propor seleção e precedência de idioma,
  fallback explícito, preservação do pt-BR e política de padrão antes de editar.
  Não presumir suporte cultural completo apenas por trocar strings.
- Verificar interação com Native AOT/globalização, tamanho, dependências,
  formatos de números/datas e testes de cada cultura. Comandos, chaves JSON,
  marcadores de perfis e códigos de saída não são traduzidos.
- **NuGet:** avaliar pacote Native AOT por RID suportado pelo SDK .NET 10,
  disponibilidade do nome, manutenção, licença, cadeia de build, atestações e
  requisitos do cliente de instalação. O projeto atual não declara `PackAsTool`.
- Não substituir os binários independentes: `dotnet tool install` exige um
  cliente .NET para instalar e não resolve sozinho o bootstrap de máquina limpa.
- **Aceite:** decisão humana separada para cada avaliação, custo e validação
  definidos; nenhuma alteração de idioma padrão ou publicação implícita.

### I9 — Operar traduções, SEO e comunidade continuamente

- **Prioridade:** P2; **dependências:** I6; usar I7 para medir divulgação.
- **Entregas:** checklist de release documental e revisão periódica com responsável.
- Operar o fluxo assistido e os gates da seção 3.4; manter instruções, skill,
  glossário e registro coerentes com mudanças reais, sem autoaprovação humana.
- Automatizar checagem de links locais, anchors, paridade dos exemplos, cobertura,
  páginas órfãs, sitemap, canonical e alternates; a automação sinaliza
  desatualização, não aprova equivalência linguística.
- Validar links externos com limites e tratamento explícito de falhas
  transitórias; não instalar ferramenta nova sem necessidade e licença verificada.
- Rever após alterações de CLI e releases; avaliar cobertura/SEO mensalmente no
  início e ajustar a cadência à capacidade real do mantenedor.
- **Aceite:** mudança de conteúdo percorre o ciclo completo; há responsável por
  traduções pendentes, links quebrados e erros observados nos buscadores.

## 6. Dependências, esforço e priorização

Fluxo principal: I0 → I1 → I2 → I3 → I4 → I5 → I6 → I7.
I9 começa após I6. I8 depende de I4 e de feedback de I7, mas é opcional.
O recorte antecipado de I6 exige aprovação explícita conforme sua descrição.

Estimativas preliminares de esforço ativo, não prazo contratual:

| Recorte | Estimativa | Principal incerteza |
| --- | --- | --- |
| I0–I2 | 3–5 dias úteis | política/registro, skill inicial e revisão em inglês |
| I3 | 3–7 dias úteis | gerador, links e testes do validador/pre-push/CI |
| I4 | 3–6 dias úteis | extensão da CLI e material de colaboração |
| I5 | 3–6 dias úteis | revisão técnica e linguística em espanhol |
| I6 | 1–3 dias úteis | hospedagem, verificações e acesso às contas |
| I7 e I9 | recorrente, capacidade a definir | feedback, revisões e mudanças do produto |
| I8 | estimar após avaliação | volume de mensagens e modelo de empacotamento |

As estimativas não incluem espera de revisores, aprovação de serviços, eventos,
moderação de pacotes, rastreamento ou indexação. Reestimar após o inventário I0.
Priorizar I1–I2 quando houver pouca capacidade; não criar um site grande antes
de ter conteúdo revisado suficiente.

## 7. Validação e critérios de conclusão

### Gates técnicos e editoriais

1. **Preservação:** originais pt-BR e links históricos funcionam; nenhuma alteração
   do produto ou de aprovações históricas acompanha PR apenas documental.
2. **Equivalência:** avisos, padrões, requisitos e exemplos coerentes com a versão
   implementada; tradução não oculta limitações da plataforma.
3. **Execução:** comandos de instalação/remoção testados em containers descartáveis
   ou runners efêmeros por plataforma; nunca no perfil real do desenvolvedor.
   Reutilizar evidências existentes válidas; marcar o que não foi executado.
4. **Build do site:** geração determinística quanto ao conteúdo, sem links locais
   ou fragments inválidos, sem assets quebrados e sem dependência de JavaScript
   para acessar os guias.
5. **Acessibilidade e apresentação:** navegação por teclado, foco, contraste,
   layout estreito, nomes de idiomas e alternativas textuais revisados.
6. **SEO técnico:** testar URLs finais e casos negativos: página inexistente,
   idioma ausente, tradução retirada, base de projeto e mudança de URL.
7. **Publicação:** CI sem segredos em PRs externos, deploy autorizado e rollback
   documentado para o artefato anterior; não exigir nova release da CLI.
8. **Ferramentas de busca:** sitemap processável, propriedade sob controle do
   mantenedor e inspeções registradas. Não registrar submissão como indexação.
9. **Automação de tradução:** testar fonte alterada, destino ausente, hash
   divergente, edição posterior à revisão, idioma não lançado, documento
   excluído da cobertura e mitigação editorial válida/inválida. O validador
   não declara correção linguística nem aceita mera mudança de hash como revisão.
10. **Integração Git/CI:** testar árvore de trabalho diferente do commit enviado,
    branch nova, exclusão, múltiplas referências, histórico insuficiente e falha
    do runner; garantir execução sem IA, rede ou credenciais na validação local
    de tradução, preservação do Gitleaks e ausência de alterações no worktree.

Verificações automatizadas futuras devem falhar de forma explícita para conteúdo
inconsistente. Revisão de texto não exige executar toda a suíte do produto;
alterações da CLI em I8 exigem os gates próprios de build, testes e Native AOT.

### Indicadores e acompanhamento

| Indicador | Meta de qualidade proposta |
| --- | --- |
| Cobertura A–C declarada disponível | 100% das páginas declaradas com fonte e revisão identificadas |
| Links internos e exemplos estruturais | nenhum erro conhecido no recorte publicado |
| Páginas canônicas públicas | todas acessíveis, sem `noindex` acidental e presentes no sitemap quando indexáveis |
| Alternates | apenas equivalentes reais, referências recíprocas e sem destinos quebrados |
| Primeiro uso | ao menos uma jornada documentada por idioma, validada por leitor/revisor adequado |
| Saúde de tradução | nenhuma instrução crítica conhecida desatualizada publicada sem mitigação |
| Busca | acompanhar descoberta, indexação, impressões, cliques e consultas por página/idioma |
| Comunidade | acompanhar relatos úteis, contribuidores externos e problemas de onboarding resolvidos |

Rever resultados aproximadamente 30, 60 e 90 dias após publicação; são janelas
de análise, não compromissos dos buscadores. Separar buscas pelo nome DotnetScope
de consultas genéricas sobre SDK/runtime. Baixo volume inicial não autoriza
conclusões estatísticas sobre um idioma nem justifica retirar o pt-BR.

Google Search Console e Bing Webmaster Tools são suficientes para a medição
inicial sem inserir rastreadores no site. Tráfego e downloads GitHub servem como
sinais complementares, não contagem de usuários únicos ou instalações bem-sucedidas.

## 8. Riscos, custos e decisões pendentes

| Risco | Mitigação |
| --- | --- |
| triplicar manutenção sem revisores | priorizar A–C, detectar atraso e lançar por recortes explícitos |
| erros de tradução em privilégios ou remoção | revisão técnica obrigatória e exemplos reais por plataforma |
| duplicação entre site e repositório | guias proprietários únicos consumidos pelo build |
| SEO incorreto em Pages de projeto | testar base path, prefixo de propriedade, sitemap e alcance de robots/IndexNow |
| troca prematura de domínio | decidir cedo; documentar migração e limites de redirecionamento |
| divulgação antes de tratar limitações | estado pré-1.0 e pendências de governança visíveis |
| expectativa de reconhecimento oficial | distinguir descoberta, adoção, distribuição, Fundação e endosso |
| aumento de dependências e custo | site estático mínimo; licença/versionamento verificados; domínio opcional |
| deriva de release e suporte nas traduções | checklist documental por mudança e link à política vigente |
| tradução automática no push modifica arquivos não enviados | proibir geração no hook; produzir e revisar antes do commit |
| hash atualizado é confundido com tradução aprovada | separar base de conteúdo, estado e evidência de revisão humana |
| hook ausente ou contornado | instalação documentada e check de CI obrigatório após autorização |

Decisão já confirmada em 2026-10-22: usar instruções, skill assistida e validador
determinístico compartilhado por pre-push e CI, sem IA no push, conforme seção 3.4.
Essa decisão não aprova automaticamente o restante do plano nem inicia fases.

Decisões humanas ainda necessárias antes da execução:

1. Aprovar exceção editorial para traduções e manter pt-BR como referência.
2. Confirmar revisores e política para contribuições em inglês/espanhol.
3. Aprovar estrutura de arquivos, gerador, hospedagem e eventual domínio.
4. Escolher lançamento completo ou recorte pt-BR/en antes do espanhol.
5. Autorizar separadamente metadados remotos, deploy e submissões aos buscadores.
6. Definir capacidade recorrente de revisão e triagem internacional.
7. Decidir I8 e eventual avaliação da .NET Foundation somente após seus estudos.

Nenhuma das decisões pendentes é presumida pela existência deste plano.

## 9. Referências verificadas

Consultadas em 2026-10-22; revalidar instruções dos serviços antes de executar.

- [GitHub — tópicos e descoberta de repositórios](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/classifying-your-repository-with-topics).
- [GitHub — hospedagem estática com Pages](https://docs.github.com/en/pages/getting-started-with-github-pages/what-is-github-pages).
- [VS Code — Agent Skills e diferenças para instruções](https://code.visualstudio.com/docs/agent-customization/agent-skills).
- [GitHub — modos interativo/programático e permissões do Copilot CLI](https://docs.github.com/en/copilot/concepts/copilot-surfaces/copilot-cli).
- [Git — contrato dos hooks, incluindo pre-push](https://git-scm.com/docs/githooks).
- [Google — versões localizadas e hreflang](https://developers.google.com/search/docs/specialty/international/localized-versions).
- [Google — sitemaps e suas limitações](https://developers.google.com/search/docs/crawling-indexing/sitemaps/overview).
- [Google — dados estruturados para aplicações](https://developers.google.com/search/docs/appearance/structured-data/software-app).
- [Google — verificação de propriedade no Search Console](https://support.google.com/webmasters/answer/9008080).
- [Bing — Webmaster Tools](https://www.bing.com/webmasters/).
- [IndexNow — protocolo, arquivo de verificação e alcance de keyLocation](https://www.indexnow.org/documentation).
- [Microsoft — finalidade e comportamento de dotnet-install](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script).
- [Microsoft — ferramentas .NET Native AOT e pacotes por RID](https://learn.microsoft.com/en-us/dotnet/core/tools/rid-specific-tools).
- [Microsoft — orientações de design de CLI](https://learn.microsoft.com/en-us/dotnet/standard/commandline/design-guidance).
- [.NET Foundation — candidatura e implicações de participação](https://dotnetfoundation.org/projects/submit-a-project).

## 10. Estado deste plano

Somente o documento de planejamento foi criado e atualizado. A arquitetura da
seção 3.4 foi confirmada pelo mantenedor; a revisão do restante permanece pendente.
Nenhuma skill, instrução, script, hook ou workflow foi implementado, e nenhuma
tradução, alteração remota, configuração de buscadores, publicação ou mudança de
comportamento foi executada. Fases I0–I9 aguardam revisão de escopo e autorização
específica para execução.
