# Detecção de secrets

Este repositório bloqueia secrets em duas camadas complementares: um hook local para o
diff staged e o job `Gitleaks / Full History` (workflow gerenciado `gw-gitleaks.yml`,
agregado ao check obrigatório `Result` do `ci.yml`) para todo o histórico
Git alcançável no runner. Ambos executam exclusivamente a imagem fixada
`docker.io/lzocateli/gitleaks:8.30.1` por Docker ou Podman (rootless). A variável
`DOTNETSCOPE_CONTAINER_ENGINE` (`docker` ou `podman`) força o engine local;
sem ela, o hook usa Docker e, na ausência dele, Podman.

A configuração versionada em `.gitleaks.toml` herda o catálogo padrão do
Gitleaks e adiciona regras locais para credenciais em connection strings,
headers Basic/Bearer e credenciais codificadas de Docker. Não existe uma regra
que detecte literalmente todos os secrets sem falsos positivos: a prática é
combinar o catálogo padrão, regras locais estreitas, push protection, revisão e
rotação. O hook local e o workflow devem sempre usar essa configuração; uma
referência a `${{ secrets.NOME }}` não é o valor do secret e pode permanecer no
workflow.

## Regra de tolerância zero

Nenhum secret pode ser commitado em qualquer parte do repositório: código,
testes, documentação, exemplos, scripts, imagens, manifests ou workflows do
GitHub. Isso inclui connection strings, senhas de teste, tokens, chaves,
certificados privados e valores que apenas “parecem” credenciais.

Workflows usam secrets de um Environment do GitHub, nunca valores literais no
YAML. Registre aqui os secrets que cada Environment exige, por nome, sem valor.

Crie-os em **Settings > Environments > ambiente correspondente > Environment secrets**:
`HOMEBREW_TAP_TOKEN` em `release` e `WINGET_CREATE_GITHUB_TOKEN` em `winget`,
conforme o [processo de release](../docs/release.md). A validação de PR não
exige Environment `ci` nem secrets de publicação. O valor é
digitado diretamente no GitHub e não deve ser copiado para o repositório, logs,
issues, pull requests ou mensagens. Pull requests usam apenas valores efêmeros
não secretos, pois secrets de ambientes protegidos não são disponibilizados a
PRs.

## Instalação local obrigatória

Na raiz de cada clone, com PowerShell 7, Git e Docker ou Podman disponíveis:

```powershell
./tools/scripts/install-gitleaks-hook.ps1
git config --local --get core.hooksPath
git hook run pre-commit
```

O segundo comando deve retornar `.githooks`, e o smoke test deve terminar com
`no leaks found`. A configuração é local ao clone e deve ser refeita após cada
novo clone. Não ignore o hook com `--no-verify`.

## Comportamento dos controles

O hook envia `git diff --cached --no-ext-diff --no-textconv` ao Gitleaks por
`stdin`. Ele não lê arquivos fora do índice e falha fechado quando nem Docker nem
Podman estão disponíveis.

O workflow faz checkout com histórico completo e usa o Git do runner para enviar
`git log --all --full-history -p --` ao mesmo scanner por `stdin`. A imagem é
minimalista e não precisa conter o executável `git`.

## Tratamento de achados

1. Interrompa o commit ou merge e não publique o valor encontrado.
2. Revogue ou rotacione imediatamente qualquer credencial real.
3. Remova o secret da árvore e, quando necessário, reconstrua o histórico.
4. Reexecute o hook e o check de histórico completo.
5. Trate falso positivo somente por exceção mínima, documentada e revisada.

Se um achado estiver em workflow ou configuração de CI, interrompa a execução,
revogue o valor se ele for real, substitua-o por uma referência a
`${{ secrets.NOME_DO_SECRET }}` e confirme que o valor foi criado no Environment
correto do GitHub. Não use `${{ secrets.* }}` como justificativa para commitar o
valor real em outro arquivo.

Não adicione allowlists automáticas nem exceções amplas. A mera remoção do
arquivo no commit atual não elimina um secret já presente no histórico.

## Checklist administrativo

- [ ] Exigir o check `Result`, que agrega `Gitleaks / Full History`, no ruleset
  `gw-main-integrity` (aplicado por `groundwork github --sync`; ver [governança](GOVERNANCE.md)).
- [x] Habilitar secret scanning e push protection nativos do GitHub
  (ambos confirmados pela API em 2026-10-22).
- [ ] Confirmar `core.hooksPath=.githooks` nos clones administrativos.
- [ ] Revisar e registrar qualquer exceção antes de alterar a configuração do scanner.

As proteções nativas do GitHub complementam e não substituem o hook e o workflow.
