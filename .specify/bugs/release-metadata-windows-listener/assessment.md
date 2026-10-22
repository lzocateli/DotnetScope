# Avaliação de Bug: falha ao encerrar o servidor HTTP de testes no Windows

- **Slug**: release-metadata-windows-listener
- **Criada em**: 2026-10-22
- **Origem**: https://github.com/lzocateli/DotnetScope/actions/runs/39866441717/job/120129699329
- **Veredito**: valid
- **Severidade**: medium

## Relato (literal ou resumido)

No job `Build, tests and Native AOT (win-x64)` do PR #13, o build e os testes unitários passaram, mas um teste de integração falhou durante o descarte do servidor HTTP local:

```text
ReleaseMetadataClientTests.Offline_SemCache_FalhaSemRede
System.ArgumentException: The handle is invalid. (Parameter 'handle')
at System.Threading.ThreadPoolBoundHandle.BindHandleCore(SafeHandle handle)
at System.Net.HttpListenerSession.get_RequestQueueBoundHandle()
at System.Net.HttpListener.BeginGetContext(...)
at DotnetScope.IntegrationTests.LocalMetadataServer.LoopAsync()
at DotnetScope.IntegrationTests.LocalMetadataServer.Dispose()
```

O resultado do job foi 64 testes de integração aprovados e 1 falho; os testes unitários passaram.

## Sintoma

No runner Windows, fechar `LocalMetadataServer` enquanto sua tarefa de escuta está aguardando uma requisição pode fazer `HttpListener.GetContextAsync()` lançar `ArgumentException` por handle inválido. A tarefa termina com a exceção não tratada e `Dispose()` falha, fazendo o check Windows obrigatório do PR falhar.

## Reprodução

1. Executar `PR Validation` para o commit `4501343705aabb03a16943432ae6dadc40f12e5c` em runner Windows.
2. Executar a suíte de integração `DotnetScope.IntegrationTests`.
3. Observar `Offline_SemCache_FalhaSemRede` passar suas asserções e falhar ao descartar `ReleaseMetadataClientTests`, em `LocalMetadataServer.Dispose()`.

## Caminhos de Código Suspeitos

- `tests/DotnetScope.IntegrationTests/LocalMetadataServer.cs:68` — fecha o listener e aguarda a tarefa de escuta; o `Dispose` só considera `ObjectDisposedException` e `HttpListenerException` como término normal.
- `tests/DotnetScope.IntegrationTests/LocalMetadataServer.cs:143-147` — a captura de `ArgumentException` depende de consultar `_listener.IsListening` após a exceção, embora o estado relevante seja o descarte iniciado pelo próprio teste.
- `tests/DotnetScope.IntegrationTests/ReleaseMetadataClientTests.cs:141-149` — o caso que aparece no job Windows e aciona o descarte do servidor.
- `tests/DotnetScope.IntegrationTests/ReleaseMetadataClientTests.cs:203-206` — descarta o servidor ao finalizar cada teste.

## Hipótese de Causa Raiz

O fechamento de `HttpListener` no Windows invalida o handle usado por `BeginGetContext`. O código já tenta tratar `ArgumentException`, mas condiciona essa captura ao estado nativo observado por `_listener.IsListening`; a exceção escapa em uma corrida de encerramento, tornando o predicado dependente do estado do listener insuficiente para distinguir o encerramento intencional. Confiança: medium; a stack trace confirma a origem e o momento, mas a corrida precisa ser coberta pela validação Windows após a correção.

## Correção Proposta

**Preferencial**: registrar explicitamente o início do descarte antes de fechar o listener e usar esse estado, em vez de consultar `IsListening`, para reconhecer `ArgumentException` de `GetContextAsync` causada pelo encerramento intencional. Manter a captura restrita a `ArgumentException` durante o descarte; não suprimir exceções desse tipo enquanto o servidor deveria continuar atendendo.

**Arquivos Prováveis de Alteração**:

- `tests/DotnetScope.IntegrationTests/LocalMetadataServer.cs`
- `tests/DotnetScope.IntegrationTests/` — teste focado de descarte, se necessário para cobrir o caminho sem depender apenas da suíte completa.

**Testes a Adicionar ou Atualizar**:

- Cobrir que o encerramento do servidor durante a espera por `GetContextAsync` completa `Dispose()` sem propagar a exceção de handle inválido no Windows.
- Executar a suíte de integração no runner Windows e confirmar que Linux/macOS continuam passando.
- Reexecutar o workflow `PR Validation` e confirmar sucesso de todos os status checks obrigatórios.

## Riscos e Considerações

- A captura deve depender de um sinal explícito de encerramento para não mascarar erros inesperados de `HttpListener` em operação normal.
- A mudança pertence ao servidor HTTP de testes; não altera o código de produção, contratos, dependências nem dados.
- O PR não deve ser aprovado ou mesclado enquanto qualquer status check obrigatório estiver falhando; um bypass de revisão não pode dispensar esses checks.

## Perguntas em Aberto

- Nenhuma identificada.
