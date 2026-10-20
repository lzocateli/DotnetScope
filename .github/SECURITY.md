# Política de segurança

> **Canal em preparação:** o GitHub Private Vulnerability Reporting deve ser
> habilitado antes da primeira divulgação pública. Até sua confirmação, não
> publique detalhes técnicos de vulnerabilidades em issues ou discussões.

## Versões suportadas

O DotnetScope ainda não possui release suportada. Antes da primeira versão pública,
esta seção será substituída por uma matriz de versões e datas de fim de suporte.

## Escopo

O DotnetScope baixa e instala binários .NET, pode executar com privilégios
administrativos no escopo `global` e altera arquivos de perfil, registro e
`PATH`. Estão no escopo, entre outros:

- download ou instalação de artefato sem verificação de integridade;
- extração fora do diretório de destino (path traversal, links, entradas especiais);
- escalonamento de privilégio ou execução indevida como administrador;
- injeção de comandos em arquivos de perfil, registro ou argumentos de processo;
- sobrescrita ou remoção de instalações, perfis ou configurações de terceiros;
- comprometimento dos binários, checksums, assinaturas ou canais de distribuição.

## Como reportar

Não abra issue pública para vulnerabilidades. Use
[Report a vulnerability](https://github.com/lzocateli/DotnetScope/security/advisories/new)
na aba **Security** do repositório. Se o recurso não estiver disponível, aguarde a
publicação de um canal privado oficial; não envie detalhes a endereços inferidos
nem por comentários públicos.

Inclua, quando possível:

- versão ou commit afetado;
- ambiente e sistema operacional;
- pré-condições e impacto;
- passos mínimos de reprodução;
- prova de conceito sanitizada;
- mitigação conhecida.

Não envie credenciais reais, chaves privadas, dados de terceiros ou dumps completos.

Não acesse dados de terceiros, não mantenha persistência e não execute testes
destrutivos fora de ambientes sob seu controle.

## Resposta esperada

- confirmação inicial em até 3 dias úteis;
- triagem e classificação em até 7 dias úteis;
- comunicação coordenada da correção conforme impacto e complexidade;
- crédito ao pesquisador quando solicitado e permitido.

Os prazos são objetivos operacionais, não garantia contratual.

## Prioridade

Recebem prioridade máxima vulnerabilidades que permitam:

- executar código ou comando sem autorização, inclusive por meio do perfil do shell;
- instalar artefato adulterado ou não verificado;
- gravar ou remover arquivos fora do destino autorizado;
- obter privilégio administrativo ou afetar outros usuários a partir do escopo `user`;
- obter tokens, certificados, secrets ou dados protegidos;
- comprometer binários, atualizações ou cadeia de suprimentos.

## Resposta coordenada

Correções incluem teste de regressão, análise de variantes e atualização de
dependências, binários, checksums, SBOM e orientação de mitigação quando aplicável. A
divulgação ocorre depois da disponibilização da correção e de prazo razoável para
atualização, salvo exploração ativa que exija comunicação imediata.

Não há programa de recompensa declarado.
