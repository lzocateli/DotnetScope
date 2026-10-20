---
name: "Configuração de ambiente e PATH"
description: "Use ao criar ou alterar a configuração de DOTNET_ROOT, PATH, arquivos de perfil de shell (bash, zsh, fish, PowerShell), registro do Windows, /etc/profile.d, /etc/paths.d, install_location ou o comando env do DotnetScope."
applyTo: "src/**/Environment/**, src/**/Profiles/**, src/**/Registry/**, tests/**/Environment/**"
---

# Configuração de ambiente e PATH

- `--configure-env` aceita `true|false` e tem padrão `true`. Com `false`, nenhuma escrita persistente ocorre: a CLI apenas imprime os comandos equivalentes.
- Variáveis gerenciadas: `DOTNET_ROOT` e entradas de `PATH` para a raiz do .NET e `~/.dotnet/tools`. `DOTNET_CLI_TELEMETRY_OPTOUT` só com `--telemetry-optout` (padrão `false`). Não adicione outras variáveis sem decisão registrada no plano.
- Linux/macOS (escopo `user`): o conteúdo fica em `~/.config/dotnetscope/env.sh` (e `env.fish`); os perfis recebem apenas um bloco que o carrega, entre `# >>> dotnetscope >>>` e `# <<< dotnetscope <<<`.
- Perfis-alvo são escolhidos pelos shells detectados: `~/.profile` e `~/.bashrc` (bash), `~/.zprofile` e `~/.zshrc` (zsh), `~/.config/fish/conf.d/dotnetscope.fish` (fish). Não crie perfil de shell inexistente, exceto `~/.profile` no Linux.
- Entradas de `PATH` usam guarda `case ":$PATH:" in *":<dir>:"*) ;; *) ... esac`, garantindo idempotência também em shells aninhados.
- Nunca edite fora dos marcadores próprios. Blocos de terceiros (por exemplo `# BEGIN ANSIBLE ...`, `dotnet-install`, `/etc/profile.d/dotnet.sh`) são apenas lidos para detectar conflito.
- Conflito: mesmo valor efetivo → nenhuma alteração; valor diferente → nenhuma alteração, relatório e código de saída de conflito, salvo `--env-conflict override|skip` explícito.
- Antes de editar, faça backup em `~/.local/state/dotnetscope/backups/<arquivo>.<timestamp>`; grave atomicamente, preserve permissões e final de linha; se o perfil for symlink, avise e exija confirmação antes de editar o alvo.
- Windows: use `HKCU\Environment` (user) ou `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` (global), preserve `REG_EXPAND_SZ`, não use `setx` e notifique com `WM_SETTINGCHANGE` via `LibraryImport`.
- Global Linux: `/etc/profile.d/dotnetscope.sh` e, para instalação por arquivo, `/etc/dotnet/install_location_<arch>`. Global macOS: `/etc/paths.d/dotnetscope`. Global Windows: também `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation` quando aplicável.
- Um processo filho não altera o shell pai: ao final, imprima `source ~/.config/dotnetscope/env.sh` ou `eval "$(dotnetscope env show --shell <shell>)"`.
- `env remove` e `uninstall` (quando não restar instalação gerenciada) removem apenas o bloco e os arquivos próprios, preservando o restante do perfil byte a byte.
- Valores escritos em perfis são escapados para o shell-alvo; caminhos com espaço, aspas, `$` ou crase não podem gerar execução de comando.
- Testes cobrem: aplicação dupla idempotente, `false` sem alteração (hash idêntico), conflito com bloco de terceiros, symlink, perfil inexistente, caminho com caracteres especiais, remoção que restaura o arquivo original e novo shell de login resolvendo `dotnet`. Execute-os somente em containers ou runners efêmeros.
