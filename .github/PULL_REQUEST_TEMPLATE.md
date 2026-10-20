# Pull request

## Objetivo

<!-- Problema resolvido e resultado observável. -->

## Escopo

<!-- Mudanças e itens deliberadamente excluídos. -->

## Rastreabilidade

- Issue:
- Fase do plano (`docs/plano-implementacao.md`):
- ADR:

## Áreas

- [ ] CLI
- [ ] Resolução de versões
- [ ] Instalação
- [ ] Ambiente e PATH
- [ ] Linux
- [ ] Windows
- [ ] macOS
- [ ] Segurança
- [ ] Empacotamento
- [ ] CI/CD
- [ ] Documentação
- [ ] Testes

## Evidências

- [ ] `Gitleaks / Full History`
- [ ] Formatação e análise estática
- [ ] Build de produção
- [ ] Testes unitários
- [ ] Testes de integração com filesystem, perfis e registro reais
- [ ] Compatibilidade da CLI (opções, padrões, `--output json`, códigos de saída)
- [ ] Publicação Native AOT sem avisos nos RIDs afetados
- [ ] E2E com binário AOT em container limpo, sem .NET pré-instalado

Comandos e resultados:

## Ambiente do usuário

- [ ] `--configure-env true` é idempotente (duas execuções produzem o mesmo perfil)
- [ ] `--configure-env false` não altera perfil, registro nem variáveis persistentes
- [ ] Blocos e instalações de terceiros foram preservados
- [ ] `uninstall`/`env remove` revertem somente o que o DotnetScope criou

## Segurança e operação

- [ ] Download verificado por SHA-512 antes de extração ou execução
- [ ] Extração confinada ao destino (sem `..`, caminhos absolutos ou links externos)
- [ ] Privilégios avaliados: `global` exige elevação prévia; `user` não roda como root
- [ ] Idempotência, lock, timeout e recuperação após falha parcial foram avaliados
- [ ] Nenhum secret ou dado sensível foi incluído
- [ ] Nenhuma connection string, senha de teste ou credencial foi incluída em
  código, testes, documentação, scripts ou workflows
- [ ] Secrets necessários ao CI foram criados no Environment do GitHub, e não
  commitados no repositório
- [ ] Dependências possuem licença e versão verificadas
- [ ] README, plano e referência da CLI estão atualizados

## Riscos e limitações

<!-- Riscos residuais e validações não executadas. -->

Registre gates não executados como `NOT RUN`, com motivo; não marque aprovação
sem evidência. Reviews são read-only. Este PR não autoriza commit, push, merge,
publicação ou deploy por um agente.
