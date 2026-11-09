# Prompt 04 — Edição manual vs automação (E01–E03, E11)

**BLOQUEADO até a F14.7 estar na main.** Verificar `git log main` por F14.7; se ausente, não executar. Se presente, partir de uma worktree nova a partir da main atualizada.

Executor: Sonnet A, com parecer prévio do `senior-analyst` (Opus) — risco de perda de dados e de reconciliação.

- **E01** — persistir edições manuais como overrides/operações por clipe ou intervalo e reaplicá-las sobre o plano regenerado (ou impedir a regeneração sobre revisão manual). Hoje `SaveManualRevisionAsync` grava `inputsFingerprint:null` e é sobrescrito pela próxima decisão.
- **E02** — `plan.reconcile` não pode persistir revisão automática como efeito colateral; persistir só a revisão reconciliada, com o perfil da revisão base; gerar o candidato sem persistir.
- **E03** — `PlanReconciler` precisa de relayout (ripple, sem overlap nem buracos, A1/V1 pareados), respeitar `operations` e tratar `CUT_REOPENED` sem deixar clipe manual sobre trecho removido.
- **E11** — `PlanId` vs `ProjectId` e `WithProjectLockAsync` em `SaveManualRevisionAsync`.
- Precedência garantida e testada: decisão manual explícita > config do projeto > política automática (restaurou pausa, rejeitou zoom, ajustou corte, mudou enquadramento).

Testes: salvar revisão manual + aprovar sugestão não relacionada → clipes manuais intactos; reconcile sem criar revisão; corte novo dentro de clipe manual → timeline sem overlap; reabrir → mesma montagem.
