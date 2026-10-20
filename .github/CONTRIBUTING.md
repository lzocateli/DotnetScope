# Contribuindo com o DotnetScope

## Antes de começar

1. Leia o [README](../README.md), o [plano de implementação](../docs/plano-implementacao.md) e os documentos da área afetada.
2. Procure a Issue, a fase do plano e os ADRs relacionados.
3. Confirme o escopo e a autorização da mudança antes de executar.
4. Nunca inclua secrets em nenhuma parte do código ou configuração, inclusive
   workflows, testes, documentação, exemplos, scripts e connection strings.
   Secrets locais permanecem exclusivamente no arquivo externo
   `$env:APPDATA\Microsoft\UserSecrets\DotnetScope\.env` (Windows) ou
   `~/.microsoft/usersecrets/DotnetScope/.env` (Linux/macOS), sem leitura por automações, e secrets de CI
   permanecem nos Environments do GitHub.
5. Instale o hook obrigatório de detecção de secrets em cada clone:

   ```powershell
   ./tools/scripts/install-gitleaks-hook.ps1
   git hook run pre-commit
   ```

   O hook usa Docker ou, na ausência dele, Podman rootless. Consulte a
   [política de detecção de secrets](SECRET-SCANNING.md).
   O hook usa `.gitleaks.toml`; não o substitua por uma execução sem a
   configuração versionada.

## Licenciamento de contribuições

O código é distribuído sob `MIT`; consulte o arquivo [LICENSE](../LICENSE).

## Fluxo

O repositório não usa Spec Kit. O trabalho segue Issue → fase do plano → branch → PR.

1. Abra ou referencie uma Issue que descreva o problema, o resultado esperado e a fase do plano.
2. Crie uma branch `tipo/descricao-curta` a partir de `main`.
3. Atualize testes, referência da CLI e documentação necessários; valide o menor recorte primeiro e registre evidências no PR.
4. Testes que instalam .NET ou editam perfil, registro ou `PATH` rodam apenas em containers ou runners efêmeros, nunca no seu perfil.
5. Use os [Conventional Commits](COMMIT_CONVENTIONS.md). Push, merge, publicação e deploy exigem autorização separada.

## Definition of Done

- escopo e autorização rastreáveis, com evidências no artefato de origem;
- critérios de aceite demonstrados;
- código formatado, analisado e compilado;
- testes reais proporcionais ao risco;
- interface da CLI (opções, padrões, saída JSON e códigos de saída) documentada e compatível;
- segurança, preservação do ambiente do usuário, concorrência e desempenho considerados;
- logs e saídas não expõem variáveis, perfis ou conteúdo sensível;
- publicação Native AOT sem avisos nos RIDs afetados;
- hook Gitleaks instalado e `Gitleaks / Full History` aprovado;
- desinstalação e reversão do ambiente documentadas e testadas;
- nenhum achado crítico ou alto aberto sem aceitação formal.

## Pull requests

Mantenha PRs coesos e pequenos o bastante para revisão responsável. Mudanças incompatíveis na CLI exigem issue, ADR quando aplicável, estratégia de migração e período de compatibilidade. Não misture refactor amplo com mudança funcional.
