# Contribuindo com o DotnetScope

## Antes de começar

1. Leia o [README](../README.md), o [plano de implementação](../docs/plano-implementacao.md) e os documentos da área afetada.
2. Procure a Issue, a fase do plano e os ADRs relacionados.
3. Aguarde a confirmação de escopo do mantenedor na Issue antes de implementar.
   Não é necessário conhecer a fase do plano para relatar um problema; o
   mantenedor faz esse enquadramento na triagem.
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
Ao enviar uma contribuição, você concorda com sua distribuição sob essa licença
e confirma ter o direito de contribuir com o conteúdo. Não inclua código de
terceiros sem licença compatível e atribuição quando exigida.

Participe de acordo com o [código de conduta](CODE_OF_CONDUCT.md). Para dúvidas,
consulte [SUPPORT](SUPPORT.md); vulnerabilidades seguem exclusivamente a
[política de segurança](SECURITY.md).

## Fluxo

O repositório não usa Spec Kit. O trabalho segue Issue → fase do plano → branch → PR.

1. Procure Issues e PRs existentes para evitar duplicação. Abra ou referencie
   uma Issue com o problema e o resultado esperado; bugs, melhorias,
   documentação e dúvidas têm [formulários próprios](https://github.com/lzocateli/DotnetScope/issues/new/choose).
   O mantenedor confirma o escopo e relaciona a fase do plano quando aplicável.
2. Faça um fork, clone seu fork e configure o repositório original como
   `upstream`. Na raiz do clone:

   ```bash
   git remote add upstream https://github.com/lzocateli/DotnetScope.git
   git fetch upstream
   git switch -c docs/descricao-curta upstream/main
   ```

   Substitua `docs/descricao-curta` pelo tipo e nome da sua mudança. Os comandos
   criam apenas uma branch local e não alteram a `main` remota.
3. Atualize testes, referência da CLI e documentação necessários; valide o menor recorte primeiro e registre evidências no PR. A validação completa, igual à do CI, é `pwsh ./tools/scripts/Invoke-Validation.ps1 -Aot`.
4. Testes que instalam .NET ou editam perfil, registro ou `PATH` rodam apenas em containers ou runners efêmeros, nunca no seu perfil.
5. Use os [Conventional Commits](COMMIT_CONVENTIONS.md). Envie a branch para
   seu fork (`git push -u origin docs/descricao-curta`) e abra um PR com base
   `lzocateli/DotnetScope:main`, preenchendo o template e vinculando a Issue
   (`Closes #numero` quando aplicável). Use draft enquanto faltar evidência.
6. Aguarde a revisão de `@lzocateli`. Responda aos comentários e atualize a
   branch com a `main` quando necessário; alterações invalidam aprovações antigas.
   Workflows de colaboradores externos aguardam autorização do mantenedor.
   Somente ele faz merge, após os checks obrigatórios e a resolução das conversas.

O envio da branch ao próprio fork faz parte do fluxo de contribuição. Agentes
continuam precisando de solicitação explícita para commit, push, merge ou
publicação. Acesso de escrita ao repositório original não é necessário nem
concedido automaticamente; consulte a [governança](GOVERNANCE.md).

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

Em mudanças apenas documentais, marque os gates de código não aplicáveis como
`N/A`, com justificativa; confira links, exemplos e coerência com a CLI. Os
checks automáticos obrigatórios continuam exigidos no GitHub.

O mantenedor pode solicitar ajustes, adiar ou recusar propostas fora do escopo.
Abertura de Issue ou PR não garante implementação, aceite ou prazo de resposta.
