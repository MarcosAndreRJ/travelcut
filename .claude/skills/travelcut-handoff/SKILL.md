---
name: travelcut-handoff
description: "Padroniza delegações aos agentes e integração sem conflitos no repositório."
---

# Procedimento


Ao delegar, enviar: `taskId`, objetivo único, documentos relevantes, paths permitidos, paths proibidos, entradas/saídas, testes e critério de pronto. Agentes de implementação podem alterar só escopo; `qa-auditor` somente leitura; `senior-analyst` para casos complexos.
No retorno exigir: status, resumo, arquivos escritos, testes realizados (comandos, sucesso/falha/não rodado), riscos, contratos afetados. Orquestrador integra/valida antes de escrever no log. No máximo 2 workers pesados na VM, nunca no mesmo arquivo simultaneamente.
