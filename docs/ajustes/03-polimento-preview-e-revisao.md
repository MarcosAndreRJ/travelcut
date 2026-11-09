# Prompt 03 — Polimento do preview e da revisão (limites do I5)

Executor: Haiku `frontend-engineer`, com Sonnet A para o que exigir backend. Não editar `useTimelineEditor`, `TimelinePlanningView`, `PlanTimelineCanvas` antes da F14.7.

1. "Depois" aplica também velocidade e zoom do plano nos clipes (`mediaTime.ts`, `MediaIntervalPlayer.tsx`). Candidato pendente que o plano não removeu não pode aparecer como "Depois (plano vigente)": rotular "Original".
2. Presets de zoom da UI (1,15 / 1,20 / 1,25) passam do teto do host (1,12): alinhar ao intervalo 1,01–1,12 e permitir "sem zoom" (hoje 1,0× não é expressável, porque `parameters:null` significa herdar). Definir contrato explícito e testar.
3. Consumir ponto focal e duração informados pelo usuário: backend (`appliedEffects[].focalPointX/Y`, duração) → `PlanClip.zoom` com foco/duração; o preview deve refletir.
4. Botões Desfazer/Restaurar por linha na fila (hoje só no detalhe).
5. Elegibilidade do lote: usar a flag vinda do backend em vez da regra local (`isBatchApprovable`, `analysisFormat.ts`); remover a duplicata de política no frontend.
6. Distinguir visualmente prévia aproximada HTML5, prévia validada e render final; mostrar a limitação quando um efeito não for reproduzível no WebView2 (sem simulação silenciosa).
7. Verificação visual no navegador (preview_start) com screenshots, em largura desktop e estreita.

Pronto quando: vitest dos arquivos tocados, `tsc` sem erros novos, nenhuma store concorrente.
