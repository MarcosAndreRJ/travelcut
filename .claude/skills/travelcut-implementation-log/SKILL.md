---
name: travelcut-implementation-log
description: "Mantem checklist, diario persistente feito/escrito/testado e rastreabilidade de execucao."
---
# Procedimento de Log e Rastreabilidade

1. Consultar `docs/16-OPERACAO-E-CHECKPOINTS.md` quando necessario para contratos de checkpoint.
2. Registrar microtarefas delimitadas no checklist do incremento atual.
3. **Imediatamente apos cada entrega testada:** registrar entrada cronologica em `progresso/IMPLEMENTATION_LOG.md` com data/hora, agente responsavel, status FEITO/ESCRITO/FALHOU, arquivos afetados, evidencias e proximo passo.
4. Nao marcar itens como concluidos sem evidencia real de teste.
5. Manter rastreabilidade de bloqueios e impedimentos em `progresso/STATE.md`.
6. Concluido o incremento autorizado pelo usuario, o orquestrador Fable consolida os resultados e encerra. Nao avancar automaticamente para fases futuras nao solicitadas.
