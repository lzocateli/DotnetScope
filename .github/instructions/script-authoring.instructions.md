---
name: "Criação e manutenção de scripts"
description: "Use ao criar, mover ou refatorar scripts PowerShell ou shell do DotnetScope, inclusive automações em tools/scripts e instaladores install.sh/install.ps1."
applyTo: "**/*.ps1, **/*.sh, **/*.bash, tools/**, .githooks/**"
---

# Criação e manutenção de scripts

- Automação de repositório (build, publicação AOT, testes em container, empacotamento, validação) fica em `tools/scripts/` e usa PowerShell 7 para funcionar igual em Linux, macOS e Windows.
- Instaladores distribuídos (`install.sh`, `install.ps1`) e hooks Git são as únicas exceções a Bash/sh; mantenha-os POSIX, pequenos e sem dependências além de `curl`/`wget`, `tar` e `sha256sum`/`shasum` (ou equivalentes do PowerShell).
- Todo script aceita `--help`, encerra com código `0` e não produz efeitos colaterais ao exibir ajuda.
- A ajuda informa finalidade, pré-requisitos, sintaxe, parâmetros, padrões e ao menos um exemplo executável.
- Erros de uso vão para stderr, retornam código diferente de zero e orientam a consultar `--help`.
- Scripts são não interativos por padrão; operações destrutivas exigem opção explícita.
- Execução em container detecta Docker e, na ausência dele, Podman; `DOTNETSCOPE_CONTAINER_ENGINE` força o engine. Use nomes de imagem totalmente qualificados (`docker.io/...`) e versões fixadas.
- Scripts nunca alteram o perfil, o registro ou o `PATH` do desenvolvedor; isso é responsabilidade exclusiva do binário `dotnetscope` e dos testes em containers.
- PowerShell: `#requires -Version 7.0`, `[CmdletBinding()]`, comment-based help (`.SYNOPSIS`, `.DESCRIPTION`, `.PARAMETER`, `.EXAMPLE`, `.NOTES`) e tratamento explícito de `$LASTEXITCODE`.
- Shell: `set -eu`, aspas em todas as expansões e `shellcheck` sem avisos.
- Nunca inclua tokens, chaves privadas ou valores reais em argumentos, exemplos ou saída.
- Valide `--help` e ao menos um fluxo funcional representativo; atualize workflows e documentação quando caminho ou interface mudar.
