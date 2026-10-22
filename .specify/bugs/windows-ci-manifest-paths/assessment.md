# Avaliação de Bug: fixtures do manifesto incompatíveis com Windows

- **Slug**: windows-ci-manifest-paths
- **Criada em**: 2026-10-22
- **Origem**: https://github.com/lzocateli/DotnetScope/actions/runs/39843102304/job/120049140959?pr=12
- **Veredito**: valid
- **Severidade**: medium

## Relato (literal ou resumido)

URL literal: `https://github.com/lzocateli/DotnetScope/actions/runs/39843102304/job/120049140959?pr=12`

Host interpretado: `github.com`.

Política de consulta: `allowlisted`.

No job `Build, tests and Native AOT (win-x64)`, o build terminou sem erros, mas dois testes unitários falharam. Ambos lançaram `DotnetScopeException: Manifesto com caminho inválido em sdk 10.0.402; ele não será usado nem sobrescrito.` em `InstallationManifestStore.ToRecord`. O resultado foi 418 aprovados, 2 falhos e 92 ignorados nos testes unitários; os 65 testes de integração passaram.

## Sintoma

Na validação Windows do PR #12, testes recém-adicionados do manifesto falham antes de verificar a compatibilidade do campo `declaredInvoker`. Espera-se que as fixtures usem caminhos absolutos válidos na plataforma do runner, para que os testes cubram o comportamento pretendido em Windows e Unix.

## Reprodução

1. Executar o workflow `PR Validation` para o commit `3ca298e0cc7ae705d6745f8405a0ce2ca99234a9` em runner Windows.
2. Executar a etapa `Validate on host`, que compila a solução e roda `dotnet test DotnetScope.slnx --configuration Release --no-build --filter Platform!=Unix`.
3. Observar a falha em `AddAsync_PersistsDeclaredInvokerForGlobalRecord` e `Read_LegacyManifestWithoutDeclaredInvoker_RemainsCompatible`.

## Caminhos de Código Suspeitos

- `tests/DotnetScope.UnitTests/InstallationManifestStoreTests.cs:47` — a fixture legada fixa `root` como `/usr/local/share/dotnet`, caminho absoluto Unix que não é totalmente qualificado no Windows.
- `tests/DotnetScope.UnitTests/InstallationManifestStoreTests.cs:62` — a fixture que pretende testar `declaredInvoker` inválido contém o mesmo caminho incompatível, podendo passar pela exceção de caminho inválido em vez de exercitar a validação do campo.
- `tests/DotnetScope.UnitTests/InstallationManifestStoreTests.cs:79` — `CreateRecord` fornece o mesmo caminho Unix ao teste de persistência, que depois relê o manifesto no runner Windows.
- `src/DotnetScope.Infrastructure/Installation/InstallationManifestStore.cs:146` — `ToRecord` rejeita corretamente entradas cujo `Root` não é absoluto na plataforma atual.

## Hipótese de Causa Raiz

As fixtures introduzidas para a auditoria do manifesto assumem caminhos Unix, embora a mesma suíte seja executada em Windows. A validação de `Path.IsPathFullyQualified` é específica da plataforma e está funcionando conforme esperado; o defeito está nos dados sintéticos dos testes, não no parser de produção. Confiança: high.

## Correção Proposta

**Preferencial**: tornar as raízes das fixtures caminhos absolutos válidos para a plataforma corrente, derivados do diretório temporário isolado já usado pelos testes. Para JSON escrito manualmente, serializar o caminho com `System.Text.Json` ou construir os dados de modo que separadores e caracteres especiais sejam escapados corretamente. Endurecer também o teste de `declaredInvoker` inválido para confirmar a mensagem/causa esperada, garantindo que não passe por uma falha anterior de validação de caminho.

**Arquivos Prováveis de Alteração**:

- `tests/DotnetScope.UnitTests/InstallationManifestStoreTests.cs`

**Testes a Adicionar ou Atualizar**:

- Atualizar os testes de persistência e compatibilidade legada para usar caminhos portáveis e verificar a leitura bem-sucedida.
- Garantir que `Read_InvalidDeclaredInvoker_FailsWithoutRevealingItsValue` falhe especificamente por campo de auditoria inválido, mantendo as asserções de não exposição do conteúdo.
- Executar os testes unitários focados no runner Windows e a validação de CI aplicável; confirmar também a suíte Linux para preservar o comportamento existente.

## Riscos e Considerações

- Não alterar a validação de caminhos de produção: ela protege contra raízes inválidas e o log indica que o comportamento correto rejeitou dados de teste incompatíveis.
- Como o defeito bloqueia um job obrigatório do PR, a severidade é medium; não há evidência de falha em runtime de produção.
- Não é necessária mudança de contrato, migração, dependência ou configuração de segurança.

## Perguntas em Aberto

- Nenhuma identificada para a causa reportada.
