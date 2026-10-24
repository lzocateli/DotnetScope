# DotnetScope - Instruções globais

Toda documentação produzida ou atualizada deve estar em português brasileiro
(pt-BR). Preserve comandos, opções, identificadores, caminhos e marcadores
literais exigidos pelas ferramentas.

## Contexto obrigatório

- O DotnetScope é uma CLI Native AOT (`dotnetscope`) que instala, lista, valida e remove SDKs e runtimes .NET nos escopos `user` e `global`, com verificação de integridade e configuração do ambiente do usuário.
- Leia o [README](../README.md) e o [plano de implementação](../docs/plano-implementacao.md) antes de alterar interface, estratégia de instalação, configuração de ambiente, segurança ou distribuição.
- Código em C#/.NET 10 com `PublishAot`; o binário publicado não depende de runtime .NET instalado.
- Build, testes e publicação AOT executam em containers ou runners de CI; comandos Docker/Podman no host apenas os orquestram.
- Testes que instalam .NET, editam perfil de shell, registro ou `PATH` executam somente em containers descartáveis ou runners efêmeros, nunca no perfil do desenvolvedor.
- O repositório não usa Spec Kit. O trabalho segue Issue → fase do plano → branch → PR com evidências; ADRs registram decisões duradouras.
- Contribuições públicas e autoridade exclusiva do mantenedor seguem [CONTRIBUTING](CONTRIBUTING.md) e [GOVERNANCE](GOVERNANCE.md); exceção de revisão não dispensa CI nem autoriza push direto na `main`.

## Fluxo de trabalho

1. Comece pelo comportamento, teste relacionado e módulo proprietário (`Cli`, `Core` ou `Infrastructure`).
2. Declare uma hipótese local e uma validação capaz de refutá-la.
3. Faça a menor alteração coerente e valide o recorte imediatamente.
4. Valide primeiro o recorte alterado; depois execute verificações amplas proporcionais ao risco, incluindo publicação AOT sem avisos.
5. Atualize README, plano, referência da CLI, segurança e testes quando o comportamento exigir.
6. Preserve alterações existentes e não reformate arquivos fora do escopo.
7. Execute somente fases ou issues com escopo revisado e autorização explícita do mantenedor.

## ADRs

- ADRs ficam em `docs/adr/AAAA-NNNN-titulo.md`, começam como `proposed` e seguem `.github/ADR_TEMPLATE.md`.
- IA não marca ADR como `accepted` sem decisão ou evidência humana explícita e referenciada.

## Commits

- Todo commit criado ou sugerido segue [Conventional Commits](COMMIT_CONVENTIONS.md) e usa os tipos e escopos aprovados pelo projeto.
- Escreva a descrição em português brasileiro, no imperativo, iniciando com minúscula e sem ponto final.
- Mudanças incompatíveis na CLI (opção, padrão, saída JSON, código de saída, local de instalação ou arquivo de perfil) usam `!` no cabeçalho e o rodapé `BREAKING CHANGE:` com impacto e migração.
- Não faça commit sem solicitação explícita. Push, publicação de release e tags sempre exigem solicitação explícita.

## Regras invariáveis do produto

- Nada é sobrescrito ou removido silenciosamente: instalações existentes (`sdk/<versão>`, `shared/<framework>/<versão>`), arquivos de perfil e chaves de registro alheios são preservados.
- `--configure-env` tem padrão `true`; com `false`, a CLI não edita perfis, registro ou variáveis persistentes e imprime instruções. A instalação ainda grava seus próprios arquivos; o ambiente de pacotes e instaladores oficiais globais é responsabilidade desses mecanismos.
- Alterações de ambiente ficam somente entre os marcadores `# >>> dotnetscope >>>` e `# <<< dotnetscope <<<`, são idempotentes, precedidas de backup e gravadas atomicamente.
- Blocos de terceiros (Ansible, `dotnet-install`, pacotes da distribuição) nunca são editados. Conflito de `DOTNET_ROOT` ou `PATH` exige decisão explícita (`--env-conflict`).
- Todo artefato baixado é verificado por SHA-512 contra o metadata oficial antes de extração ou execução; hosts de download seguem allowlist.
- Nunca execute scripts baixados da rede, nem interpole argumentos em shell; use `ProcessStartInfo.ArgumentList`.
- Extração rejeita caminhos absolutos, `..`, links para fora do destino e entradas especiais; usa staging e movimentação atômica.
- O escopo `global` exige processo já elevado; a CLI não se autoeleva. O escopo `user` não executa como root sem `--allow-root`.
- Não misture instalação global por `apt` com instalação global por arquivo no mesmo destino.
- Normalize e valide paths (`--install-dir`, `--archive`) antes de qualquer acesso ao filesystem.
- Não registre tokens, variáveis de ambiente completas, conteúdo de arquivos de perfil ou secrets em logs e saídas.
- No desenvolvimento local, secrets (por exemplo, chaves de assinatura) ficam exclusivamente em `$env:APPDATA\Microsoft\UserSecrets\DotnetScope\.env` no Windows ou `~/.microsoft/usersecrets/DotnetScope/.env` no Linux/macOS. Nunca crie `.env`, `secrets.json` ou templates com valores reais no repositório e não leia, exiba ou copie esse arquivo durante validações.
- O hook `.githooks/pre-commit` e o workflow `gw-gitleaks.yml` são obrigatórios; nunca use `--no-verify`. Consulte [SECRET-SCANNING.md](SECRET-SCANNING.md).
- Workflows e scripts `gw-*` e `tools/scripts/ci/` são gerenciados pelo Groundwork: não os edite localmente; mudanças vão para o catálogo. Consulte [CI-CD.md](CI-CD.md).
- Dependências externas exigem licença permissiva, gratuita, versão fixada, manutenção verificada e compatibilidade com Native AOT. Licença ausente, ambígua, copyleft, comercial ou source-available bloqueia a dependência sob a distribuição MIT do DotnetScope.
- Prefira a BCL; toda dependência nova precisa de justificativa no PR.

## Qualidade mínima

- Código novo possui testes nos níveis adequados ao risco.
- Rode formatador, análise estática, build e testes do projeto alterado.
- Publicação Native AOT não pode gerar avisos de trimming ou AOT.
- Filesystem, extração, perfis de shell e registro usam testes de integração reais em containers ou runners efêmeros, não mocks como evidência principal.
- Fluxos de instalação são validados de ponta a ponta com o binário AOT em container limpo, sem .NET pré-instalado.
- Superfícies privilegiadas (escopo `global`, download, extração, edição de perfil e registro) recebem threat model e revisão de segurança.
- Reviews priorizam integridade do ambiente do usuário, segurança da cadeia de download, regressões, concorrência e lacunas de teste.

## Instruções especializadas

As regras detalhadas em `.github/instructions/` são carregadas pelo caminho alterado ou pela descrição. Não replique essas regras em código ou documentação local.
