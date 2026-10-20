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
- Hardening (F9): verificação Authenticode do instalador Windows e da assinatura
  do `.pkg` no macOS.

## 8. Extração e transação

- `System.Formats.Tar` + `GZipStream`; `System.IO.Compression` para zip.
- Rejeitar caminhos absolutos, `..`, symlinks/hardlinks para fora do destino,
  entradas de dispositivo e nomes duplicados; preservar bits de execução.
- Staging em `<raiz>/.dotnetscope-staging-<id>` e `rename` atômico por diretório
  de componente.
- Lock de instalação por raiz; journal `state.json`; `doctor --repair` remove
  staging órfão e recupera instalações parciais.
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
- Native AOT publicado no próprio SO em matriz de CI; Linux glibc compilado na
  base mais antiga suportada pelo .NET 10 (a confirmar na F5).
- Build local e no CI em `docker.io/lzocateli/dotnet-sdk:10.0.402-noble-v1`
  via `tools/scripts/Invoke-Validation.ps1`; com `-Aot`, `clang` e `zlib1g-dev`
  são instalados no container descartável (decisão da F0b; imagem própria com
  toolchain AOT pode ser criada na F5).
- Artefatos: `dotnetscope-<versão>-<rid>.tar.gz`/`.zip`, `SHA256SUMS`, assinatura,
  SBOM e atestação de proveniência (GitHub Attestations).
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

## 15. Fases

| Fase | Entrega | Critério de aceite | Estado |
| --- | --- | --- | --- |
| F0a | `.github`, `.githooks`, Gitleaks, plano, README, LICENSE | instruções e hook validados | concluída |
| F0b | `global.json`, `Directory.*.props`, `DotnetScope.slnx`, projetos base, `ExitCodes`, `tools/scripts/Invoke-Validation.ps1`, workflow `pr-validation` | build, testes e AOT `linux-x64` verdes em container e no CI | concluída localmente; CI pendente do primeiro PR |
| F1 | Parsing, cliente de metadata, cache, `list --available` | resolução correta com fixtures, incluindo `lts`/`sts`/feature band | pendente |
| F2 | `install` user Linux por arquivo, verificação, extração segura, manifesto, lock | SDK funcional em container limpo; casos maliciosos rejeitados | pendente |
| F3 | `--configure-env` (Linux/macOS), `env show\|apply\|remove`, detecção de conflito | casos obrigatórios da seção 12 verdes | pendente |
| F4 | `uninstall`, `list --installed`, `doctor [--repair]` | reversão completa e origem correta na listagem | pendente |
| F5 | Publicação AOT multi-RID, `install.sh`/`install.ps1`, release no GitHub | binários rodam sem .NET; checksums publicados | pendente |
| F6 | Windows user (zip + registro + `WM_SETTINGCHANGE`) | `dotnet` disponível em novo terminal; reversão do registro | pendente |
| F7 | Global: `apt`, instalador Windows, `.pkg`, instalação por arquivo (D4), `--install-deps` | instalação elevada validada; mistura com pacote recusada | pendente |
| F8 | macOS user | E2E em runner macOS | pendente |
| F9 | Assinatura, SBOM, Authenticode/`.pkg`, `.deb`/APT, winget, Homebrew | artefatos assinados e verificáveis | pendente |

Cada fase vira uma Issue (template `Tarefa técnica` ou `Propor funcionalidade`)
com milestone, e é entregue por PR com as evidências do template.

## 16. Riscos e questões em aberto

- Base glibc mínima do binário Linux e suporte a distribuições sem ICU.
- Disponibilidade e granularidade de versões nos feeds `apt` do Ubuntu.
- Comportamento de `install_location` e de `InstalledVersions` com instalações
  globais mistas.
- Shells não suportados na v1 (nushell, tcsh) apenas recebem instruções.
- Custo de manter repositório APT assinado e manifests winget/Homebrew.
