# Correção de Bug: fixtures do manifesto incompatíveis com Windows

- **Slug**: windows-ci-manifest-paths
- **Corrigido em**: 2026-10-22
- **Avaliação**: ./assessment.md
- **Estado**: partial

## Resumo

As fixtures agora derivam `root` do diretório temporário dos testes, tornando o caminho absoluto válido na plataforma corrente. O teste de `declaredInvoker` inválido também verifica a mensagem específica da validação de auditoria, em vez de aceitar qualquer falha anterior.

## Alterações

| Arquivo | Alteração | Notas |
|------|--------|-------|
| `tests/DotnetScope.UnitTests/InstallationManifestStoreTests.cs` | modificado | Caminho de fixture portátil e serialização JSON de strings com `JsonEncodedText`, compatível com as verificações de trimming/AOT. As fixtures legada e inválida usam um helper comum; a persistência respeita a política de plataforma do campo. |

## Destaques do Diff

- `RootPath` usa o diretório temporário isolado e alimenta tanto o registro persistido quanto as fixtures JSON.
- `Read_InvalidDeclaredInvoker_FailsWithoutRevealingItsValue` exige a mensagem de campo de auditoria inválido e continua verificando que o valor sensível não aparece na exceção.

## Testes Adicionados ou Atualizados

- `InstallationManifestStoreTests.AddAsync_AppliesDeclaredInvokerPlatformPolicy` — valida persistência em Linux/macOS e omissão em Windows, com leitura usando caminho portável.
- `InstallationManifestStoreTests.Read_LegacyManifestWithoutDeclaredInvoker_RemainsCompatible` — valida compatibilidade de manifesto legado com caminho portável.
- `InstallationManifestStoreTests.Read_InvalidDeclaredInvoker_FailsWithoutRevealingItsValue` — confirma a causa da rejeição e protege contra exposição do valor.

## Verificação Local

- Comando focado, dentro da imagem `docker.io/lzocateli/dotnet-sdk:10.0.402-noble-v1`: `dotnet test tests/DotnetScope.UnitTests/DotnetScope.UnitTests.csproj --configuration Release --filter FullyQualifiedName~DotnetScope.UnitTests.InstallationManifestStoreTests` → aprovado, 4 testes.
- `dotnet format DotnetScope.slnx --verify-no-changes --no-restore` na mesma imagem → aprovado.
- `dotnet test tests/DotnetScope.UnitTests/DotnetScope.UnitTests.csproj --configuration Release` na mesma imagem → aprovado, 512 testes.
- `git diff --check` → aprovado.
- A primeira execução atualizada do job Windows confirmou a correção das fixtures de caminho, mas encontrou uma expectativa incorreta adicional no teste de `AddAsync`: o campo `declaredInvoker` é omitido no Windows por política. A asserção foi ajustada para validar esse comportamento.
- A execução atualizada do job Windows, após esse ajuste, ainda está pendente.

## Desvios da Avaliação

O primeiro ajuste corrigiu os caminhos conforme a avaliação. A execução Windows revelou que o teste de persistência também precisava respeitar a política existente que limita `declaredInvoker` a Linux/macOS; o teste agora verifica a persistência nesses sistemas e a omissão em Windows. `JsonEncodedText` escapa as strings JSON sem introduzir chamadas incompatíveis com as verificações de trimming/AOT.

## Acompanhamento

- Confirmar o job `Build, tests and Native AOT (win-x64)` no PR após publicar o ajuste por plataforma.
- Se o runner Windows passar, atualizar o estado deste relatório para `applied`.
