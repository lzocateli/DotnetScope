# Política de segurança

> **Canal privado habilitado:** GitHub Private Vulnerability Reporting,
> confirmado pela API em 2026-10-22. Não publique detalhes técnicos de
> vulnerabilidades em Issues ou discussões.

## Versões suportadas

Versões pré-1.0, incluindo a
[v0.4.0](https://github.com/lzocateli/DotnetScope/releases/tag/v0.4.0), não têm
suporte de segurança nem backports. Atualize para a release mais recente; não
há suporte retroativo implícito para versões já publicadas.

A partir de `1.0.0`, somente a linha minor `1.x` mais recente é suportada.
Correções de segurança são publicadas nessa linha; uma minor anterior deixa de
ser suportada quando uma minor mais nova é publicada. O suporte da linha `1.x`
termina com a publicação de `2.0.0`. Não há SLA contratual; os objetivos
operacionais de resposta desta política continuam descritos em
[Resposta esperada](#resposta-esperada).

Ao relatar uma vulnerabilidade, informe todas as versões afetadas conhecidas e,
quando seguro, se o problema ocorre na
[release mais recente](https://github.com/lzocateli/DotnetScope/releases/latest).
O canal privado permanece disponível independentemente da versão afetada.

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
