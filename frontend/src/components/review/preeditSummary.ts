import type { EditPlanDto, EditSuggestion } from '../../types/ipc';

// Painel de controle da pre-edicao (F14.8). Funcoes puras: sem React e sem IPC.
// Origem vem do plano vigente (appliedCuts[].origin) e o status efetivo vem das sugestoes.
// Elegibilidade para lote NAO e decidida aqui: o host define; a UI apenas exclui o que ja foi aplicado.

export type PreeditOutcome = 'autoApplied' | 'userApplied' | 'pending' | 'deferred' | 'notApplied' | 'conflict';

export const PREEDIT_OUTCOME_LABEL: Record<PreeditOutcome, string> = {
  autoApplied: 'Aplicado automaticamente',
  userApplied: 'Aplicado pelo usuário',
  pending: 'Pendente de revisão',
  deferred: 'Pendente de revisão',
  notApplied: 'Não aplicado',
  conflict: 'Conflito'
};

/**
 * Situação efetiva de uma sugestão.
 * - Rejeitada: "Não aplicado".
 * - Presente em appliedCuts do plano: automático (auto_trivial / residual_sub_frame) ou pelo usuário (approved_by_user).
 * - Aprovada sem corte no plano vigente: "Conflito" (com plano); sem plano, conta como aplicada pelo usuário.
 * - Adiada e demais pendentes: "Pendente de revisão".
 */
export const outcomeOf = (s: EditSuggestion, plan: EditPlanDto | null = null): PreeditOutcome => {
  if (s.status === 'Rejected') return 'notApplied';
  const cut = plan && Array.isArray(plan.appliedCuts)
    ? plan.appliedCuts.find((c) => c.suggestionId === s.suggestionId)
    : undefined;
  if (cut) return cut.origin === 'approved_by_user' ? 'userApplied' : 'autoApplied';
  if (s.status === 'Approved') return plan && Array.isArray(plan.appliedCuts) ? 'conflict' : 'userApplied';
  if (s.status === 'Deferred') return 'deferred';
  return 'pending';
};

export interface PreeditSummary {
  total: number;
  autoApplied: number;
  userApplied: number;
  pending: number;
  notApplied: number;
  conflict: number;
  /** Sugestões com redo disponível (decisão desfeita que pode ser restaurada). Não é exclusivo das outras contagens. */
  restored: number;
  /** Tempo removido no plano vigente, em segundos da fonte; null sem plano. */
  removedSourceSeconds: number | null;
  /** Clipes com zoom aplicado no plano vigente (zoom diferente de 1); null sem plano. */
  zoomsApplied: number | null;
}

const countZoomClips = (plan: EditPlanDto): number => {
  const clips = Array.isArray(plan.clips) ? plan.clips : [];
  const ids = new Set(
    clips.filter((c) => typeof c.zoom === 'number' && Number.isFinite(c.zoom) && c.zoom !== 1).map((c) => c.clipId)
  );
  return ids.size;
};

/** Contagens reais do painel, derivadas de sugestões + plano vigente. */
export const derivePreeditSummary = (suggestions: EditSuggestion[], plan: EditPlanDto | null): PreeditSummary => {
  const summary: PreeditSummary = {
    total: suggestions.length,
    autoApplied: 0,
    userApplied: 0,
    pending: 0,
    notApplied: 0,
    conflict: 0,
    restored: 0,
    removedSourceSeconds: null,
    zoomsApplied: null
  };
  for (const s of suggestions) {
    switch (outcomeOf(s, plan)) {
      case 'autoApplied':
        summary.autoApplied += 1;
        break;
      case 'userApplied':
        summary.userApplied += 1;
        break;
      case 'pending':
        summary.pending += 1;
        break;
      case 'notApplied':
        summary.notApplied += 1;
        break;
      case 'conflict':
        summary.conflict += 1;
        break;
      default:
        break; // adiadas aparecem apenas no filtro "Adiados"
    }
    if (s.canRedo === true) summary.restored += 1;
  }
  if (plan) {
    const us = plan.summary?.removedSourceUs;
    summary.removedSourceSeconds = typeof us === 'number' && Number.isFinite(us) ? us / 1_000_000 : null;
    summary.zoomsApplied = countZoomClips(plan);
  }
  return summary;
};

/** Texto PT-BR do tempo removido ("12,3 s"); "—" quando indisponível. */
export const formatRemovedSeconds = (seconds: number | null): string =>
  seconds === null ? '—' : `${seconds.toFixed(1).replace('.', ',')} s`;
