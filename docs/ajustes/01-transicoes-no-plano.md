# Prompt 01 — Transições (fade/dissolve) materializadas no plano (E05)

Contexto: `EditorialEffectsService.ValidateHandlesAsync` e `TransitionRecommender` já validam handles de verdade e ordenam pela `ChronologyService` (commit 7ac027c). Mas as transições editoriais (`EditorialTransition`, SQLite) ainda são um modelo paralelo: o `EditPlanner` só emite "cut"/"dissolve" por seção e não lê sugestões de transição. `TransitionRecommender` ainda grava dissolve sem conhecer as bordas dos clipes.

Missão (Sonnet B no backend; Haiku para tipos TS aditivos):
1. Estender `PlanClip`/DTO de forma aditiva para carregar a transição de saída/entrada (fade | dissolve | crossfade, duração em frames, origem auto|human, policyVersion, razão).
2. `EditPlanner` materializa transições aprovadas (humano) ou autoaprovadas pela `AutoEditorialPolicy` (nova versão): fade simples em fronteira com handles validados → Apply (Equilibrado/Agressivo; Conservador não); dissolve complexa ou transição entre arquivos → Review; fronteira sem handle suficiente ou que prejudica a continuidade → Skip. Limitar a densidade (não transformar todo corte em animação).
3. Mesma precedência de decisão (manual > projeto > política), idempotência e undo via histórico de decisões; incluir no `PlanInputsFingerprint` só quando existirem.
4. Gerar sugestões de transição no `PostAnalysisOrchestrator` (como o zoom), usando as bordas reais dos clipes do plano.
5. Preview: `PlanPreviewPlayer` deve representar fade/dissolve ou rotular a limitação (sem simular silenciosamente). Não editar `useTimelineEditor`, `TimelinePlanningView`, `PlanTimelineCanvas` enquanto a F14.7 não estiver integrada.

Testes: handles 0 + dissolve → Review/Skip; fade válido → Apply por perfil; idempotência; undo; projeto antigo.
Pronto quando: `dotnet test tests/TravelCut.UnitTests` verde + vitest dos arquivos tocados; um commit por item.
