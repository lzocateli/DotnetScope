# Verificação de Bug: fixtures do manifesto incompatíveis com Windows

- **Slug**: windows-ci-manifest-paths
- **Testado em**: 2026-10-22
- **Avaliação**: ./assessment.md
- **Correção**: ./fix.md
- **Resultado**: verified
- **Estado do bug**: fechado em 2026-10-22 após aprovação e merge do PR #12

## Resumo

A falha originalmente reproduzida no job Windows não ocorreu novamente após a correção: as fixtures usam caminhos absolutos portáveis e a expectativa de `declaredInvoker` respeita a política da plataforma. A validação final do PR passou em Windows, Linux e macOS, sem falhas nos testes unitários e de integração executados nesses jobs.

## Verificações Executadas

| Verificação | Comando / Ação | Resultado | Notas |
|-------|------------------|--------|-------|
| Reprodução após correção | Workflow `PR Validation`, execução `39845533558`, job `Build, tests and Native AOT (win-x64)` no SHA `56a06400a6c346c0179ab807d1fd9a65089abaf7` | pass | O job passou; as falhas de caminho e a expectativa incorreta de persistência não se reproduziram. |
| Testes novos / atualizados | `dotnet test tests/DotnetScope.UnitTests/DotnetScope.UnitTests.csproj --configuration Release --filter FullyQualifiedName~DotnetScope.UnitTests.InstallationManifestStoreTests` em container Linux | pass | 4 testes passaram. |
| Suíte de regressão | `dotnet test tests/DotnetScope.UnitTests/DotnetScope.UnitTests.csproj --configuration Release` em container Linux | pass | 512 testes passaram. No job Windows: 420 unitários passaram, 92 foram ignorados, nenhum falhou; os 65 testes de integração passaram. |
| Matriz de plataforma | Workflow `PR Validation` nos jobs `linux-x64`, `osx-arm64` e `win-x64` | pass | Build, testes e Native AOT passaram nos três jobs. |
| Lint / verificação de tipos | `dotnet format DotnetScope.slnx --verify-no-changes --no-restore` em container Linux | pass | Nenhuma divergência de formatação. |

## Trechos de Saída

```text
Windows unit tests: Failed: 0, Passed: 420, Skipped: 92, Total: 512
Windows integration tests: Failed: 0, Passed: 65, Skipped: 0, Total: 65
PR Validation 39845533558: success
```

## Riscos Residuais

- No Windows, testes marcados para Unix permanecem ignorados conforme a seleção da suíte; a validação Linux e macOS também passou.
- A validação confirma os testes e builds do commit verificado, sem afirmar cobertura de todas as combinações de sistema e arquitetura.

## Recomendação

Encerre o bug: o sintoma original foi reproduzido no job Windows antes da correção e deixou de ocorrer na execução de CI posterior. O PR #12 foi aprovado e mesclado.
