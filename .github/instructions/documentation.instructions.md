---
name: "Documentação"
description: "Use ao criar ou alterar README, documentação técnica, runbook, ADR, plano ou referência da CLI do DotnetScope."
applyTo: "**/*.md, docs/**"
---

# Documentação

- Escreva em português brasileiro, com termos técnicos consistentes. Preserve comandos, opções, identificadores e labels literais exigidos pelas ferramentas.
- O README raiz é autoridade para visão, escopo e uso; `.github/` define procedimentos; `docs/plano-implementacao.md` é a fonte das fases, decisões e critérios de aceite.
- Mudanças de opção, saída `--output json`, código de saída, variável de ambiente ou arquivo de perfil alterado atualizam README, `docs/` e o plano no mesmo PR.
- Não duplique conteúdo extenso; use links entre documentos proprietários.
- ADRs vivem em `docs/adr`, usam `AAAA-NNNN-titulo.md`, seguem `.github/ADR_TEMPLATE.md` e começam como `proposed`.
- ADRs registram decisões duradouras; não substituem requisitos, plano ou autorização. Registre origem, revisor humano e referências.
- IA não marca ADR como `accepted` nem decisão do plano como aprovada sem decisão humana explícita e referenciada.
- README, plano e ADRs aceitos restringem mudanças. Conflitos exigem decisão humana; não crie fonte paralela de requisitos em guias, prompts ou Issues.
- Runbooks incluem sinais, diagnóstico, mitigação, recuperação e escalonamento.
- Comandos informam diretório, pré-requisitos, resultado esperado e impacto, inclusive arquivos de perfil, registro ou `PATH` alterados.
- Exemplos usam valores fictícios; nunca inclua secret, host interno, nome de usuário ou caminho pessoal real.
- Registre limitações, matriz suportada (SO, distribuição, arquitetura, shell e escopo), riscos e validações não executadas.
- Links locais devem resolver e headings devem ser únicos.
- Diferencie requisito, proposta, decisão aceita e comportamento implementado.
