# Fixtures de metadata

Documentos sintéticos no formato oficial de
[`releases-index.json`](https://json.schemastore.org/dotnet-releases-index.json) e
`releases.json` por canal, nomeados como as chaves do cache
(`releases-<canal>.json`). Versões e datas seguem o padrão real, mas URLs não são
baixadas e os hashes são valores de baixa entropia, não SHA-512 de arquivos reais.

Casos cobertos:

- canais `go-live` (11.0), `active` (10.0), `maintenance` (9.0) e `eol` (7.0) e uma
  entrada de índice inválida que deve ser descartada;
- 10.0.13: hash SHA-512 em maiúsculas no SDK `linux-x64`;
- 10.0.12: SDK 10.0.401 apenas com hash SHA-256 (artefato inutilizável);
- 10.0.11: três feature bands (10.0.400, 10.0.303 e 10.0.111);
- prereleases em 10.0 e 11.0, variante `composite` e arquivo sem RID no ASP.NET Core;
- 9.0: SDK 9.0.319 `linux-arm64` com URL `http://` em host fora da allowlist;
- `windowsdesktop` publicado somente para `win-x64` e ausente em 7.0.
