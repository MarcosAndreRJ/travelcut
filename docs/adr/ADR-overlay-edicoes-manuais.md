# ADR — Overlay de edições manuais da timeline

- Estado: Aprovada (parecer do `senior-analyst`, recomendação A; implementada na F14.7)
- Escopo: `PlanManualEdit`, `ManualEditDeriver`, `ManualEdits.Compact`, `PlanReconciler.Apply`, `ManualPrecedence`, `PlanningUseCases` (save/generate/decisão/pós-análise/reconcile)

## Contexto
Toda regeneração (decisão, pós-análise, `plan.generate`) reconstruía o plano automático e superseedia a revisão manual. A edição do usuário não guardava intenção, só o resultado, e não sobrevivia a uma mudança de entradas.

## Decisão
1. **Overlay ancorado na fonte.** A edição manual é um conjunto `E` de `PlanManualEdit` (exclude, include, split, place, duplicate, property) em µs por mídia, gravado no JSON da revisão (`manualEdits`, aditivo; esquema `editplan/1` mantido).
2. **Save baseado em estado.** O servidor grava `Apply(Build(entradas atuais), Compact(E_base + DeriveDelta(rBase, clipes do cliente)))`. Só a diferença de estado conta; sem diferença não há revisão nova (editar e desfazer no cliente não grava nada). Perfil, base de tempo e fuso vêm da revisão vigente.
3. **Regeneração reaplica o overlay.** Decisão, pós-análise e `plan.generate` fazem `Apply(auto, E)`; o autor passa a `planner-rules/1+manual`. Sem overlay o plano automático é devolvido intacto e o fingerprint de entradas antigo não muda.
4. **Precedência.** Edição manual > config do projeto > política automática. Exceção única: decisão humana efetiva (approve/reject no topo da pilha) com `BaseRevision >= IntroducedInRevision` que sobreponha o trecho da edição vence. A edição é **ignorada na leitura**, continua gravada no overlay, e desfazer a decisão a reativa.
5. **Recusa segura.** Se o overlay não pode ser reaplicado (revisão ilegível, adulterada por fingerprint, base de manual legada ausente, `Apply` recusa estruturalmente), nada é gravado: `PLAN_MANUAL_EDITS_UNREADABLE` (ilegível) ou `PLAN_MANUAL_PROTECTED` com `details = suggestedActions:reconcile,discard_manual_edit`. Vale também para `plan.reconcile` (prévia), que não devolve `AUTOGEN_FAILED` nesse caso.
6. **Migração.** Revisão manual legada (sem `manualEdits`) é migrada na primeira regeneração/save por `Derive(base lida do disco, revisão manual)`, com profundidade limitada.

## Alternativas descartadas
- Log de operações do cliente como fonte da verdade (frágil a undo/redo e a revisões obsoletas).
- Merge de três vias entre plano automático novo e plano manual (ressuscita clipes apagados, sem relayout).
- Recusar toda regeneração com revisão manual vigente (bloqueia decisões e pós-análise).

## Consequências e limitações conhecidas
- `Compact`: exclude seguido de re-include do mesmo trecho vira `restore` com a velocidade do automático (não preserva velocidade custom anterior).
- `Apply` valida estrutura e invariantes I1–I10, mas não roda o `PlanValidation.Run` completo; a validação persistida é a estrutural, e as demais são derivadas na leitura.
- Edições ignoradas por decisão posterior são diagnosticadas só em log/testes (sem campo na UI).
- Originais de mídia nunca são tocados: o overlay vive no JSON da revisão.

## Validação
`PlanManualOverlayTests` (R1, R4, R5, precedência ponta a ponta), `PlanReconcilerApplyTests`, `ManualEditPropertyTests` (I1–I10, 700 sequências, seed fixa) e `EndToEndManualOverlayAcceptanceTests` (IPC, reinício, SHA-256 dos originais).
