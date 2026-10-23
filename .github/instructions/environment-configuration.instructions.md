---
name: "Configuração de ambiente e PATH"
description: "Use ao criar ou alterar a configuração de DOTNET_ROOT, PATH, arquivos de perfil de shell (bash, zsh, fish, PowerShell), registro do Windows, /etc/profile.d, /etc/paths.d, install_location ou o comando env do DotnetScope."
applyTo: "src/**/UserEnvironment/**, src/**/Registry/**, tests/**/*Environment*"
---

# Configuração de ambiente e PATH

- `--configure-env` aceita `true|false` e tem padrão `true`. Com `false`, a CLI não altera o ambiente persistente; a instalação ainda grava seus próprios arquivos. Em estratégias oficiais globais, o ambiente é responsabilidade do instalador da Microsoft.
- Variáveis gerenciadas: `DOTNET_ROOT` e entradas de `PATH` para a raiz do .NET e `~/.dotnet/tools`. `DOTNET_CLI_TELEMETRY_OPTOUT` só com `--telemetry-optout` (padrão `false`). Não adicione outras variáveis sem decisão registrada no plano.
- Linux/macOS (escopo `user`): o conteúdo fica em `~/.config/dotnetscope/env.sh` e `~/.config/fish/conf.d/dotnetscope.fish`; os perfis recebem apenas um bloco que o carrega, entre `# >>> dotnetscope >>>` e `# <<< dotnetscope <<<`.
- Perfis existentes incluem `~/.profile`, `~/.bash_profile`, `~/.bash_login`, `~/.bashrc`, `~/.zprofile` e `~/.zshrc`. As exceções de criação e a detecção de fish seguem a [referência da CLI](../../docs/cli.md#o-que-é-gravado), inclusive `~/.zprofile` no macOS e `~/.profile` para bash de login.
- Entradas de `PATH` usam guarda `case ":$PATH:" in *":<dir>:"*) ;; *) ... esac`, garantindo idempotência também em shells aninhados.
- Nunca edite fora dos marcadores próprios. Blocos de terceiros (por exemplo `# BEGIN ANSIBLE ...`, `dotnet-install`, `/etc/profile.d/dotnet.sh`) são apenas lidos para detectar conflito.
- Conflito: mesmo valor efetivo → aplicação normal do bloco próprio, com guardas contra duplicação; valor diferente → nenhuma alteração, relatório e código de saída de conflito, salvo `--env-conflict override|skip` explícito.
- Antes de editar, faça backup em `~/.local/state/dotnetscope/backups/<arquivo>.<timestamp>`; grave atomicamente, preserve permissões e final de linha; se o perfil for symlink, avise e exija confirmação antes de editar o alvo.
- Windows user: use `HKCU\Environment`, preserve o tipo existente do `Path`, não use `setx` e notifique com `WM_SETTINGCHANGE` via `LibraryImport`. Variáveis e tipos seguem a [CLI](../../docs/cli.md#windows). Falha de escrita no registro retorna código `7` e sugere o destino `profile`.
- Windows user com `--windows-env-target profile` (D7 do plano): nunca grave no registro. O conteúdo fica em `%LOCALAPPDATA%\dotnetscope\env.ps1` (UTF-8 com BOM) e os perfis `profile.ps1` do Windows PowerShell e do PowerShell 7 recebem só o bloco ASCII que o carrega, com as mesmas garantias de marcadores, backup e remoção. Respeite a política de execução efetiva e siga a [CLI](../../docs/cli.md#windows-sem-registro---windows-env-target-profile).
- Global Linux por arquivo: `/etc/profile.d/dotnetscope.sh` e `/etc/dotnet/install_location_<arch>`; por `apt`, o ambiente é do pacote. Global Windows e macOS: ambiente do instalador oficial, sem edição própria de `HKLM` ou `/etc/paths.d` e sem `env apply` global.
- Um processo filho não altera o shell pai: ao final, imprima `source ~/.config/dotnetscope/env.sh` ou `eval "$(dotnetscope env show --shell <shell>)"`.
- `env remove` e `uninstall` (quando não restar instalação gerenciada) removem apenas o bloco e os arquivos próprios, preservando o restante do perfil byte a byte.
- Valores escritos em perfis são escapados para o shell-alvo; caminhos com espaço, aspas, `$` ou crase não podem gerar execução de comando.
- Testes cobrem: aplicação dupla idempotente, `false` sem alteração (hash idêntico), conflito com bloco de terceiros, symlink, perfil inexistente, caminho com caracteres especiais, remoção que restaura o arquivo original e novo shell de login resolvendo `dotnet`. Execute-os somente em containers ou runners efêmeros.
