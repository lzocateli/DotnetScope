---
name: "Segurança do DotnetScope"
description: "Use ao alterar download, verificação de integridade, extração, privilégios, escopo global, execução de processos, perfis, registro, dependências, distribuição ou logs do DotnetScope."
applyTo: "src/**, tools/**, .github/workflows/**"
---

# Segurança

- Metadata e artefatos vêm apenas de HTTPS em hosts oficiais da allowlist; redirects para fora dela falham fechado.
- Verifique SHA-512 do metadata oficial antes de extrair ou executar; `--archive` local exige `--sha512` explícito.
- Nunca execute scripts baixados (`dotnet-install.sh`/`.ps1` ou outros); a CLI implementa a instalação por arquivo diretamente.
- Downloads e staging ficam em diretório privado (0700) no mesmo filesystem do destino, evitando TOCTOU entre verificação e uso.
- Extração é confinada ao destino normalizado; rejeite caminhos absolutos, `..`, links e entradas especiais.
- O escopo `global` exige processo já elevado; a CLI não chama `sudo`/UAC sozinha. O escopo `user` recusa root sem `--allow-root` e nunca grava em diretórios do sistema.
- `--install-dir` é normalizado, absoluto e validado contra diretórios do sistema, raiz e diretórios de outros usuários.
- Comandos externos usam caminho absoluto, `ArgumentList`, ambiente mínimo e timeout; nada de interpolação em shell.
- Conteúdo escrito em perfis e registro é escapado para o destino; trate caminhos com caracteres especiais como dados, nunca como código.
- Estado, manifesto, lock e backups ficam em diretórios com permissões restritas: do usuário no escopo `user`, do sistema e separados dos dados do usuário no escopo `global`, conforme a [CLI](../../docs/cli.md).
- Logs e `--output json` não incluem variáveis de ambiente completas, conteúdo de perfis, tokens ou URLs com credenciais.
- Releases do DotnetScope publicam `SHA256SUMS`, SBOM e atestações Sigstore keyless de proveniência e SBOM; não há Authenticode ou Developer ID dos binários próprios na F9 sem custo. Os instaladores `install.sh`/`install.ps1` verificam o checksum e não se autoelevam.
- Dependências exigem licença permissiva compatível com MIT, versão fixada, manutenção verificada, compatibilidade AOT e verificação de vulnerabilidades.
- Nova fronteira de privilégio, nova fonte de download ou novo arquivo de sistema alterado exige threat model e testes negativos.
- Achado crítico ou alto bloqueia release sem aceitação formal de risco.
