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
| `2` | Uso inválido: opção, versão ou componente inválido, prerelease exata sem `--prerelease`. |
| `3` | Canal, versão ou artefato inexistente para o RID. |
| `4` | Falha de rede, metadata indisponível ou malformado, ou ausente no cache com `--offline`. |
| `5` | Origem fora da allowlist (metadata, redirect ou arquivo resolvido). |
| `130` | Cancelado pelo usuário (Ctrl+C). |

Os códigos `6`, `7` e `8` estão reservados para conflito, privilégio e
dependência nativa ausente, usados a partir da instalação (F2).
