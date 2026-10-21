# Plano de implementação — DotnetScope

| Campo | Valor |
| --- | --- |
| Status | aprovado para F0 (decisões de 2026-10-20) |
| Origem | IA assistida |
| Revisor humano | Lincoln Zocateli |
| Repositório | <https://github.com/lzocateli/DotnetScope> |
| Licença | MIT |

## 1. Objetivo

O DotnetScope é uma CLI Native AOT (`dotnetscope`) que instala, lista, valida e
remove SDKs e runtimes .NET nos escopos `user` e `global`. Ele resolve versões
a partir do metadata oficial, verifica a integridade dos artefatos, extrai de
forma segura e, por padrão, configura `DOTNET_ROOT` e `PATH` no perfil do
usuário.

Não é objetivo substituir gerenciadores de pacotes da distribuição nem o
`dotnet-install`; o DotnetScope orquestra esses mecanismos com uma interface
única, verificação e reversão.

## 2. Decisões registradas

| # | Decisão | Valor |
| --- | --- | --- |
| D1 | Licença | MIT |
| D2 | Nome do binário | `dotnetscope` |
| D3 | Diretório padrão do escopo `user` | `~/.dotnet` (Linux/macOS) e `%LOCALAPPDATA%\Microsoft\dotnet` (Windows), compartilhado com `dotnet-install`, Ansible e IDEs |
| D4 | Instalação global por arquivo, fora do gerenciador de pacotes | Sim, no roadmap (F7) |
| D5 | Processo e hospedagem | GitHub, sem Spec Kit: Issue → fase deste plano → branch → PR |
| D6 | Configuração de ambiente | `--configure-env` com padrão `true`; `false` só imprime instruções |

## 3. Interface da CLI

```text
dotnetscope install <componente> <versão> [opções]
dotnetscope install SDK-10.0.400                  # forma compacta
dotnetscope install --from-global-json [caminho]
dotnetscope uninstall <componente> <versão> [--scope user|global] [--force]
dotnetscope list [--installed | --available <canal>] [--scope ...]
dotnetscope env show|apply|remove [--scope ...] [--shell bash|zsh|fish|pwsh]
dotnetscope doctor [--repair]
dotnetscope --version | --help
```

### 3.1 Componentes e versões

- Componentes: `sdk`, `runtime`, `aspnetcore` e `windowsdesktop` (só Windows).
- Forma compacta: `SDK-10.0.400`, `RUNTIME-10.0`, `ASPNETCORE-10.0`.
- Especificação de versão:
  - exata: `10.0.400`;
  - canal: `10.0` (último patch);
  - feature band: `10.0.4xx` (último patch da faixa, só SDK);
  - `lts` ou `sts` (canal mais recente com a fase de suporte indicada).
- Preview somente com `--prerelease`; versão EOL instala com aviso.
- Se a versão resolvida for diferente da escrita, a CLI exibe o plano e exige
  confirmação (ou `--yes`).

### 3.2 Opções de `install`

| Opção | Padrão | Descrição |
| --- | --- | --- |
| `--scope user\|global` | `user` | Escopo da instalação |
| `--configure-env true\|false` | `true` | Configura `DOTNET_ROOT` e `PATH` de forma persistente |
| `--env-conflict fail\|override\|skip` | `fail` | Política quando terceiros já definem valor diferente |
| `--telemetry-optout` | `false` | Adiciona `DOTNET_CLI_TELEMETRY_OPTOUT=1` ao bloco gerenciado |
| `--install-dir <dir>` | ver D3 e seção 5 | Raiz alternativa da instalação |
| `--source auto\|archive\|apt\|installer\|pkg` | `auto` | Estratégia de instalação |
| `--archive <arquivo> --sha512 <hash>` | — | Instalação offline com hash obrigatório |
| `--install-deps` | `false` | Instala dependências nativas (somente `global` em Linux) |
| `--allow-root` | `false` | Permite escopo `user` executando como root |
| `--prerelease` | `false` | Permite versões preview/RC |
| `--offline` | `false` | Usa apenas metadata em cache |
| `--dry-run` | `false` | Mostra o plano sem efeitos |
| `--yes` | `false` | Confirma o plano sem prompt |
| `--output text\|json` | `text` | Formato de saída |
| `--verbose` | `false` | Diagnóstico detalhado em stderr |

Sem TTY e sem `--yes`, a CLI não pede confirmação: falha com código de uso.

### 3.3 Códigos de saída (proposta)

| Código | Significado |
| --- | --- |
| 0 | sucesso |
| 1 | erro inesperado |
| 2 | uso inválido ou confirmação ausente sem TTY |
| 3 | versão ou artefato não encontrado para SO/arquitetura |
| 4 | falha de rede ou metadata indisponível |
| 5 | falha de integridade (hash, arquivo malicioso) |
| 6 | conflito de instalação ou de ambiente |
| 7 | privilégio insuficiente ou inadequado |
| 8 | dependência nativa ausente |
| 130 | cancelado pelo usuário |

Os códigos são contrato estável a partir da primeira release.

## 4. Configuração de ambiente (`--configure-env`, padrão `true`)

### 4.1 Variáveis gerenciadas

- `DOTNET_ROOT` = raiz da instalação.
- `PATH` += raiz da instalação e `~/.dotnet/tools` (no escopo `global`, apenas a raiz).
- Opcional: `DOTNET_CLI_TELEMETRY_OPTOUT=1` com `--telemetry-optout`.

### 4.2 Destino por SO e escopo

| Escopo / SO | `true` | `false` |
| --- | --- | --- |
| user Linux | `~/.config/dotnetscope/env.sh` + bloco que o carrega em `~/.profile` e `~/.bashrc`; `~/.zprofile`/`~/.zshrc` se zsh existir; `~/.config/fish/conf.d/dotnetscope.fish` se fish existir | só imprime instruções |
| user macOS | `~/.config/dotnetscope/env.sh` + bloco em `~/.zprofile` (e bash/fish se presentes) | só imprime |
| user Windows | `HKCU\Environment` (`REG_EXPAND_SZ`) + `WM_SETTINGCHANGE` | só imprime |
| global Linux | `/etc/profile.d/dotnetscope.sh`; instalação por arquivo também grava `/etc/dotnet/install_location_<arch>` | só imprime |
| global macOS | `/etc/paths.d/dotnetscope` | só imprime |
| global Windows | `HKLM\...\Session Manager\Environment` + `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation` (instalação por arquivo) | só imprime |

### 4.3 Regras

1. Escrita somente entre `# >>> dotnetscope >>>` e `# <<< dotnetscope <<<`.
2. O bloco é idempotente; entradas de `PATH` usam guarda `case ":$PATH:"`.
3. Backup em `~/.local/state/dotnetscope/backups/<arquivo>.<timestamp>` antes de
   editar; gravação atômica preservando permissões e final de linha.
4. Perfil que é symlink (dotfiles) gera aviso e exige confirmação.
5. Detecção de terceiros, por exemplo `# BEGIN ANSIBLE USER DOTNET`,
   `/etc/profile.d/dotnet.sh` e `/usr/bin/dotnet`:
   - mesmo valor efetivo: sem conflito; o bloco próprio é escrito com guardas e
     não duplica entradas;
   - valor diferente: nada é alterado, a CLI reporta e sai com código 6, salvo
     `--env-conflict override|skip`.
6. Blocos de terceiros nunca são editados.
7. Valores são escapados para o shell-alvo; caminhos com espaço, `$`, aspas ou
   crase nunca viram código.
8. O shell atual não muda: a CLI imprime `source ~/.config/dotnetscope/env.sh` ou
   `eval "$(dotnetscope env show --shell bash)"`.
9. Windows nunca usa `setx` (trunca em 1024 caracteres).
10. `uninstall` remove o bloco quando não restar instalação gerenciada;
    `env remove` remove explicitamente, preservando o restante do arquivo byte a byte.

Exemplo de bloco no Linux:

```sh
# >>> dotnetscope >>>
# Gerenciado por dotnetscope. Use `dotnetscope env remove` para remover.
[ -f "$HOME/.config/dotnetscope/env.sh" ] && . "$HOME/.config/dotnetscope/env.sh"
# <<< dotnetscope <<<
```

## 5. Estratégias por escopo

| SO | `user` | `global` (`--source auto`) |
| --- | --- | --- |
| Linux | `archive` (`tar.gz`) em `~/.dotnet`, sem sudo | `apt` quando o canal/versão estiver disponível; senão `archive` em `/usr/local/share/dotnet` |
| Windows | `archive` (zip) em `%LOCALAPPDATA%\Microsoft\dotnet` | `installer` oficial (`.exe`, `/install /quiet /norestart`) quando a versão existir; `archive` em `%ProgramFiles%\dotnet` somente se o destino não for gerenciado por instalador |
| macOS | `archive` (`tar.gz`) em `~/.dotnet` | `pkg` oficial (`installer -pkg`); `archive` em `/usr/local/share/dotnet` somente se o destino não for gerenciado por `.pkg` |

Regras comuns:

- `global` exige processo já elevado; não há autoelevação.
- `user` recusa root sem `--allow-root`.
- Instalação global por arquivo (D4) é recusada se houver .NET global gerenciado
  por pacote (`dpkg -S`, `/usr/lib/dotnet`, MSI/`.pkg` registrados) no mesmo
  destino ou concorrendo pelo mesmo `dotnet` no `PATH`; a CLI orienta usar
  `--source apt|installer|pkg` ou outro `--install-dir`.
- Coexistência no mesmo diretório (D3): versões existentes em `sdk/<v>` e
  `shared/<fw>/<v>` nunca são substituídas; `dotnet` e `host/fxr` só são
  atualizados se a versão nova for maior.
- `list --installed` distingue origem: `dotnetscope`, `external` (Ansible,
  `dotnet-install`, IDE) e `package` (`apt`, MSI, `.pkg`).

## 6. Resolução de versões

- Fonte: `releases-index.json` → `releases.json` do canal, com versões,
  `support-phase`, datas de EOL e arquivos por RID com hash SHA-512.
- Cache em `~/.cache/dotnetscope` (Windows: `%LOCALAPPDATA%\dotnetscope\cache`)
  com TTL; `--offline` usa somente o cache.
- Allowlist HTTPS de hosts oficiais de metadata e download; redirect fora dela
  falha fechado.
- `--from-global-json` lê `sdk.version`, `rollForward` e `allowPrerelease`.
- `apt`: consulta `apt-cache policy dotnet-sdk-<canal>` para decidir se atende o pedido.

## 7. Download e verificação

- `HttpClient` único com timeout, retry exponencial para falhas transitórias,
  `HTTPS_PROXY`/`NO_PROXY` e CA corporativa (`SSL_CERT_DIR`/`SSL_CERT_FILE`).
- Download em diretório temporário privado (0700) no mesmo filesystem do destino.
- SHA-512 calculado em streaming e comparado antes de qualquer extração ou execução.
- Instalação offline: `--archive <arquivo> --sha512 <hash>`.
- Verificação da assinatura Authenticode do instalador Windows e da assinatura
  do `.pkg` no macOS antes de executá-los: antecipada para a F7 (D-F7-1 do
  [modelo de ameaças](modelo-de-ameacas-f7.md#7-decisões)).

## 8. Extração e transação

- `System.Formats.Tar` + `GZipStream`; `System.IO.Compression` para zip.
- Rejeitar caminhos absolutos, `..`, symlinks/hardlinks para fora do destino,
  entradas de dispositivo e nomes duplicados; preservar bits de execução.
- Staging em `<raiz>/.dotnetscope-staging-<id>` e `rename` atômico por diretório
  de componente.
- Lock de instalação por raiz (`.dotnetscope.lock`); journal `journal.json` no
  staging; `doctor --repair` (F4) remove staging órfão e recupera instalações
  parciais a partir do journal.
- Unidade de instalação ("folha"): `sdk/<v>`, `templates/<v>`, `shared/<fw>/<v>`,
  `host/fxr/<v>`, `packs/<pack>/<v>`, `sdk-manifests/<faixa>/<id>/<v>` e itens de
  primeiro nível (`dotnet`, `dnx`, `LICENSE.txt`). O manifesto registra as folhas do
  arquivo (`paths`) e as criadas pela instalação (`addedPaths`); a remoção (F4) só
  apaga folhas que nenhuma outra instalação registrada contém.
- Manifesto em `~/.local/share/dotnetscope/manifest.json` (Windows:
  `%LOCALAPPDATA%\dotnetscope\manifest.json`) com componente, versão, origem,
  caminhos e hash. `uninstall` remove apenas o que o DotnetScope instalou;
  conteúdo `external` exige `--force`.

## 9. Dependências nativas (Linux)

- Detectar ICU, OpenSSL, zlib, libstdc++, libgcc e CA certificates.
- `user`: apenas reporta o que falta e sugere o comando da distribuição.
- `global`: `--install-deps` instala via gerenciador de pacotes.

## 10. Validação pós-instalação

1. `<raiz>/dotnet --list-sdks` e `--list-runtimes` com ambiente explícito.
2. Shell de login novo (`bash -lc 'command -v dotnet; dotnet --version'`, ou
   equivalente) para detectar sombreamento por outro `dotnet` no `PATH`.
3. Relatório final: versão resolvida, destino, estratégia, variáveis, arquivos
   editados, backups e próximos comandos.

## 11. Arquitetura

```text
DotnetScope/
  .github/                         # instruções, templates, workflows
  .githooks/                       # pre-commit Gitleaks (Docker ou Podman)
  docs/                            # plano, ADRs, referência da CLI
  src/DotnetScope.Cli/             # entrada, parsing, UX, códigos de saída
  src/DotnetScope.Core/            # domínio e políticas, sem I/O
  src/DotnetScope.Infrastructure/  # metadata, download, extração, estratégias, ambiente
  tests/DotnetScope.UnitTests/
  tests/DotnetScope.IntegrationTests/
  tests/e2e/                       # containers por distro e scripts de cenário
  tools/scripts/                   # build, testes em container, publicação AOT, empacotamento
  Directory.Build.props
  Directory.Packages.props
  global.json                      # SDK 10.0.400, rollForward latestPatch
  DotnetScope.slnx
```

- Interfaces: `IPlatformDetector`, `IReleaseMetadataClient`, `IDownloader`,
  `IArchiveExtractor`, `IInstallStrategy`, `IEnvironmentConfigurator`,
  `IProcessRunner`, `IFileSystem`, `IManifestStore`.
- Estratégias: `ArchiveInstallStrategy`, `AptInstallStrategy`,
  `WindowsInstallerStrategy`, `MacPkgStrategy`.
- Configuradores: `PosixProfileConfigurator`, `FishConfigurator`,
  `WindowsRegistryConfigurator`, `SystemProfileConfigurator`.
- `PublishAot`, `IsAotCompatible`, `InvariantGlobalization`, `TreatWarningsAsErrors`.
- JSON por `JsonSerializerContext`; P/Invoke por `LibraryImport`; processos por
  `ProcessStartInfo.ArgumentList`.
- Dependências externas: apenas `System.CommandLine` (MIT, versão estável fixada,
  validada com AOT). Testes: xUnit.

## 12. Testes

| Nível | Cobertura |
| --- | --- |
| Unitário | parsing de versões e forma compacta, resolução com fixtures, seleção de estratégia, geração/remoção de blocos, escaping por shell, códigos de saída |
| Integração | filesystem real temporário, servidor HTTP local com metadata/arquivos sintéticos, tar/zip maliciosos, hash divergente, lock concorrente, falha no meio da transação, registro Windows em runner |
| E2E | binário AOT em containers limpos sem .NET: Ubuntu 22.04/24.04, Debian 12, Fedora, Alpine (musl); runners Windows e macOS |
| Agendado | download real dos artefatos oficiais e instalação completa |

Casos obrigatórios de ambiente:

- `--configure-env false` mantém perfis com hash idêntico;
- `true` aplicado duas vezes produz o mesmo arquivo;
- conflito com bloco Ansible de valor diferente sai com código 6 sem alterar nada;
- `uninstall`/`env remove` restauram o perfil original;
- shell de login novo resolve `dotnet` para a raiz instalada.

Testes que editam perfil, registro ou `PATH` só executam em containers ou
runners efêmeros.

## 13. Build, distribuição e releases

- RIDs: `linux-x64`, `linux-arm64`, `linux-musl-x64`, `win-x64`, `win-arm64`,
  `osx-arm64`, `osx-x64`.
- Native AOT publicado no próprio SO em matriz de CI. RIDs Linux usam as
  imagens oficiais de cross-build do .NET (`azurelinux-3.0-net10.0-cross-*`):
  sysroot glibc 2.27 (Ubuntu 18.04) ou musl 1.2.3 (Alpine 3.17), clang e lld
  (decisão da F5; ver [processo de release](release.md)).
- Build local e no CI em `docker.io/lzocateli/dotnet-sdk:10.0.402-noble-v1`
  via `tools/scripts/Invoke-Validation.ps1`; com `-Aot`, `clang` e `zlib1g-dev`
  são instalados no container descartável (decisão da F0b; imagem própria com
  toolchain AOT pode ser criada na F5).
- Artefatos: `dotnetscope-<rid>.tar.gz`/`.zip`, `install.sh`, `install.ps1`,
  `SHA256SUMS` e atestação de proveniência (GitHub Attestations); assinatura e
  SBOM na F9. Os nomes não levam versão para que `releases/latest/download`
  seja estável (decisão da F5).
- Canais, em ordem: GitHub Releases + `install.sh`/`install.ps1` (verificam
  checksum, sem autoelevação) → `.deb` e repositório APT assinado → winget e Homebrew.
- O próprio `dotnetscope` instala em `~/.local/bin` (user) ou `/usr/local/bin` (global).
- Versionamento SemVer a partir de `0.1.0`; tag `vX.Y.Z` publica após aprovação
  no Environment `release`.

## 14. Segurança

- Nunca executar scripts baixados.
- Integridade verificada antes de extrair ou executar.
- Extração confinada ao destino normalizado.
- Nada sobrescrito ou removido sem ação explícita.
- Privilégio somente em `global`, sem autoelevação.
- `--install-dir` normalizado, absoluto e validado.
- Estado, manifesto, lock e backups com permissões restritas.
- Logs sem variáveis completas, conteúdo de perfis, tokens ou URLs com credenciais.
- Threat model antes da F7 (escopo global) e revisão de segurança antes da 1.0.
  O da F7 está em [modelo-de-ameacas-f7.md](modelo-de-ameacas-f7.md), aprovado
  pelo revisor humano em 2026-10-21 (decisões D-F7-1 a D-F7-5, sem custo
  financeiro nem complexidade de processo adicionais).

## 15. Fases

| Fase | Entrega | Critério de aceite | Estado |
| --- | --- | --- | --- |
| F0a | `.github`, `.githooks`, Gitleaks, plano, README, LICENSE | instruções e hook validados | concluída |
| F0b | `global.json`, `Directory.*.props`, `DotnetScope.slnx`, projetos base, `ExitCodes`, `tools/scripts/Invoke-Validation.ps1`, workflow `pr-validation` | build, testes e AOT `linux-x64` verdes em container e no CI | concluída |
| F1 | Parsing, cliente de metadata, cache, `list --available` | resolução correta com fixtures, incluindo `lts`/`sts`/feature band | concluída ([referência](cli.md)) |
| F2 | `install` user Linux por arquivo, verificação, extração segura, manifesto, lock | SDK funcional em container limpo; casos maliciosos rejeitados | concluída (E2E Ubuntu 24.04 e Fedora 41) |
| F3 | `--configure-env` (Linux/macOS), `env show\|apply\|remove`, detecção de conflito | casos obrigatórios da seção 12 verdes | concluída (E2E bash/zsh/fish em Ubuntu 24.04 e Fedora 41) |
| F4 | `uninstall`, `list --installed`, `doctor [--repair]`, `install --archive <arquivo> --sha512 <hash>` (seção 7), `install --from-global-json [caminho]` (seção 3, com `rollForward`) | reversão completa e origem correta na listagem; instalação offline verificada; versão resolvida conforme `global.json` | concluída (E2E com remoção completa em Ubuntu 24.04 e Fedora 41) |
| F5 | Publicação AOT multi-RID, `install.sh`/`install.ps1`, release no GitHub | binários rodam sem .NET; checksums publicados | concluída (smoke em Ubuntu 18.04, Rocky 8, Debian 12, Alpine 3.17/3.24 e runners Windows/macOS; [processo de release](release.md)) |
| F6 | Windows user (zip + registro + `WM_SETTINGCHANGE`) | `dotnet` disponível em novo terminal; reversão do registro | concluída (testes nativos e E2E em `windows-2025`; [referência](cli.md#windows)) |
| F7a | Global Linux por arquivo (D4): elevação, diretórios confiáveis, estado e cache globais, `/etc/profile.d`, `install_location`, `uninstall`/`env`/`list`/`doctor` globais | R03, R05–R09, R13, R14 (parcial), R17, R18 (`archive`) e R20 do [modelo de ameaças](modelo-de-ameacas-f7.md) com testes negativos; E2E Ubuntu 24.04 e Fedora 41 | concluída |
| F7b | Global Linux por `apt` (`--source auto\|archive\|apt`) e `--install-deps` | R10–R12, R19 | concluída (E2E apt em Ubuntu 24.04) |
| F7c | Global Windows: instalador `.exe` com Authenticode (sem `archive`, decisão de 2026-10-21) | R01–R04, R16, R17, R18 | concluída (testes nativos e E2E global em `windows-2025`) |
| F7d | Global macOS: `.pkg` com assinatura (sem `archive`, decisão de 2026-10-21) | R01–R03, R16, R17 | concluída (E2E global em `macos-15`, com raiz confiável e não confiável) |
| F8 | macOS user | E2E em runner macOS | concluída (testes nativos e E2E user em `macos-15`: zsh, bash sem perfil próprio e bash com `~/.bash_profile`) |
| F9 | Assinatura, SBOM, Authenticode/`.pkg`, `.deb`/APT, winget, Homebrew | artefatos assinados e verificáveis | pendente |

Cada fase vira uma Issue (template `Tarefa técnica` ou `Propor funcionalidade`)
com milestone, e é entregue por PR com as evidências do template.

## 16. Riscos e questões em aberto

- Suporte a distribuições sem ICU (o binário usa `InvariantGlobalization`; o
  SDK instalado ainda exige ICU ou `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT`).
- Disponibilidade e granularidade de versões nos feeds `apt` do Ubuntu; sem o
  pacote nas fontes configuradas, o escopo global usa `archive` (D-F7-3).
- A verificação de assinatura dos instaladores oficiais `.exe` e `.pkg` foi
  antecipada da F9 para a F7 (D-F7-1); a F9 mantém a assinatura dos artefatos do
  próprio DotnetScope e a do `.pkg` próprio.
- Comportamento de `install_location` e de `InstalledVersions` com instalações
  globais mistas.
- Shells não suportados na v1 (nushell, tcsh) apenas recebem instruções.
- Custo de manter repositório APT assinado e manifests winget/Homebrew.
- O binário de desenvolvimento de `Invoke-Validation.ps1 -Aot` ainda é
  publicado em Ubuntu 24.04 (glibc 2.39); os pacotes de release usam a base
  glibc 2.27 desde a F5. `linux-musl-arm64` não é publicado.
- `install --archive` e `install --from-global-json` foram atribuídos à F4 em
  2026-10-20 (decisão do revisor humano).
- Decisões de implementação da F3:
  - o bloco dos perfis usa sempre LF;
  - além dos perfis da seção 4.2, `~/.bash_profile` e `~/.bash_login` recebem o
    bloco quando existem, porque o bash de login não lê `~/.profile` nesse caso;
  - a remoção exata usa o estado em `~/.local/state/dotnetscope/env-state.json`.
- Decisões de implementação da F4:
  - a posse das folhas compartilhadas é transferida para a instalação que
    continua usando-as, o que garante que a última remoção apaga tudo o que o
    DotnetScope criou;
  - `doctor` retorna o código `6` quando restam erros;
  - `allowPrerelease` do `global.json` só vale quando `true` explícito;
  - arquivos de usuário que o SDK cria em `~/.dotnet` (`DOTNET_CLI_HOME`) nunca
    são removidos.
- Decisões de implementação da F5:
  - os instaladores não alteram perfis, registro nem `PATH`; mostram a linha a
    adicionar (a configuração de ambiente continua exclusiva do binário);
  - `install.sh` aceita `http://` apenas para `127.0.0.1`/`localhost`
    (`--base-url`, usado nos testes); downloads reais exigem HTTPS sem
    redirecionamento para HTTP;
  - o binário novo é executado no diretório de destino antes de substituir o
    anterior, evitando `/tmp` com `noexec` e trocas por binário inválido;
  - como root, `install.sh` recusa diretórios de destino que não pertencem ao
    root ou são graváveis por grupo/outros (ataque por link simbólico no
    arquivo temporário), e o arquivo temporário é criado com `mktemp`;
  - o one-liner do Windows habilita TLS 1.2 antes de baixar `install.ps1`,
    porque o Windows PowerShell 5.1 pode não habilitá-lo por padrão;
  - smoke de `linux-arm64` e `win-arm64` roda em runners Arm64 nativos
    (`ubuntu-24.04-arm`, `windows-11-arm`), sem emulação.
- Decisões de implementação da F8:
  - no macOS, `~/.profile` também é criado quando o shell de login é bash sem
    `~/.bash_profile` nem `~/.bash_login` (o runner `macos-15` usa bash e
    mostrou que, sem isso, o ambiente não valia para usuários de bash);
  - o E2E do macOS usa `HOME` isolado em diretório temporário e cobre zsh,
    bash sem perfil próprio e bash com `~/.bash_profile`.
- Decisões de implementação da F7d:
  - no macOS, o escopo global usa somente o `.pkg` oficial (decisão do revisor
    humano em 2026-10-21, como no Windows); a remoção não é automática (D-F7-4);
  - o estado global fica em `/var/db/dotnetscope` (dono root, sem escrita de
    outros); a assinatura exige Developer ID com o certificado de instalador da
    Microsoft (Team ID `UBF8T346G9`);
  - uma raiz do `.pkg` com ancestral de outro usuário (Homebrew em Macs Intel)
    gera aviso, não erro, porque o instalador oficial grava nela de qualquer forma;
    nesse caso o dotnetscope não executa o host dessa raiz como root (validação
    por arquivos) e não usa o atalho "já instalado";
  - o `installer` ignora SIGINT e a CLI aguarda até 31 minutos após Ctrl+C
    (`ProcessTerminationTimeout`), para registrar o resultado real dos
    instaladores oficiais (Windows e macOS);
  - o `stat` usa a sintaxe BSD no macOS (`-f '%u %Lp'`).
- Decisões de implementação da F7c:
  - no Windows, o escopo global usa somente o instalador oficial `.exe`; a
    estratégia `archive` em `%ProgramFiles%\dotnet` com `HKLM` foi descartada
    pelo revisor humano em 2026-10-21 (mais risco e código, benefício pequeno) e
    só volta se houver demanda;
  - o estado global fica em `%ProgramData%\dotnetscope` com ACL protegida
    (Administradores e SYSTEM); o instalador roda do staging nesse diretório,
    com `TEMP` apontando para ele, e fica aberto sem compartilhar escrita
    enquanto o hash e a assinatura são conferidos e ele executa;
  - a assinatura exige assinante `O=Microsoft Corporation`, sem verificação de
    revogação online.
- Decisões de implementação da F7b:
  - `--source auto` usa o `apt` somente quando o candidato é exatamente a versão
    que o metadata resolveria (por exemplo, `dotnet-runtime-10.0` 10.0.13 no
    Ubuntu 24.04); o SDK do Ubuntu (faixa 1xx) só é escolhido com `--source apt`;
  - instalações por pacote não recebem ambiente do dotnetscope (status
    `package`); a remoção usa `apt-get remove` apenas do pacote registrado;
  - os gerenciadores de pacotes recebem somente as variáveis de proxy do
    ambiente, necessárias em redes corporativas; os pacotes são verificados por
    assinatura pelo próprio gerenciador;
  - pacotes já instalados não são registrados; a remoção é simulada e recusada
    se levaria dependentes; registros apt casam pela versão instalada hoje;
  - `--install-deps` roda antes da resolução da versão, porque `ca-certificates`
    e OpenSSL são necessários para baixar o metadata, e cobre `apt`, `dnf` e `apk`.
- Decisões de implementação da F7a:
  - a F7 foi dividida em F7a a F7d (Linux por arquivo, `apt`, Windows e macOS)
    para validar cada sistema separadamente;
  - dono e permissões são lidos com o `stat` do sistema: o .NET não expõe o UID
    do dono, e um P/Invoke de `stat` dependeria do layout da struct por libc e
    arquitetura (e o símbolo nem é exportado na glibc 2.27 da base de build);
  - o escopo global não inclui `~/.dotnet/tools` no `PATH` (plano, seção 4.1);
  - `/usr/bin/dotnet` ou `/usr/local/bin/dotnet` apontando para outra raiz é
    tratado como conflito de ambiente (`--env-conflict`), o que cobre a mistura
    com o pacote da distribuição no `PATH` (R10, parcial; a detecção completa
    vem com o `apt` na F7b);
  - o manifesto global fica privado do root; para outros usuários, as
    instalações globais aparecem como `external`.
- Decisões de implementação da F6:
  - o `Path` do usuário recebe a raiz e as ferramentas no início, preservando o
    tipo existente; com `REG_EXPAND_SZ`, as ferramentas usam
    `%USERPROFILE%\.dotnet\tools`, a forma que o SDK grava na primeira execução,
    para que ele não duplique a entrada;
  - o estado (`%LOCALAPPDATA%\dotnetscope\env-state.json`) registra as entradas
    acrescentadas e os valores anteriores; a remoção restaura valor e tipo e não
    toca em valores que o usuário alterou depois;
  - um `dotnet` no `Path` da máquina tem precedência sobre o do usuário no
    Windows e não enxerga os SDKs da raiz do usuário: a CLI e o `doctor` avisam,
    sem tratar como conflito (código `0`), porque corrigir exige administrador;
  - `list --installed` inclui `%ProgramFiles%\dotnet` e classifica como
    `package` a raiz registrada pelo instalador oficial;
  - a execução como administrador não é recusada no escopo user do Windows;
  - os testes de registro usam uma subchave isolada em `HKCU\Software`; o E2E
    real, que altera `HKCU\Environment` e o `Path` da máquina, só roda em runner
    efêmero do GitHub.
- A configuração de ambiente global
  (`/etc/profile.d`, `/etc/paths.d`, `install_location`) na F7.
- O metadata oficial publica assinatura PKCS#7 destacada (campo `signature`,
  arquivo `.p7s`). Hoje a confiança no metadata vem do TLS e da allowlist;
  avaliar a verificação dessa assinatura (compatível com AOT) na F9.
- Canais antigos (1.0 e 2.0) trazem apenas SHA-256 ou nenhum hash em parte dos
  arquivos; esses artefatos são listados como indisponíveis e nunca resolvidos.
