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
| `tests/DotnetScope.UnitTests/InstallationManifestStoreTests.cs` | modificado | Caminho de fixture portátil e serialização JSON de strings com `JsonEncodedText`, compatível com as verificações de trimming/AOT. As fixtures legada e inválida usam um helper comum. |

## Destaques do Diff

- `RootPath` usa o diretório temporário isolado e alimenta tanto o registro persistido quanto as fixtures JSON.
- `Read_InvalidDeclaredInvoker_FailsWithoutRevealingItsValue` exige a mensagem de campo de auditoria inválido e continua verificando que o valor sensível não aparece na exceção.

## Testes Adicionados ou Atualizados

- `InstallationManifestStoreTests.AddAsync_PersistsDeclaredInvokerForGlobalRecord` — valida persistência e leitura com caminho portável.
- `InstallationManifestStoreTests.Read_LegacyManifestWithoutDeclaredInvoker_RemainsCompatible` — valida compatibilidade de manifesto legado com caminho portável.
- `InstallationManifestStoreTests.Read_InvalidDeclaredInvoker_FailsWithoutRevealingItsValue` — confirma a causa da rejeição e protege contra exposição do valor.

## Verificação Local

- Comando focado, dentro da imagem `docker.io/lzocateli/dotnet-sdk:10.0.402-noble-v1`: `dotnet test tests/DotnetScope.UnitTests/DotnetScope.UnitTests.csproj --configuration Release --filter FullyQualifiedName~DotnetScope.UnitTests.InstallationManifestStoreTests` → aprovado, 4 testes.
- `dotnet format DotnetScope.slnx --verify-no-changes --no-restore` na mesma imagem → aprovado.
- `dotnet test tests/DotnetScope.UnitTests/DotnetScope.UnitTests.csproj --configuration Release` na mesma imagem → aprovado, 512 testes.
- `git diff --check` → aprovado.
- A validação específica no runner Windows ainda está pendente; deve ser confirmada no workflow do PR.

## Desvios da Avaliação

Nenhum. `JsonEncodedText` foi usado para escapar as strings JSON sem introduzir chamadas de serialização incompatíveis com as verificações de trimming/AOT dos testes.

## Acompanhamento

- Confirmar o job `Build, tests and Native AOT (win-x64)` no PR após publicar a correção.
- Se o runner Windows passar, atualizar o estado deste relatório para `applied`.
