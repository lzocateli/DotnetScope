# Referência da CLI

Referência do comportamento **implementado** do `dotnetscope`. O comportamento
planejado das próximas fases está no [plano de implementação](plano-implementacao.md).

## Opções globais

| Opção | Descrição |
| --- | --- |
| `--verbose` | Exibe diagnóstico detalhado em stderr (requisições, uso do cache). |
| `--version` | Exibe a versão. |
| `-h`, `--help` | Exibe a ajuda do comando. |

## `list --available`

Consulta o metadata oficial de releases .NET
(`https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json`
e `releases.json` de cada canal).

```text
dotnetscope list --available [<versão>] [-c|--component <componente>]
                 [--prerelease] [--offline] [--output text|json]
```

| Opção | Padrão | Descrição |
| --- | --- | --- |
| `--available` sem valor | — | Lista os canais: tipo (`lts`/`sts`), fase, últimas versões e fim do suporte. |
| `--available <versão>` | — | Lista as versões do componente que correspondem à especificação e marca com `*` a que seria resolvida para o RID atual. |
| `-c`, `--component` | `sdk` | `sdk`, `runtime`, `aspnetcore` ou `windowsdesktop`. |
| `--prerelease` | `false` | Inclui canais `preview`/`go-live` e versões preview/RC. |
| `--offline` | `false` | Usa somente o cache local, sem acessar a rede. |
| `--output` | `text` | `text` ou `json`. |

### Especificação de versão

| Forma | Exemplo | Resolve para |
| --- | --- | --- |
| Exata | `10.0.400`, `11.0.100-rc.2.26504.105` | A própria versão; prerelease exige `--prerelease`. |
| Canal | `10.0` | Último patch do canal. |
| Feature band (só SDK) | `10.0.4xx` | Último patch da faixa. |
| Suporte | `lts`, `sts` | Canal mais recente do tipo em fase `active` ou `maintenance` (com `--prerelease`, também `preview`/`go-live`). |
| Compacta | `SDK-10.0.400`, `RUNTIME-10.0`, `ASPNETCORE-lts`, `WINDOWSDESKTOP-10.0` | Componente e versão em um único valor; conflita com `--component` diferente. |

A versão resolvida é a mais nova que corresponde à especificação e possui o
arquivo compactado oficial para o RID atual (`<prefixo>-<rid>.tar.gz`, ou `.zip`
no Windows) com hash SHA-512 no metadata. Versões cujo metadata traz apenas
SHA-256, ou nenhum hash, aparecem com arquivo `indisponível` e nunca são
resolvidas. Se o arquivo da versão escolhida estiver em origem fora da allowlist,
ele aparece como `origem recusada` e a resolução falha com código `5`, sem recorrer
a uma versão mais antiga. Canais `eol` são listados com aviso em stderr.

O RID é detectado do sistema: `linux-x64`, `linux-arm64`, `linux-musl-x64`
(Alpine), `osx-arm64`, `win-x64` etc.

### Saída JSON

Canais (`list --available --output json`):

```json
{
  "channels": [
    {
      "channel": "10.0",
      "releaseType": "lts",
      "supportPhase": "active",
      "latestRelease": "10.0.13",
      "latestRuntime": "10.0.13",
      "latestSdk": "10.0.402",
      "eolDate": "2028-11-14"
    }
  ]
}
```

Versões (`list --available 10.0.4xx --output json`):

```json
{
  "component": "sdk",
  "request": "10.0.4xx",
  "rid": "linux-x64",
  "channel": { "channel": "10.0", "releaseType": "lts", "supportPhase": "active", "...": "..." },
  "versions": [
    { "version": "10.0.402", "releaseVersion": "10.0.13", "releaseDate": "2026-10-13", "security": true, "archiveAvailable": true }
  ],
  "resolved": {
    "version": "10.0.402",
    "fileName": "dotnet-sdk-linux-x64.tar.gz",
    "url": "https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.402/dotnet-sdk-10.0.402-linux-x64.tar.gz",
    "sha512": "<128 caracteres hexadecimais minúsculos>"
  }
}
```

`resolved` é `null` quando nenhuma versão listada pode ser resolvida para o RID;
nesse caso o código de saída é `3`. Valores ausentes no metadata são `null`. As
chaves são contrato estável: remoções ou renomeações são mudanças incompatíveis.

## `install`

Instala um SDK ou runtime a partir do arquivo compactado oficial, verificado por
SHA-512. Nesta versão funciona no escopo `user`: `.tar.gz` no Linux e no macOS e
`.zip` no Windows. O escopo `global` está disponível no Linux
([Escopo global](#escopo-global-linux)); no Windows e no macOS retorna código `2`.

```text
dotnetscope install <componente> <versão> [opções]
dotnetscope install <COMPONENTE-versão> [opções]
dotnetscope install <componente> <versão-exata> --archive <arquivo.tar.gz|arquivo.zip> --sha512 <hash> [opções]
dotnetscope install --from-global-json [caminho] [opções]
```

| Opção | Padrão | Descrição |
| --- | --- | --- |
| `--scope` | `user` | `user` ou `global` (somente Linux nesta versão; exige root, ver [Escopo global](#escopo-global-linux)). |
| `--source` | `auto` | Estratégia do escopo global: `auto`, `archive` ou `apt` (ver [Estratégia apt](#estratégia-apt)). Com `--scope user`, `apt` é erro de uso. |
| `--install-deps` | `false` | Escopo global: instala as dependências nativas pelo gerenciador de pacotes antes de resolver a versão. Com `--scope user`, é erro de uso. |
| `--install-dir` | `~/.dotnet` (Windows: `%LOCALAPPDATA%\Microsoft\dotnet`) | Raiz da instalação; precisa ser absoluta. |
| `--prerelease` | `false` | Permite versões preview/RC. |
| `--offline` | `false` | Resolve a versão somente com o metadata em cache (o download continua exigindo rede). |
| `--dry-run` | `false` | Mostra o plano sem criar diretórios nem baixar. |
| `-y`, `--yes` | `false` | Confirma quando a versão resolvida difere da informada (canal, feature band, `lts`, `sts`). |
| `--allow-root` | `false` | Permite o escopo user executando como root. |
| `--archive` | — | Instala offline a partir de um `.tar.gz` local (`.zip` no Windows), sem consultar o metadata. Exige versão exata e `--sha512`. |
| `--sha512` | — | SHA-512 esperado do arquivo de `--archive` (128 caracteres hexadecimais). |
| `--from-global-json` | — | Instala o SDK exigido por um `global.json`. Aceita arquivo ou diretório; sem valor, procura a partir do diretório atual em direção à raiz. |
| `--configure-env` | `true` | Configura `DOTNET_ROOT` e `PATH` nos perfis ou, no Windows, em `HKCU\Environment` (seção [`env`](#env)); `false` só imprime as instruções. |
| `--env-conflict` | `fail` | `fail`, `override` ou `skip` quando terceiros definem outro `DOTNET_ROOT`. |
| `--telemetry-optout` | `false` | Inclui `DOTNET_CLI_TELEMETRY_OPTOUT=1` no ambiente. |
| `--output` | `text` | `text` ou `json`. |

Componentes e especificações de versão seguem as mesmas regras de
[`list --available`](#especificação-de-versão).

### Instalação offline (`--archive`)

O arquivo local é copiado para o staging privado e o SHA-512 é verificado na
cópia (código `5` se divergir), que então é extraída. Isso evita que o arquivo
original seja trocado entre a verificação e a extração. O metadata não é
consultado. Se o arquivo não contiver `sdk/<versão>` ou
`shared/<framework>/<versão>` correspondente ao pedido, a instalação é recusada
com código `5`. O manifesto registra a URL `file://` do arquivo.

### `global.json` (`--from-global-json`)

São lidos `sdk.version`, `sdk.rollForward` e `sdk.allowPrerelease`. Comentários
e vírgulas finais são aceitos. Sem `sdk.version`, a política é `latestMajor`; com
versão e sem `rollForward`, `latestPatch`. Prerelease só é considerado com
`"allowPrerelease": true` ou `--prerelease`.

| `rollForward` | SDK instalado (com arquivo para o RID) |
| --- | --- |
| `disable` | Exatamente a versão pedida |
| `patch` | A versão pedida; senão o último patch da mesma feature band |
| `latestPatch` | O último patch da mesma feature band (≥ pedido) |
| `feature` | O último patch da feature band pedida; senão o da próxima feature band do mesmo canal |
| `latestFeature` | A maior versão do mesmo canal (≥ pedido) |
| `minor` | Como `feature`; senão a menor feature band do próximo canal do mesmo major |
| `latestMinor` | A maior versão do mesmo major |
| `major` | Como `minor`, permitindo majors superiores |
| `latestMajor` | A maior versão disponível |

Se a versão escolhida diferir de `sdk.version`, a CLI pede confirmação ou
`--yes`, como nas demais versões não exatas.

### Comportamento

1. Recusa execução como root (código `7`) sem `--allow-root`; com ele, a raiz
   padrão é `/root/.dotnet`.
2. Valida `--install-dir`, inclusive após resolver links simbólicos. São
   recusados com código `7`: `/`, o próprio home, diretórios do sistema (`/usr`,
   `/opt`, `/etc`, `/var` etc.) e o home de outros usuários. Subdiretórios do
   próprio home são sempre aceitos, mesmo quando o home fica em um desses locais.
   As regras do Windows estão na [seção Windows](#windows); lá a execução como
   administrador não é recusada.
3. Com `--configure-env true`, planeja o ambiente antes de baixar: um conflito
   de `DOTNET_ROOT` com a política `fail` retorna código `6` sem alterar nada.
4. Resolve a versão. Se ela não for exata, pede confirmação no terminal ou exige
   `--yes`: sem terminal interativo, retorna código `2`; se o usuário recusar,
   código `130`.
5. Se `sdk/<versão>` ou `shared/<framework>/<versão>` já existe na raiz, de
   qualquer origem, informa que já está instalado e não baixa nada.
6. Obtém um lock exclusivo da raiz (`.dotnetscope.lock`). Se outra operação
   estiver em andamento, retorna código `6` após 30 segundos.
7. Baixa para um diretório temporário privado dentro da raiz
   (`.dotnetscope-staging-<id>`, modo `0700`), retomando com `Range` após falhas
   transitórias, e verifica o SHA-512 antes de extrair (código `5` se divergir).
8. Extrai aceitando somente diretórios e arquivos regulares com caminhos
   relativos confinados. Links, dispositivos, caminhos absolutos, `..`, nomes
   duplicados e arquivos acima dos limites são recusados com código `5`. No
   `.zip`, também são recusados letras de unidade, `\`, `:` (fluxos alternativos
   do NTFS), nomes reservados do Windows (`CON`, `NUL`, `COM1` etc.), segmentos
   terminados em ponto ou espaço, nomes que diferem só em maiúsculas, entradas
   criptografadas e tamanhos reais acima do declarado.
9. Move para a raiz apenas as pastas versionadas e os arquivos ausentes, sem
   substituir nada existente. O executável `dotnet` só é atualizado quando o
   `host/fxr` do arquivo é mais novo que todos os da raiz. Falhas desfazem o que
   foi movido. No Windows, renomeações bloqueadas por antivírus são repetidas
   por até 3 s. Ctrl+C cancela o download e a extração e remove o staging; depois
   que a mesclagem começa (operação de milissegundos), ela é concluída.
10. Registra a instalação no manifesto e valida o host com `dotnet --list-sdks`
    ou `--list-runtimes`.
11. Se a validação passou e `--configure-env` é `true`, aplica o ambiente e
    verifica um shell de login novo.

Bibliotecas nativas ausentes (ICU, OpenSSL, libstdc++, libgcc_s, certificados de
CA) geram avisos antes da instalação. Se a validação falhar, os arquivos
permanecem instalados e o código é `8` quando há dependências ausentes, ou `1`
nos demais casos.

Ao final, a CLI mostra como usar o .NET na sessão atual: `. '<env.sh>'` com o
ambiente configurado ou, com `--configure-env false`, os comandos:

```bash
export DOTNET_ROOT='/home/usuario/.dotnet'
export PATH='/home/usuario/.dotnet':'/home/usuario/.dotnet/tools':"$PATH"
```

As ferramentas globais (`dotnet tool install -g`) ficam sempre em
`~/.dotnet/tools`, mesmo com `--install-dir`.

### Saída JSON do `install`

```json
{
  "status": "installed",
  "component": "sdk",
  "request": "lts",
  "version": "10.0.402",
  "rid": "linux-x64",
  "scope": "user",
  "installDir": "/home/usuario/.dotnet",
  "artifact": { "fileName": "dotnet-sdk-linux-x64.tar.gz", "url": "https://builds.dotnet.microsoft.com/...", "sha512": "..." },
  "addedPaths": ["sdk/10.0.402", "shared/Microsoft.NETCore.App/10.0.13", "..."],
  "sharedPaths": [],
  "hostReplaced": false,
  "validation": { "succeeded": true, "detail": "dotnet --list-sdks lista 10.0.402" },
  "missingDependencies": [],
  "environment": { "dotnetRoot": "/home/usuario/.dotnet", "status": "applied", "...": "mesmo formato de env apply" }
}
```

`status` é `installed`, `alreadyInstalled` ou `planned` (`--dry-run`).
`addedPaths` são as pastas criadas por esta instalação; `sharedPaths`, as que o
arquivo contém, mas já existiam. `validation` é `null` em `--dry-run`.

## `env`

Configura `DOTNET_ROOT` e `PATH` nos perfis do usuário no Linux e no macOS e nas
variáveis do usuário no Windows ([seção Windows](#windows)). O `install` executa
o mesmo fluxo por padrão (`--configure-env true`).

```text
dotnetscope env show   [--shell posix|sh|bash|zsh|fish|pwsh|powershell|cmd] [--install-dir <dir>] [--telemetry-optout]
                       [--scope user|global]
dotnetscope env apply  [--install-dir <dir>] [--env-conflict fail|override|skip] [--telemetry-optout]
                       [-y|--yes] [--dry-run] [--output text|json]
dotnetscope env remove [--dry-run] [--output text|json] [--scope user|global]
```

- `env show` só imprime o script, sem gravar nada. É útil para a sessão atual:
  `eval "$(dotnetscope env show)"` ou, no PowerShell (padrão no Windows),
  `dotnetscope env show | Out-String | Invoke-Expression`. Com `--shell cmd`,
  caminhos com `%`, `!`, `^` ou `"` são recusados (código `2`).
- `env apply` exige `dotnet` (`dotnet.exe` no Windows) na raiz (código `3` se não houver).
- `env remove` retira o bloco e os scripts próprios, restaurando os perfis byte
  a byte. Um perfil que o DotnetScope criou é apagado se ficar vazio.

### O que é gravado

| Arquivo | Conteúdo |
| --- | --- |
| `~/.config/dotnetscope/env.sh` (ou `$XDG_CONFIG_HOME`) | `DOTNET_ROOT` e entradas de `PATH` (raiz e `~/.dotnet/tools`) com guardas `case ":$PATH:"`, mais `DOTNET_CLI_TELEMETRY_OPTOUT=1` com `--telemetry-optout`. |
| `~/.config/fish/conf.d/dotnetscope.fish` | Equivalente para o fish, quando ele está instalado ou `~/.config/fish` existe. |
| `~/.profile`, `~/.bash_profile`, `~/.bash_login`, `~/.bashrc`, `~/.zprofile`, `~/.zshrc` | Bloco que carrega o `env.sh`, somente nos arquivos que já existem. Exceções: `~/.profile` é criado no Linux e `~/.zprofile` no macOS. |

O bloco gravado nos perfis é:

```sh
# >>> dotnetscope >>>
# Gerenciado por dotnetscope. Use `dotnetscope env remove` para remover.
if [ -f "$HOME"/'.config/dotnetscope/env.sh' ]; then . "$HOME"/'.config/dotnetscope/env.sh'; fi
# <<< dotnetscope <<<
```

### Garantias

- Nada fora dos marcadores é alterado. Aplicar duas vezes não modifica nenhum
  arquivo e não gera novos backups.
- Os valores usam aspas simples: caminhos com espaço, aspas, `$` ou crase nunca
  viram código.
- Cada arquivo editado ganha um backup em
  `~/.local/state/dotnetscope/backups/<arquivo>.<data>` (modo `0600`). A gravação
  é atômica e preserva permissões.
- Perfis que não estão em UTF-8 ou com bloco malformado não são editados
  (código `6`).
- Se um perfil for alterado entre o planejamento e a gravação, nada é gravado
  (código `6`).
- Um perfil que é link simbólico (dotfiles) é editado no alvo, mantendo o link.
  Isso exige confirmação no terminal ou `--yes`; sem terminal, retorna código
  `2`. `--dry-run` nunca pede confirmação.
- Com `--configure-env false`, nenhum arquivo é tocado; a CLI só imprime os
  `export` equivalentes.
- O bloco usa sempre LF, mesmo em perfis com CRLF, para continuar
  interpretável pelo shell.

### Conflitos com terceiros

Atribuições de `DOTNET_ROOT` fora do bloco são procuradas nos perfis, em
`~/.zshenv`, em `~/.config/fish/config.fish`, em `/etc/environment`, em
`/etc/profile` e em `/etc/profile.d/*.sh`. Um exemplo é o bloco
`# BEGIN ANSIBLE USER DOTNET`. Valores literais, `$HOME`, `${HOME}` e `~` são
avaliados; qualquer outra expansão conta como valor diferente.

| Situação | `--env-conflict fail` (padrão) | `override` | `skip` |
| --- | --- | --- | --- |
| Mesmo valor efetivo | Aplica normalmente | Aplica | Aplica |
| Valor diferente | Código `6`, nada alterado; no `install`, antes do download | Aplica e move o bloco para o fim dos perfis | Não altera nada; aviso e código `0` |

Arquivos de terceiros são apenas lidos; links quebrados neles são ignorados.

### Verificação

Depois de aplicar, a CLI abre um shell de login novo (o `$SHELL` do usuário ou,
na ausência, o de `/etc/passwd`), com ambiente mínimo e prazo de 20 s, para
confirmar que `dotnet` resolve para a raiz configurada. Se resolver para outro
lugar, ou se houver `dotnet` do sistema em `/usr/bin`, a CLI emite um aviso. O
shell atual não muda: use o comando `. <env.sh>` impresso ao final.

### Saída JSON do `env`

```json
{
  "action": "apply",
  "environment": {
    "dotnetRoot": "/home/usuario/.dotnet",
    "pathEntries": ["/home/usuario/.dotnet", "/home/usuario/.dotnet/tools"],
    "status": "applied",
    "script": "/home/usuario/.config/dotnetscope/env.sh",
    "changedFiles": ["/home/usuario/.config/dotnetscope/env.sh", "/home/usuario/.profile"],
    "backups": [],
    "conflicts": [],
    "loginShell": { "shell": "/bin/bash", "dotnet": "/home/usuario/.dotnet/dotnet", "dotnetRoot": "/home/usuario/.dotnet", "matches": true }
  }
}
```

Valores de `status`:

- `applied`;
- `unchanged`;
- `planned` (`--dry-run`);
- `skipped` (`--env-conflict skip`);
- `removed` (`env remove`);
- no `install`, também `disabled` (`--configure-env false`), `notApplied`
  (validação do host falhou) e `package` (estratégia apt: o ambiente é do pacote).

O objeto `environment` da saída JSON do `install` tem o mesmo formato.

### Windows

No Windows, `env apply` (e o `install` com `--configure-env true`) grava nas
variáveis persistentes do usuário, em `HKCU\Environment`, e avisa as janelas
abertas com `WM_SETTINGCHANGE`. `setx` nunca é usado, porque trunca valores em
1024 caracteres.

| Variável | Conteúdo |
| --- | --- |
| `DOTNET_ROOT` | Raiz da instalação (`REG_SZ`). |
| `Path` | Raiz e diretório de ferramentas acrescentados no início, somente se ausentes. O tipo existente é preservado; um `Path` novo é `REG_EXPAND_SZ`. Com `REG_EXPAND_SZ`, as ferramentas usam `%USERPROFILE%\.dotnet\tools`, a mesma forma que o SDK grava na primeira execução. |
| `DOTNET_CLI_TELEMETRY_OPTOUT` | `1` com `--telemetry-optout`; sem a opção, um valor gravado antes pelo DotnetScope volta ao anterior. |

Garantias:

- Entradas existentes do `Path` são comparadas expandidas, sem distinção de
  maiúsculas e sem barra final: nada é duplicado e entradas vazias são mantidas.
- Antes de gravar, os valores anteriores vão para
  `%LOCALAPPDATA%\dotnetscope\backups\environment.<data>.json`. O estado em
  `%LOCALAPPDATA%\dotnetscope\env-state.json` registra o que o DotnetScope
  acrescentou e os valores anteriores.
- `env remove` (e o `uninstall` da última instalação gerenciada na raiz) retira
  do `Path` somente as entradas acrescentadas e restaura `DOTNET_ROOT` e
  `DOTNET_CLI_TELEMETRY_OPTOUT` ao valor e tipo anteriores. Valores alterados
  pelo usuário depois disso não são tocados. Um `Path` criado pelo DotnetScope é
  apagado se ficar vazio.
- Se uma variável mudar entre o planejamento e a gravação, nada é gravado
  (código `6`). Um valor de tipo diferente de `REG_SZ`/`REG_EXPAND_SZ` também
  retorna código `6`.
- `DOTNET_ROOT` do usuário com outro valor segue a tabela de
  [conflitos](#conflitos-com-terceiros); o conflito é reportado com
  `file` = `HKCU\Environment\DOTNET_ROOT` e `line` = `0`.
- `changedFiles` lista os valores alterados (por exemplo,
  `HKCU\Environment\Path`) e `script` é `null`.

Verificação: o Windows monta o `Path` de um terminal novo com o `Path` da
máquina seguido do `Path` do usuário. A CLI faz o mesmo cálculo a partir do
registro e informa em `loginShell` para onde `dotnet` resolve. Se um `dotnet`
do `Path` da máquina, como `C:\Program Files\dotnet`, tiver precedência, a CLI
avisa: o `dotnet.exe` da máquina não enxerga os SDKs da raiz do usuário. Nesse
caso, remova a entrada do `Path` da máquina (exige administrador). O `doctor`
reporta a mesma situação (`new-terminal`). Terminais já abertos não mudam; use o
comando de `env show` impresso ao final.

`--install-dir` no Windows recusa a raiz de uma unidade, o próprio perfil,
caminhos de rede (`\\servidor\...`), `%WINDIR%`, `%ProgramFiles%`,
`%ProgramFiles(x86)%`, `%ProgramData%` e perfis de outros usuários, inclusive
por junção ou link simbólico (código `7`).

## `list --installed`

```text
dotnetscope list --installed [--install-dir <dir>] [--output text|json]
```

Lista SDKs e runtimes encontrados nas raízes abaixo, com a origem de cada um:

- a raiz do usuário (`~/.dotnet`, `%LOCALAPPDATA%\Microsoft\dotnet` no Windows,
  ou `--install-dir`);
- as raízes registradas no manifesto;
- as raízes do sistema (`/usr/lib/dotnet`, `/usr/share/dotnet`,
  `/usr/local/share/dotnet`, `/usr/lib64/dotnet` e `/opt/dotnet`; no Windows,
  `%ProgramFiles%\dotnet` e `%ProgramFiles(x86)%\dotnet`).

| Origem | Significado |
| --- | --- |
| `dotnetscope` | Instalado pelo DotnetScope. Pode ter registro próprio ou ter chegado com outra instalação; nesse caso, `managedBy` mostra qual, por exemplo `sdk 10.0.402`. |
| `external` | Instalado por outra ferramenta (`dotnet-install`, Ansible, IDE) ou em raiz que não pertence a pacote do sistema. |
| `package` | Em raiz cujo `dotnet` pertence a um pacote do sistema, conforme `dpkg-query -S` ou `rpm -qf`; no Windows, raiz registrada pelo instalador oficial em `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation`. |

Registros do manifesto cujos arquivos não existem aparecem com `present: false`;
use `dotnetscope doctor`. É uma operação somente leitura.

```json
{ "installations": [ { "component": "sdk", "version": "10.0.402", "origin": "dotnetscope", "managedBy": null, "root": "/home/usuario/.dotnet", "present": true } ] }
```

## `uninstall`

```text
dotnetscope uninstall <componente> <versão> [--scope user|global] [--install-dir <dir>] [--force] [--keep-env]
                      [--dry-run] [--allow-root] [--output text|json]
dotnetscope uninstall <COMPONENTE-versão> [opções]
```

A versão precisa ser exata (código `2` caso contrário). Sob o lock da raiz, a
remoção segue estas regras:

- Apaga somente pastas criadas pelo DotnetScope (`addedPaths` de alguma
  instalação registrada) que nenhuma outra instalação registrada na mesma raiz
  contém.
- Pastas ainda usadas por outra instalação permanecem e passam a pertencer a
  ela, de modo que a última remoção apaga tudo o que o DotnetScope criou.
  Exemplos: o runtime e o `host/fxr` compartilhados por dois SDKs.
- Pastas que já existiam antes da instalação nunca são removidas. O mesmo vale
  para arquivos de usuário que o SDK cria em `~/.dotnet` (que também é o
  `DOTNET_CLI_HOME`).
- As pastas vão primeiro para uma lixeira no staging, com journal gravado antes
  do primeiro movimento. Se a atualização do manifesto falhar, tudo é restaurado
  (código `1`).
- Componente sem registro próprio (instalado por outra ferramenta) exige
  `--force` (código `6` sem ele) e remove só `sdk/<v>` ou
  `shared/<framework>/<v>`. Se a pasta fizer parte de uma instalação gerenciada,
  como o runtime trazido por um SDK, a remoção é recusada mesmo com `--force`.
- Componente ausente retorna código `3`.
- Quando não resta instalação gerenciada na raiz e o ambiente aponta para ela, o
  bloco de `DOTNET_ROOT`/`PATH` sai dos perfis, como em `env remove`. Use
  `--keep-env` para mantê-lo.

```json
{ "status": "removed", "component": "sdk", "version": "10.0.402", "root": "/home/usuario/.dotnet", "origin": "dotnetscope",
  "removedPaths": ["sdk/10.0.402"], "keptPaths": [ { "path": "shared/Microsoft.NETCore.App/10.0.13", "usedBy": "sdk 10.0.113" } ],
  "environment": "unchanged" }
```

## `doctor`

```text
dotnetscope doctor [--repair] [--scope user|global] [--install-dir <dir>] [--output text|json]
```

Verifica a raiz do usuário e as raízes do manifesto, cada uma sob o seu lock.

| Código | Severidade | Situação | Com `--repair` |
| --- | --- | --- | --- |
| `orphan-staging` | aviso | Diretório temporário sem conteúdo da raiz, ou de operação concluída | Apagado (restaura o host se ele faltar) |
| `interrupted-install` | erro | Instalação interrompida antes do registro no manifesto | Apaga as pastas movidas que ninguém usa e restaura o host do backup |
| `interrupted-uninstall` | erro | Remoção interrompida | Devolve à raiz as pastas da lixeira se o registro ainda existe; senão, só limpa |
| `unknown-staging` | erro | Staging com lixeira ou backup, mas sem journal | Não é alterado; verifique manualmente |
| `invalid-journal` | erro | Journal com caminhos fora da raiz | Não é alterado |
| `stale-record` | erro | Registro no manifesto sem a pasta do componente | Remove o registro, transfere a posse das pastas ainda usadas e apaga as que ninguém usa |
| `missing-files` | aviso | Pastas registradas que não existem mais | — |
| `host-validation` | erro | `dotnet --list-sdks`/`--list-runtimes` não lista o componente | — |
| `root-busy` | aviso | Outra operação está em andamento na raiz | — |
| `env-script-missing`, `env-profile-missing`, `env-root-missing`, `login-shell` | aviso | Inconsistências do ambiente | — (use `env apply` ou `env remove`) |
| `native-dependency` | aviso | Biblioteca nativa ausente | — |

A decisão sobre uma instalação interrompida usa a identidade gravada no journal
(componente e versão): se o registro existe, a instalação foi concluída. O
backup do host é sempre lido de `<staging>/backup/dotnet`, nunca de um caminho
do journal.

O código de saída é `0` quando não restam erros (ou todos foram corrigidos) e
`6` quando há erros não corrigidos.

```json
{ "healthy": false, "findings": [ { "code": "stale-record", "severity": "error", "message": "...", "repairable": true, "repaired": false } ] }
```

## Escopo global (Linux)

`--scope global` instala para todos os usuários da máquina. Os requisitos vêm do
[modelo de ameaças da F7](modelo-de-ameacas-f7.md). As estratégias são `archive`
(instalação por arquivo, descrita nesta seção) e [`apt`](#estratégia-apt); Windows
e macOS chegam nas próximas etapas da F7.

```bash
sudo dotnetscope install sdk lts --scope global --yes
sudo dotnetscope uninstall sdk 10.0.402 --scope global
dotnetscope env show --scope global      # não exige root
sudo dotnetscope doctor --scope global --repair
```

| Item | Caminho |
| --- | --- |
| Raiz padrão | `/usr/local/share/dotnet` (`--install-dir` para outra) |
| Ambiente de todos os shells de login | `/etc/profile.d/dotnetscope.sh`: `DOTNET_ROOT` e a raiz no início do `PATH`, com guarda contra duplicação |
| Localização do runtime para o host .NET | `/etc/dotnet/install_location_<arch>` |
| Manifesto, estado do ambiente, backups e lock | `/var/lib/dotnetscope` (dono root, arquivos `0600`) |
| Cache de metadata | `/var/cache/dotnetscope/metadata` |

Garantias:

- Exige root (código `7` sem ele). O dotnetscope nunca chama `sudo`.
- Os caminhos acima são fixos: `HOME` e `XDG_*` herdados de quem elevou não são
  usados, e o cache do usuário nunca é lido.
- A raiz e todos os seus ancestrais, antes e depois de resolver links, precisam
  pertencer ao root e não aceitar escrita de grupo ou outros (código `7`). A
  mesma regra vale para `/var/lib/dotnetscope`, `/var/cache/dotnetscope`,
  `/etc/profile.d` e `/etc/dotnet`. A raiz nova nasce com modo `0755`,
  independentemente do `umask`.
- `--install-dir` recusa diretórios do sistema (`/`, `/usr`, `/opt`, `/etc`,
  `/proc` etc., código `7`) e as raízes dos pacotes da distribuição
  (`/usr/lib/dotnet`, `/usr/share/dotnet`, `/usr/lib64/dotnet`, código `6`).
- São conflito (política `--env-conflict`, padrão código `6` sem alterar nada,
  antes do download): `DOTNET_ROOT` com outro valor em `/etc/environment`,
  `/etc/profile` ou `/etc/profile.d/*.sh`; `install_location` de terceiro com
  outra raiz; e `/usr/bin/dotnet` ou `/usr/local/bin/dotnet` apontando para
  outra raiz (por exemplo, o pacote `dotnet-sdk-*` da distribuição).
- `/etc/profile.d/dotnetscope.sh` e `install_location` são gravados de forma
  atômica, com dono root e modo `0644`, após backup em
  `/var/lib/dotnetscope/backups`. Se algum deles for link simbólico, nada é
  gravado (código `6`).
- `env remove --scope global` e o `uninstall` da última instalação gerenciada
  restauram o conteúdo anterior exato desses arquivos; um arquivo alterado por
  terceiros depois disso não é tocado. O diretório `/etc/dotnet`, se criado,
  permanece vazio.
- O manifesto global é privado do root: `list --installed` executado por outro
  usuário mostra as instalações globais com origem `external`.
- `--prerelease`, `--archive`, `--from-global-json`, `--dry-run` e `--offline`
  funcionam como no escopo user. O diretório de ferramentas (`~/.dotnet/tools`)
  não entra no `PATH` global.

### Estratégia apt

| `--source` | Comportamento |
| --- | --- |
| `auto` (padrão) | Usa o `apt` somente se o candidato das fontes configuradas for **exatamente** a versão que o metadata resolveria; caso contrário, `archive`. Com `--install-dir`, ou com o ambiente global apontando para uma instalação por arquivo, usa sempre `archive`. O resultado nunca difere do da estratégia `archive`. |
| `apt` | Usa o candidato do pacote do canal (`dotnet-sdk-<canal>`, `dotnet-runtime-<canal>`, `aspnetcore-runtime-<canal>`), respeitando versão exata ou feature band quando pedidas. Sem candidato compatível, código `3`; com `--install-dir` ou `--archive`, código `2`. |
| `archive` | Sempre instala por arquivo em `/usr/local/share/dotnet` (ou `--install-dir`). |

```bash
sudo dotnetscope install runtime 10.0 --scope global --yes          # auto: apt se tiver 10.0.<último patch>
sudo dotnetscope install sdk 10.0 --scope global --source apt --yes # SDK do pacote da distribuição (por exemplo, 10.0.1xx)
sudo dotnetscope install sdk lts --scope global --install-deps --yes
```

- Usa somente as fontes apt já configuradas: o dotnetscope não executa
  `apt-get update` e não adiciona repositório nem chave. Se o pacote não
  aparecer, execute `apt-get update` antes.
- `apt-get`, `apt-cache`, `dnf` e `apk` são executados pelo caminho absoluto, sem
  shell, com ambiente fixo (somente as variáveis de proxy são repassadas; os
  gerenciadores verificam as assinaturas dos pacotes) e prazo. `APT_CONFIG`,
  `LD_PRELOAD` e o `PATH` de quem elevou não têm efeito. Nomes e versões de
  pacote são validados por expressão regular antes de qualquer uso.
- A raiz é a do pacote: `/usr/lib/dotnet` (Ubuntu) ou `/usr/share/dotnet`
  (packages.microsoft.com), lida do destino real de `/usr/bin/dotnet`. O
  dotnetscope não grava ambiente (`environment.status` = `package`).
- Um pacote já instalado na mesma versão é informado como `alreadyInstalled` e
  não é registrado: ele pertence a quem o instalou. Em outra versão, código `6`
  sem alterar nada.
- O manifesto global registra a instalação com `artifact.url` = `apt:<pacote>`.
  `uninstall --scope global` (sem `--install-dir`) casa o pedido com a versão
  instalada hoje, inclusive depois de um `apt upgrade`, e executa `apt-get
  remove` somente desse pacote. A remoção é simulada antes: se o apt também
  removeria pacotes que dependem dele (por exemplo, o SDK ao remover o runtime),
  nada é removido e a CLI retorna código `6`. Dependências instaladas pelo apt
  permanecem. O `doctor` não cria lock nem repara nada em raízes de pacote.
- Não mistura estratégias: `--source apt` com o ambiente global apontando para
  uma instalação por arquivo, ou `archive` com `/usr/bin/dotnet` de pacote,
  retorna código `6`.
- `--install-deps` instala uma tabela fixa por gerenciador: `apt` (`ca-certificates`,
  `libc6`, `libgcc-s1`, `libstdc++6`, `zlib1g`, a primeira `libicu<N>` e a
  primeira `libssl3t64`/`libssl3`/`libssl1.1` disponíveis), `dnf` (`ca-certificates`,
  `glibc`, `libgcc`, `libstdc++`, `zlib`, `libicu`, `openssl-libs`) ou `apk`
  (`ca-certificates`, `libgcc`, `libstdc++`, `zlib`, `icu-libs`, `libssl3`). Não
  roda em `--dry-run`.

## Manifesto de instalações

| SO | Arquivo |
| --- | --- |
| Linux/macOS | `$XDG_DATA_HOME/dotnetscope/manifest.json` (se absoluto) ou `~/.local/share/dotnetscope/manifest.json` |
| Windows | `%LOCALAPPDATA%\dotnetscope\manifest.json` |

O manifesto registra componente, versão, RID, escopo, raiz, URL, SHA-512, data,
pastas do arquivo (`paths`) e pastas criadas (`addedPaths`). É gravado com modo
`0600`, de forma atômica e sob lock entre processos. Um manifesto corrompido
nunca é sobrescrito: a instalação é desfeita e a CLI retorna código `1`.

## Cache do metadata

| SO | Diretório |
| --- | --- |
| Linux/macOS | `$XDG_CACHE_HOME/dotnetscope/metadata` (se absoluto) ou `~/.cache/dotnetscope/metadata` |
| Windows | `%LOCALAPPDATA%\dotnetscope\cache\metadata` |

- O diretório é criado com modo `0700` e os arquivos com `0600`; a gravação é atômica.
- Dentro de 1 hora o cache é usado sem acessar a rede; depois disso o metadata é
  baixado novamente.
- Se o download falhar (rede, HTTP, metadata malformado), o cache expirado é usado
  com aviso em stderr e permanece intacto.
- Se o cache não puder ser gravado (diretório somente leitura, por exemplo), o
  comando continua com o metadata baixado e emite aviso.
- `--offline` usa o cache de qualquer idade e falha com código `4` se o documento
  necessário não estiver em cache.

## Rede e segurança

- Somente HTTPS na porta padrão para `builds.dotnet.microsoft.com` e
  `download.visualstudio.microsoft.com`; outras origens, inclusive em redirects ou
  em URLs do próprio metadata (`releases.json` e arquivos), são recusadas com
  código `5`. Arquivos de canais EOL hospedados em `download.microsoft.com`
  (1.x e 2.x) são, portanto, recusados.
- Proxy por `HTTPS_PROXY`/`HTTP_PROXY`/`NO_PROXY`. Em redes com inspeção TLS,
  exponha a CA corporativa por `SSL_CERT_DIR` ou `SSL_CERT_FILE`.
- Até 3 tentativas com backoff para falhas transitórias (conexão, HTTP 408, 429 e
  5xx, conexão interrompida e prazo de 60 s por tentativa, que inclui a leitura do
  corpo); respostas acima de 64 MiB são recusadas.

## Códigos de saída

| Código | Significado |
| --- | --- |
| `0` | Sucesso. |
| `1` | Erro inesperado (detalhes com `--verbose`). |
| `2` | Uso inválido: opção, versão ou componente inválido, prerelease exata sem `--prerelease`, escopo global, confirmação ausente sem terminal. |
| `3` | Canal, versão ou artefato inexistente para o RID; plataforma ainda não suportada pelo `install`. |
| `4` | Falha de rede, metadata indisponível ou malformado, ou ausente no cache com `--offline`. |
| `5` | Integridade: origem fora da allowlist (metadata, redirect ou arquivo resolvido), SHA-512 divergente ou arquivo recusado na extração. |
| `6` | Conflito: `doctor` com erros não corrigidos, `uninstall` de componente externo sem `--force` ou de pasta usada por instalação gerenciada, outra operação em andamento, item existente com tipo incompatível, `DOTNET_ROOT` de terceiros com outro valor, bloco malformado, perfil não UTF-8 ou alterado durante a operação. |
| `7` | Privilégio inadequado: escopo user como root ou `--install-dir` em local do sistema ou de outro usuário. |
| `8` | Instalação gravada, mas a validação falhou com dependências nativas ausentes. |
| `130` | Cancelado pelo usuário (Ctrl+C ou confirmação recusada). |
