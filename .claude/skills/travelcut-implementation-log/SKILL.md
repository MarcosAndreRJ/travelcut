---
name: travelcut-implementation-log
description: "Mantém checklist, diário persistente feito/escrito/testado e retomada entre fases automaticamente."
---

# Procedimento


1. Ler `docs/16-OPERACAO-E-CHECKPOINTS.md`.
2. Antes de cada fase, adicionar microtarefas a `progresso/CHECKLIST.md` e marcar `STATE.md=in_progress`.
3. **IMEDIATAMENTE após cada entrega testada** adicionar entrada cronológica a `progresso/IMPLEMENTATION_LOG.md`: horário real, agente, FEITO/ESCRITO/FALHOU, arquivos, evidência e próximo passo.
4. Atualizar checklist após evidência; não marcar [x] por previsão. Sempre manter próximo checkpoint e impedimento em STATE.
5. Gate passou? Opus atualiza status e lê próximo prompt **durante a sessão atual**; se bloqueio, documentar/aguardar. Nenhum trabalho assíncrono prometido.
