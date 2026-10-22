---
name: "CI e release"
description: "Use ao criar ou alterar GitHub Actions, Dependabot, matriz de build Native AOT, release, checksums, assinatura, SBOM, proveniência ou canais de distribuição do DotnetScope."
applyTo: ".github/workflows/**, .github/dependabot.yml, .github/release.yml"
---

# CI e release

- Fixe actions por SHA e registre a versão humana em comentário.
- Use `permissions: {}` no workflow e permissões mínimas por job.
- Pull requests não recebem secrets; chaves de assinatura ficam em Environment protegido `release`.
- Nenhum secret, token ou chave privada é versionado em código, configuração, documentação ou workflow.
- Separe restore, format, build, testes unitários, integração, E2E e publicação AOT.
- Publique Native AOT por RID no próprio SO: `linux-x64`, `linux-arm64`, `linux-musl-x64`, `win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`. Linux glibc compila em base antiga para maximizar compatibilidade.
- Publicação AOT falha com qualquer aviso de trimming ou AOT.
- E2E de cada RID roda o binário publicado em ambiente limpo, sem .NET pré-instalado.
- Release gera arquivos `tar.gz`/`zip`, `.deb`, instaladores, `SHA256SUMS`, SBOM e atestações Sigstore keyless de proveniência e SBOM. Authenticode e Developer ID dos binários próprios estão fora da F9 sem custo; não os confunda com a verificação de assinatura dos instaladores oficiais da Microsoft.
- Artefatos publicados devem ser imutáveis; a limitação atual de reexecução está registrada no [processo de release](../../docs/release.md#limitações-conhecidas).
- Versões seguem SemVer; tags `vX.Y.Z` disparam publicação somente após aprovação no Environment `release`.
- Cacheie somente conteúdo reprodutível; restore usa lock file em modo travado.
- Não adicione workflow antes de existir comando local equivalente e validado em `tools/scripts/`.
- Workflows de E2E que alteram perfil, registro ou `PATH` usam apenas runners efêmeros ou containers.
- Falha de segurança crítica ou alta bloqueia release.
