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
SHA-512. Nesta versão funciona no escopo `user` no Linux e no macOS (`.tar.gz`);
o escopo `global` e o Windows retornam erro.

```text
dotnetscope install <componente> <versão> [opções]
dotnetscope install <COMPONENTE-versão> [opções]
```

| Opção | Padrão | Descrição |
| --- | --- | --- |
| `--scope` | `user` | `user`; `global` ainda não está disponível (código `2`). |
| `--install-dir` | `~/.dotnet` | Raiz da instalação; precisa ser absoluta. |
| `--prerelease` | `false` | Permite versões preview/RC. |
| `--offline` | `false` | Resolve a versão somente com o metadata em cache (o download continua exigindo rede). |
| `--dry-run` | `false` | Mostra o plano sem criar diretórios nem baixar. |
| `-y`, `--yes` | `false` | Confirma quando a versão resolvida difere da informada (canal, feature band, `lts`, `sts`). |
| `--allow-root` | `false` | Permite o escopo user executando como root. |
| `--configure-env` | `true` | Configura `DOTNET_ROOT` e `PATH` nos perfis (seção [`env`](#env)); `false` só imprime as instruções. |
| `--env-conflict` | `fail` | `fail`, `override` ou `skip` quando terceiros definem outro `DOTNET_ROOT`. |
| `--telemetry-optout` | `false` | Inclui `DOTNET_CLI_TELEMETRY_OPTOUT=1` no ambiente. |
| `--output` | `text` | `text` ou `json`. |

Componentes e especificações de versão seguem as mesmas regras de
[`list --available`](#especificação-de-versão).

### Comportamento

1. Recusa execução como root (código `7`) sem `--allow-root`; com ele, a raiz
   padrão é `/root/.dotnet`.
2. Valida `--install-dir`, inclusive após resolver links simbólicos. São
   recusados com código `7`: `/`, o próprio home, diretórios do sistema (`/usr`,
   `/opt`, `/etc`, `/var` etc.) e o home de outros usuários. Subdiretórios do
   próprio home são sempre aceitos, mesmo quando o home fica em um desses locais.
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
   duplicados e arquivos acima dos limites são recusados com código `5`.
9. Move para a raiz apenas as pastas versionadas e os arquivos ausentes, sem
   substituir nada existente. O executável `dotnet` só é atualizado quando o
   `host/fxr` do arquivo é mais novo que todos os da raiz. Falhas desfazem o que
   foi movido. Ctrl+C cancela o download e a extração e remove o staging; depois
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

Configura `DOTNET_ROOT` e `PATH` nos perfis do usuário no Linux e no macOS. O
`install` executa o mesmo fluxo por padrão (`--configure-env true`).

```text
dotnetscope env show   [--shell posix|sh|bash|zsh|fish] [--install-dir <dir>] [--telemetry-optout]
dotnetscope env apply  [--install-dir <dir>] [--env-conflict fail|override|skip] [--telemetry-optout]
                       [-y|--yes] [--dry-run] [--output text|json]
dotnetscope env remove [--dry-run] [--output text|json]
```

- `env show` só imprime o script, sem gravar nada. É útil para a sessão atual:
  `eval "$(dotnetscope env show)"`.
- `env apply` exige `dotnet` na raiz (código `3` se não houver).
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
- no `install`, também `disabled` (`--configure-env false`) e `notApplied`
  (validação do host falhou).

O objeto `environment` da saída JSON do `install` tem o mesmo formato.

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
| `6` | Conflito: outra operação em andamento, item existente com tipo incompatível, `DOTNET_ROOT` de terceiros com outro valor, bloco malformado, perfil não UTF-8 ou alterado durante a operação. |
| `7` | Privilégio inadequado: escopo user como root ou `--install-dir` em local do sistema ou de outro usuário. |
| `8` | Instalação gravada, mas a validação falhou com dependências nativas ausentes. |
| `130` | Cancelado pelo usuário (Ctrl+C ou confirmação recusada). |
