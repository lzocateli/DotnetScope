# Modelo de ameaças — F7 (escopo global)

| Campo | Valor |
| --- | --- |
| Status | aprovado pelo revisor humano em 2026-10-21, com a condição de não acrescentar custo financeiro nem complexidade ao processo |
| Origem | IA assistida |
| Revisor humano | Lincoln Zocateli |
| Fase | F7 do [plano de implementação](plano-implementacao.md#15-fases) |
| Método | STRIDE por fronteira de confiança |
| Data | 2026-10-21 |

Este documento cumpre a exigência da [seção 14 do plano](plano-implementacao.md#14-segurança):
"threat model antes da F7". Ele descreve o que a F7 vai construir, o que pode
dar errado quando o DotnetScope roda com privilégio de administrador e quais
mitigações e testes viram requisitos da fase. Nada aqui está implementado; as
[decisões](#7-decisões) foram aprovadas pelo revisor humano em 2026-10-21.

## 1. Escopo

A F7 adiciona o escopo `global` (plano, [seções 4.2 e 5](plano-implementacao.md#5-estratégias-por-escopo)):

| SO | `--source auto` | Alternativas | Ambiente persistente |
| --- | --- | --- | --- |
| Linux | `apt` quando o canal/versão existe nas fontes configuradas | `archive` em `/usr/local/share/dotnet` | `/etc/profile.d/dotnetscope.sh` e `/etc/dotnet/install_location_<arch>` |
| Windows | instalador oficial `.exe` (`/install /quiet /norestart`) | `archive` em `%ProgramFiles%\dotnet`, se o destino não for gerenciado por instalador | `HKLM\...\Session Manager\Environment` e `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation` |
| macOS | `.pkg` oficial (`installer -pkg`) | `archive` em `/usr/local/share/dotnet`, se o destino não for gerenciado por `.pkg` | `/etc/paths.d/dotnetscope` |

Também entram `--install-deps` (dependências nativas via gerenciador de
pacotes, só Linux) e `uninstall`, `list --installed`, `env` e `doctor` com
`--scope global`.

Fora do escopo deste modelo: o escopo `user` (já entregue e coberto pelas
regras de segurança atuais), a cadeia de release do próprio DotnetScope
(coberta pela [F5](release.md) e pela F9) e o comprometimento da infraestrutura
da Microsoft que assina e publica o .NET (risco residual aceito, ver
[seção 8](#8-riscos-residuais)).

### 1.1 Premissas

- A elevação já existe quando a CLI começa: `sudo` ou root no Linux e macOS,
  terminal elevado (UAC) no Windows. A CLI nunca se autoeleva.
- Quem consegue elevar é, por definição, administrador da máquina. O atacante
  relevante **não** é esse administrador, e sim alguém que tenta influenciar o
  processo elevado sem ter o privilégio.
- O metadata oficial (`releases.json`) traz SHA-512 para `.tar.gz`, `.zip`,
  `.exe` e `.pkg`, todos em `builds.dotnet.microsoft.com` (verificado em
  2026-10-21 no canal 10.0).
- O host .NET (`dotnet`, apphosts) localiza o runtime global por
  `install_location` (Linux e macOS) e por `InstalledVersions\<arch>\InstallLocation`
  na visão de 32 bits do registro (Windows). Quem controla esses valores
  controla qual runtime todos os apphosts da máquina carregam.

## 2. Ativos

| Ativo | Por que importa |
| --- | --- |
| A1. Execução como root/administrador | Qualquer desvio vira execução de código com controle total da máquina. |
| A2. Raiz global (`/usr/local/share/dotnet`, `%ProgramFiles%\dotnet`) | Executada por todos os usuários e serviços; um binário trocado afeta todos. |
| A3. Arquivos e chaves de sistema (`/etc/profile.d`, `/etc/paths.d`, `/etc/dotnet/install_location*`, `HKLM`) | Definem o `PATH` e o runtime de todos os usuários. |
| A4. Instalações gerenciadas por pacote (`apt`/`rpm`, MSI, `.pkg`) | Corrompê-las quebra atualizações de segurança do sistema. |
| A5. Estado global (manifesto, lock, journal, backups, cache de metadata) | Decide o que é instalado e removido como root. |
| A6. Integridade dos artefatos baixados | Base de toda a confiança. |
| A7. Disponibilidade do .NET da máquina | Builds e serviços dependem dele. |

## 3. Atores e fronteiras de confiança

| Ator | Capacidade assumida |
| --- | --- |
| AT1. Malware ou processo sem privilégio do mesmo usuário que eleva | Edita perfis, `~/.config`, `~/.cache`, `%TEMP%`, variáveis exportadas para o `sudo -E`; vê quando o processo elevado roda. |
| AT2. Outro usuário local sem privilégio | Cria arquivos em diretórios graváveis por todos (`/tmp`, `/usr/local` mal configurado, `C:\` em algumas máquinas). |
| AT3. Atacante de rede | Intercepta ou altera tráfego; pode operar um proxy. Não tem CA confiável pelo sistema. |
| AT4. Fonte de pacotes não oficial | Repositório `apt` de terceiro configurado na máquina. |
| AT5. Administrador legítimo | Confiável; erros dele (comando errado, interrupção) são tratados como falha operacional, não ataque. |

```mermaid
flowchart LR
    subgraph U["Contexto sem privilégio (AT1, AT2)"]
        user["Usuário / malware no usuário"]
        uenv["Ambiente herdado<br/>HOME, XDG_*, SSL_CERT_*, PATH, TEMP"]
        ucache["~/.cache/dotnetscope<br/>%LOCALAPPDATA%\\dotnetscope"]
    end

    subgraph P["Processo elevado (root / administrador)"]
        cli["dotnetscope --scope global"]
        stage["Staging privado"]
    end

    subgraph N["Rede (AT3)"]
        meta["builds.dotnet.microsoft.com<br/>metadata e artefatos"]
        repo["Fontes apt configuradas (AT4)"]
    end

    subgraph S["Sistema (A2, A3, A4, A5)"]
        root["Raiz global<br/>/usr/local/share/dotnet<br/>%ProgramFiles%\\dotnet"]
        sysenv["/etc/profile.d, /etc/paths.d<br/>/etc/dotnet/install_location_arch<br/>HKLM Environment e InstalledVersions"]
        state["Estado global<br/>/var/lib, /var/cache<br/>%ProgramData%\\dotnetscope"]
        pkg["Gerenciadores<br/>apt-get, installer.exe, installer -pkg"]
    end

    user -- "sudo / UAC (TB1)" --> cli
    uenv -. "TB2: herança de ambiente" .-> cli
    ucache -. "TB3: arquivos do usuário" .-> cli
    cli -- "TB4: HTTPS + allowlist" --> meta
    cli --> stage
    stage -- "TB5: verificar e usar" --> root
    cli -- "TB6: processo externo" --> pkg
    pkg --> repo
    cli --> sysenv
    cli --> state
    root -. "executado por todos" .-> user
```

Fronteiras numeradas na tabela de ameaças:

| Fronteira | Travessia |
| --- | --- |
| TB1 | Usuário comum → processo elevado (linha de comando). |
| TB2 | Variáveis de ambiente do usuário → processo elevado (`sudo -E`, `env_keep`, token elevado do mesmo usuário no Windows). |
| TB3 | Arquivos do usuário (cache, estado, `%TEMP%`) lidos ou usados pelo processo elevado. |
| TB4 | Rede → processo elevado (metadata e artefatos). |
| TB5 | Staging → raiz global e arquivos de sistema (verificação → uso). |
| TB6 | Processo elevado → executáveis externos (`apt-get`, instaladores oficiais). |

## 4. Fluxos

### 4.1 Decisão de estratégia e pré-condições

```mermaid
flowchart TD
    start(["dotnetscope install ... --scope global"]) --> elev{"Processo elevado?<br/>euid 0 / token elevado"}
    elev -- "não" --> e7["Código 7, nada alterado<br/>sem sudo nem UAC automáticos"]
    elev -- "sim" --> envsan["Ambiente saneado<br/>caminhos fixos do sistema, ignora XDG_* e HOME"]
    envsan --> resolve["Resolve versão com metadata<br/>cache global do sistema"]
    resolve --> src{"--source"}
    src -- "auto" --> os{"SO"}
    os -- "Linux" --> aptq{"apt tem o canal/versão<br/>nas fontes configuradas?"}
    aptq -- "sim" --> apt["Estratégia apt"]
    aptq -- "não" --> arch["Estratégia archive"]
    os -- "Windows" --> exe["Instalador .exe oficial"]
    os -- "macOS" --> pkgs["Instalador .pkg oficial"]
    src -- "archive" --> arch
    src -- "apt / installer / pkg" --> explicit["Estratégia pedida"]
    arch --> owned{"Destino e ancestrais<br/>pertencem ao root/Administradores<br/>sem escrita para outros?"}
    owned -- "não" --> e7b["Código 7"]
    owned -- "sim" --> mixed{"Destino ou PATH já gerenciado<br/>por pacote, MSI ou .pkg?"}
    mixed -- "sim" --> e6["Código 6 com orientação<br/>--source apt, installer ou pkg"]
    mixed -- "não" --> conflict{"Conflito em arquivos/chaves<br/>de ambiente do sistema?"}
    conflict -- "sim, fail" --> e6b["Código 6, nada alterado"]
    conflict -- "não ou override" --> go(["Executa a estratégia"])
    apt --> checks["Verificações da estratégia<br/>staging exclusivo, SHA-512, assinatura,<br/>nomes de pacote validados (4.3 e 4.4)"]
    exe --> checks
    pkgs --> checks
    explicit --> checks
    checks -- "falha" --> e5["Código 5, 6 ou 7, nada executado"]
    checks -- "ok" --> go
```

### 4.2 Estratégia `archive` global

```mermaid
sequenceDiagram
    autonumber
    actor Admin as Administrador
    participant CLI as dotnetscope elevado
    participant Net as builds.dotnet.microsoft.com
    participant Stg as Staging na raiz global
    participant Root as Raiz global
    participant Sys as Ambiente do sistema

    Admin->>CLI: install sdk 10.0 --scope global --source archive
    CLI->>Root: valida dono e permissões da raiz e dos ancestrais
    CLI->>Root: lock exclusivo .dotnetscope.lock
    CLI->>Stg: cria staging dono root, modo 0700 ou DACL só Administradores
    CLI->>Net: HTTPS na allowlist, download em streaming
    CLI->>Stg: SHA-512 do metadata conferido antes de abrir o arquivo
    CLI->>Stg: extração confinada, sem links nem entradas especiais
    CLI->>Root: journal e mesclagem por renomeação, sem seguir links
    CLI->>Sys: grava profile.d, paths.d ou HKLM com backup e escrita atômica
    CLI->>Sys: install_location aponta para a raiz validada
    CLI->>Root: valida o host com ambiente mínimo
    CLI-->>Admin: relatório, manifesto global atualizado
```

### 4.3 Instaladores oficiais `.exe` e `.pkg`

```mermaid
sequenceDiagram
    autonumber
    participant CLI as dotnetscope elevado
    participant Net as builds.dotnet.microsoft.com
    participant Stg as Staging exclusivo
    participant Inst as installer.exe ou installer -pkg

    CLI->>Stg: cria diretório exclusivo, dono root ou DACL Administradores e SYSTEM
    CLI->>Net: baixa o .exe ou .pkg indicado pelo metadata
    CLI->>Stg: abre o arquivo e mantém o handle sem compartilhar escrita nem exclusão
    CLI->>Stg: confere SHA-512 pelo handle aberto
    CLI->>Stg: verifica assinatura Authenticode ou pkgutil, ver D-F7-1
    CLI->>Inst: caminho absoluto, ArgumentList, ambiente mínimo, timeout
    Inst-->>CLI: código de saída mapeado para os códigos da CLI
    CLI->>Stg: fecha o handle e remove o staging
```

### 4.4 Estratégia `apt`

```mermaid
sequenceDiagram
    autonumber
    participant CLI as dotnetscope elevado
    participant Apt as /usr/bin/apt-get e apt-cache
    participant Repo as Fontes apt configuradas

    CLI->>Apt: apt-cache policy dotnet-sdk-10.0 com ambiente mínimo
    Apt->>Repo: índices assinados
    Apt-->>CLI: versões candidatas
    CLI->>CLI: valida nome e versão do pacote por expressão regular
    CLI->>Apt: apt-get install -y dotnet-sdk-10.0=versão com ArgumentList
    Apt->>Repo: pacotes verificados por assinatura do apt
    Apt-->>CLI: código de saída
    CLI->>CLI: registra no manifesto global com origem package
```

## 5. Ameaças (STRIDE)

Severidade inerente: impacto × probabilidade sem as mitigações desta tabela.
Severidade residual: depois das mitigações. Escala: crítica, alta, média e baixa.

| ID | STRIDE | Fronteira | Ameaça | Inerente | Mitigação (requisito) | Residual |
| --- | --- | --- | --- | --- | --- | --- |
| T01 | T, E | TB4 | Artefato adulterado na rede ou na CDN é instalado ou executado como root. | crítica | R01, R02 | média |
| T02 | T, E | TB3, TB5 | O arquivo é trocado entre a verificação e o uso (TOCTOU). No Windows, o `%TEMP%` do token elevado é o mesmo do não elevado. | crítica | R03, R04 | baixa |
| T03 | E | TB5, TB6 | DLL ou binário plantado no diretório de onde o instalador `.exe` é executado. | alta | R03 | baixa |
| T04 | E | TB5 | Raiz global ou ancestral gravável por usuário comum (pré-criada, `--install-dir` inseguro): binário plantado é executado por root e por todos os usuários via `PATH` do sistema. | crítica | R05 | baixa |
| T05 | T, E | TB5 | Link simbólico ou junção no destino, staging ou arquivo de sistema faz o root criar, sobrescrever ou apagar arquivos fora do alvo. | alta | R05, R06 | baixa |
| T06 | T, E | TB2 | Ambiente herdado do usuário: `SSL_CERT_FILE`/`SSL_CERT_DIR` com CA maliciosa (MITM de metadata e artefato), `HOME`/`XDG_*` levando estado e cache para área do usuário, `DOTNET_*` alterando a validação. | alta | R07, R08 | baixa |
| T07 | T | TB3 | Cache de metadata do usuário lido pelo root: hashes e versões forjados ou downgrade para versão oficial vulnerável. | alta | R08 | baixa |
| T08 | T, E | TB5 | Escrita insegura ou sobrescrita de `install_location`, `profile.d`, `paths.d` ou `HKLM`: todos os apphosts carregam um runtime do atacante, ou o .NET do pacote quebra. | alta | R05, R09 | baixa |
| T09 | T, D | TB5 | Mistura com .NET de pacote, MSI ou `.pkg` no mesmo destino ou `PATH`: corrompe o pacote ou é sobrescrita na próxima atualização. | média | R10 | baixa |
| T10 | E | TB6 | Injeção de argumentos ou opções em `apt-get`, `installer.exe`, `installer` ou `pkgutil` a partir de entrada do usuário ou do metadata. | alta | R11 | baixa |
| T11 | S | TB2, TB6 | Executável externo resolvido pelo `PATH` do usuário (`apt-get` falso) ou com `LD_PRELOAD`, `DYLD_*`, `APT_CONFIG` herdados. | alta | R11, R07 | baixa |
| T12 | T | TB6 | `apt` instala de fonte de terceiro, ou a CLI amplia a confiança adicionando repositório ou chave. | média | R12 | média |
| T13 | T, D | TB3 | Lock, estado, journal ou backups globais pré-criados ou editados por usuário comum: negação de serviço ou estado forjado tratado como confiável (por exemplo, remover arquivos do sistema). | média | R13 | baixa |
| T14 | R | TB1 | Sem trilha de quem instalou ou removeu o quê no escopo global. | baixa | R14 | baixa |
| T15 | I | TB1 | Log como root expõe variáveis, credenciais de proxy em URL ou conteúdo de arquivos de sistema. | baixa | R15 | baixa |
| T16 | D | TB6 | Instalador oficial travado, interativo ou bloqueado (UAC, reinicialização pendente, outra instalação MSI em andamento). | média | R16 | baixa |
| T17 | E | TB1 | Autoelevação ou execução parcial sem privilégio, deixando a raiz global inconsistente. | média | R17 | baixa |
| T18 | T | TB5 | `uninstall --scope global` remove arquivos de pacote ou de outra instalação. | média | R10, R13, R18 | baixa |
| T19 | E | TB6 | `--install-deps` instala pacotes cujo nome vem de entrada não confiável. | baixa | R19 | baixa |
| T20 | D | TB5 | Interrupção (Ctrl+C, queda de energia) deixa a raiz global inconsistente para todos os usuários. | média | R20 | baixa |

## 6. Requisitos derivados para a F7

Cada requisito vira tarefa da F7 e tem ao menos um teste negativo.

Estado de implementação (o plano divide a F7 em F7a a F7d):

| Etapa | Requisitos |
| --- | --- |
| F7a (Linux, `archive`) | R03 e R18 para `archive`; R05, R06, R07, R08, R09, R13, R17 e R20; R10 parcial (raízes de pacote e `dotnet` concorrente no `PATH` do sistema); R14 parcial (data, origem, hash e escopo, sem o usuário que elevou) |
| F7b (`apt`) | R10, R11, R12 e R19 (implementados); em R07, as variáveis de proxy são repassadas aos gerenciadores de pacotes, que verificam assinaturas |
| F7c (Windows) | R01 a R04, R16, R17 e R18 para `.exe` (implementados e validados em Windows real); `archive` no Windows descartado pelo revisor humano |
| F7d (macOS) | R01 a R03, R16 e R17 para `.pkg` (implementados e validados em macOS real); `archive` no macOS descartado pelo revisor humano; T04 com raiz do `.pkg` de outro dono só gera aviso |

| ID | Requisito | Ameaças | Verificação |
| --- | --- | --- | --- |
| R01 | Manter a allowlist HTTPS, recusar redirects para fora dela e conferir o SHA-512 do metadata antes de extrair ou executar qualquer artefato global. | T01 | Testes existentes, estendidos a `.exe` e `.pkg`: hash divergente retorna código `5` sem executar nada. |
| R02 | Verificar a assinatura do instalador antes de executá-lo: Authenticode (`WinVerifyTrust`, assinante Microsoft) no `.exe` e `pkgutil --check-signature` (Developer ID Microsoft) no `.pkg`. Usa somente APIs e ferramentas do sistema (D-F7-1). | T01 | Integração: instalador sem assinatura, com assinatura de terceiro ou assinatura inválida é recusado (código `5`). |
| R03 | Staging do escopo global em diretório exclusivo do processo elevado: dentro da raiz validada (`archive`) ou em `/var/cache/dotnetscope` e `%ProgramData%\dotnetscope\staging` (instaladores). Dono root e modo `0700`, ou DACL protegida só com Administradores e SYSTEM. Nunca usar `TMPDIR` ou `%TEMP%`. O diretório contém apenas o artefato. | T02, T03 | Integração: staging criado com dono e ACL esperados; `%TEMP%` nunca é usado (verificado por caminho no relatório `--verbose`). |
| R04 | Conferir o hash e executar o instalador com o arquivo aberto, sem compartilhar escrita nem exclusão até o fim da execução. | T02 | Integração no Windows: um segundo processo não consegue sobrescrever nem renomear o arquivo durante a execução. |
| R05 | Validar a raiz global e todos os ancestrais antes de qualquer escrita. No Linux e macOS: dono uid 0 e sem escrita para grupo ou outros, inclusive após resolver links. No Windows: dono Administradores ou SYSTEM e DACL sem escrita para `Users`, `Authenticated Users` ou `Everyone`. Falha com código `7`. Os mesmos critérios valem para diretórios de arquivos de sistema editados (`/etc/profile.d`, `/etc/paths.d`, `/etc/dotnet`). | T04, T05, T08 | Integração em container como root: diretórios com dono 65534, modo `0777` ou ancestral gravável são recusados; Windows: diretório com ACE de escrita para `Users` é recusado. |
| R06 | Não seguir links durante criação, mesclagem, gravação de arquivos de sistema e remoção. Verificar `LinkTarget` antes de cada operação e remover links sem tocar no alvo. | T05 | Integração: link em staging, raiz ou `/etc/dotnet` apontando para `/etc` não altera o alvo. |
| R07 | Ambiente mínimo e caminhos fixos no escopo global: ignorar `HOME` e `XDG_*` para estado e cache; executar processos externos com ambiente explícito (sem `LD_PRELOAD`, `DYLD_*`, `APT_CONFIG`, `DOTNET_*` herdados); `SSL_CERT_FILE` e `SSL_CERT_DIR` são ignorados: vale só o repositório de certificados do sistema (D-F7-2). | T06, T11 | Unitário: construção do ambiente dos processos; integração: `XDG_CACHE_HOME` e `HOME` apontando para diretório do usuário não são lidos como root. |
| R08 | Cache de metadata global separado e protegido (`/var/cache/dotnetscope`, `%ProgramData%\dotnetscope\cache` com DACL protegida). O escopo global nunca lê o cache do usuário. | T06, T07 | Integração: cache do usuário com hash forjado não influencia a resolução global. |
| R09 | Arquivos e chaves de sistema: escrita atômica, dono root e modo `0644` (ou chave em `HKLM`), bloco ou valor gerenciado, backup no estado global, conflito com valor de terceiro retorna código `6` (política `--env-conflict`), remoção exata. `install_location` só aponta para raiz que passou em R05. | T08 | Integração Linux/macOS e Windows: conflito, idempotência, remoção exata e restauração do valor anterior. |
| R10 | Detectar instalações gerenciadas por pacote antes de escrever: `dpkg -S`/`rpm -qf`, `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions` e registros MSI, `pkgutil --pkgs` (`com.microsoft.dotnet.*`). A estratégia `archive` recusa o mesmo destino e um `dotnet` concorrente no `PATH` do sistema, com código `6` e orientação. | T09, T18 | E2E: Ubuntu com `dotnet-sdk-*` do `apt` recusa `archive` em `/usr/lib/dotnet`; Windows com SDK do runner recusa `archive` em `%ProgramFiles%\dotnet`. |
| R11 | Processos externos com caminho absoluto fixo (`/usr/bin/apt-get`, `/usr/bin/apt-cache`, `/usr/sbin/installer`, `/usr/sbin/pkgutil`, instalador no staging), `ArgumentList`, ambiente de R07 e timeout. Nomes e versões de pacote validados por expressão regular estrita antes do uso. | T10, T11 | Unitário: valores com espaço, `-o`, `;` ou `=` extra são recusados; integração: `PATH` com `apt-get` falso não é usado. |
| R12 | A estratégia `apt` usa somente as fontes já configuradas; a CLI não adiciona repositórios nem chaves (D-F7-3). | T12 | Revisão de código e teste: nenhuma escrita em `/etc/apt`. |
| R13 | Estado global (manifesto, journal, lock, backups) em `/var/lib/dotnetscope` e `%ProgramData%\dotnetscope`, com dono e permissões de R05. Estado com dono inesperado é recusado (código `6`), nunca usado. | T13, T18 | Integração: manifesto global pré-criado por outro usuário é recusado. |
| R14 | O manifesto global registra data, estratégia, origem, hash e o usuário que elevou (`SUDO_USER` ou usuário do token), sem dados sensíveis. | T14 | Unitário do formato; E2E confere o registro. |
| R15 | Manter as regras de log atuais e remover `userinfo` de URLs de proxy exibidas. | T15 | Unitário: proxy `http://usuario:senha@proxy` não aparece em `--verbose`. |
| R16 | Instaladores com `/install /quiet /norestart` ou `installer -pkg -target /`, timeout e mapeamento de códigos (`3010`/`1641` → sucesso com aviso de reinicialização; `1618` → código `6`). | T16 | Unitário do mapeamento; E2E no Windows. |
| R17 | Verificar a elevação antes de qualquer efeito (`geteuid() == 0`; token elevado e grupo Administradores no Windows). Sem elevação, código `7`. Nunca chamar `sudo`, `runas` ou ShellExecute com `runas`. | T17 | Unitário com serviço de elevação injetado; E2E sem elevação. |
| R18 | `uninstall --scope global` usa o mecanismo da estratégia: manifesto e posse de folhas (`archive`), `apt-get remove` do pacote registrado (`apt`) e o instalador oficial com `/uninstall /quiet`, verificado como em R02 (`.exe`). Instalações por `.pkg` não são removidas automaticamente: a CLI orienta a remoção manual (D-F7-4). Nunca remove o que não registrou. | T18 | E2E: remoção completa restaura o estado anterior; pacote não registrado não é removido. |
| R19 | `--install-deps` instala somente nomes de pacote de uma tabela fixa por distribuição no código. | T19 | Unitário da tabela; nenhum valor de entrada vira nome de pacote. |
| R20 | Journal, lock e `doctor --repair --scope global` (exige elevação) para recuperar operações interrompidas, como no escopo user. | T20 | Integração com falha injetada na mesclagem global. |

## 7. Decisões

Aprovadas por Lincoln Zocateli em 2026-10-21, que seguiu as recomendações com
a condição de que não acrescentassem custo financeiro nem complexidade ao
processo. Todas atendem à condição: usam apenas APIs e ferramentas já presentes
nos sistemas, não exigem certificado, serviço ou etapa de release nova, e três
delas reduzem os casos a tratar.

| ID | Pergunta | Decisão | Custo e processo |
| --- | --- | --- | --- |
| D-F7-1 | Verificar assinatura de `.exe` e `.pkg` já na F7 (o plano previa a F9)? | Sim, na F7 (R02). | Verifica a assinatura da Microsoft com `WinVerifyTrust` e `pkgutil`; nenhum certificado pago nem passo de release. A assinatura dos artefatos do próprio DotnetScope continua na F9. |
| D-F7-2 | Aceitar `SSL_CERT_FILE`/`SSL_CERT_DIR` do ambiente no escopo global? | Não; vale só o repositório de certificados do sistema (R07). CA corporativa deve ser instalada no sistema. | Reduz casos a tratar. |
| D-F7-3 | A estratégia `apt` pode adicionar o repositório `packages.microsoft.com`? | Não; só fontes configuradas. Sem o pacote, `--source auto` usa `archive` (R12). | Nada de repositório ou chave para manter. |
| D-F7-4 | Como remover instalações feitas por `.exe` e `.pkg`? | `.exe /uninstall /quiet` com a verificação de R02; `.pkg` sem remoção automática, com orientação (R18). | Reaproveita R02; nada novo no macOS. |
| D-F7-5 | `--install-dir` no escopo global do Windows | Aceita qualquer diretório que passe em R05. | R05 já é obrigatório. |

## 8. Riscos residuais

- Comprometimento da Microsoft (metadata e assinatura juntos): fora do controle
  do DotnetScope; mitigado parcialmente pela atestação e pelo monitoramento
  público dos releases.
- Fontes `apt` de terceiros já configuradas pelo administrador (T12): a CLI
  confia no `apt` como o administrador já confia.
- Administrador que executa a CLI de um binário adulterado: coberto pela
  cadeia de release ([F5](release.md) e F9), não pela F7.
- Macros de ambiente expandidas pelo shell antes do `sudo` (por exemplo,
  `--install-dir $HOME/x`): a CLI valida o resultado (R05), não a intenção.

## 9. Workflow de verificação

```mermaid
flowchart LR
    tm["Modelo de ameaças<br/>este documento"] --> rev{"Revisão humana<br/>D-F7-1 a D-F7-5, aprovada"}
    rev -- "ajustes" --> tm
    rev -- "aprovado" --> req["Requisitos R01 a R20<br/>no plano, seção F7"]
    req --> impl["Implementação por estratégia<br/>archive, apt, exe, pkg"]
    impl --> neg["Testes negativos por ameaça<br/>unitário, integração, E2E"]
    neg --> ci{"CI verde<br/>Linux, Windows, macOS"}
    ci -- "falha" --> impl
    ci -- "verde" --> sec["Revisão de segurança independente<br/>security-review"]
    sec -- "achado crítico ou alto" --> impl
    sec -- "sem bloqueio" --> done(["F7 concluída"])
```

```mermaid
flowchart TB
    subgraph Unit["Unitário em container"]
        u1["Política de elevação e ambiente mínimo<br/>R07, R11, R17"]
        u2["Validação de nomes e versões de pacote<br/>R11, R19"]
        u3["Mapeamento de códigos dos instaladores<br/>R16"]
    end
    subgraph Int["Integração"]
        i1["Container como root: dono, modo, links<br/>R05, R06, R13"]
        i2["Cache e estado do usuário ignorados<br/>R07, R08"]
        i3["Windows: DACL, handle exclusivo, Authenticode<br/>R03, R04, R02"]
    end
    subgraph E2E["E2E em ambiente descartável"]
        e1["Ubuntu e Fedora: archive e apt global"]
        e2["windows-2025: instalador .exe e archive"]
        e3["macos-15: .pkg e archive"]
    end
    Unit --> Int --> E2E
```

## 10. Referências

- [Plano de implementação](plano-implementacao.md): seções 4.2, 5, 9, 14 e 15.
- [Referência da CLI](cli.md): comportamento atual do escopo user.
- [Política de segurança](../.github/SECURITY.md).
- [Regras de segurança do repositório](../.github/instructions/security.instructions.md).
