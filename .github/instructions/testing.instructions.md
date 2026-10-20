---
name: "Testes do DotnetScope"
description: "Use ao criar ou alterar testes unitários, de integração ou E2E do DotnetScope, fixtures de metadata, arquivos maliciosos ou matrizes por SO."
applyTo: "tests/**, **/*Tests.cs"
---

# Testes

- Use xUnit; nomes descrevem comportamento observável (`Metodo_Cenario_Resultado`).
- Unitários cobrem parsing de versões (`10.0.400`, `10.0`, `10.0.4xx`, `lts`, `sts`, forma compacta `SDK-10.0.400`), resolução contra fixtures de `releases-index.json`/`releases.json`, seleção de estratégia por SO/escopo e geração de blocos de perfil.
- Integração usa filesystem real em diretório temporário isolado, servidor HTTP local com metadata e arquivos sintéticos e hashes calculados no teste; nunca dependa da internet fora do job agendado.
- Extração cobre tar/zip maliciosos: caminho absoluto, `..`, symlink e hardlink para fora, entradas de dispositivo, nomes duplicados e arquivo truncado.
- Integridade cobre hash divergente, download interrompido, retomada, redirect para host fora da allowlist e metadata malformado.
- Transação cobre falha no meio da instalação, cancelamento, lock concorrente e `doctor --repair`.
- Ambiente segue `environment-configuration.instructions.md`; testes que editam perfil, registro ou `PATH` executam apenas em containers ou runners efêmeros, nunca no perfil do desenvolvedor.
- E2E usa o binário Native AOT em containers limpos sem .NET (Ubuntu 22.04/24.04, Debian, Fedora, Alpine/musl) e runners Windows/macOS: instalar, abrir shell de login novo, executar `dotnet --version`, desinstalar e comparar perfis com o estado inicial.
- Declare a matriz SO × arquitetura × shell × escopo com resultado; marque combinações não verificadas explicitamente.
- Download real de artefatos oficiais roda em job agendado, não em cada PR.
- Fixtures usam apenas dados sintéticos e nunca contêm credenciais, caminhos pessoais ou nomes de usuário reais.
- Teste alterado deve falhar sem a correção e passar com ela.
- Registre comandos e resultados no PR; não marque gate sem executar.
