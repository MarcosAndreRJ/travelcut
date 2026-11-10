import type { EditPlanDto, PlanEditorialOperationDto } from '../../types/ipc';

/**
 * Estado puro do editor de timeline (sem React), usado por useTimelineEditor.
 *
 * Histórico contínuo: salvar NÃO zera o histórico. O marcador `savedIndex` indica qual
 * entrada corresponde ao último conteúdo gravado no host; "sujo" = index !== savedIndex.
 * O log de operações é derivado das entradas do histórico (não é fonte de verdade).
 */

export interface PlanRef {
  planId: string;
  revision: number;
}

export interface HistoryEntry {
  plan: EditPlanDto;
  /** Operação editorial que produziu esta entrada (null na entrada inicial). */
  op: PlanEditorialOperationDto | null;
}

export interface TimelineHistory {
  entries: HistoryEntry[];
  index: number;
  /** Índice gravado no host; -1 quando o estado salvo saiu do histórico (sempre sujo). */
  savedIndex: number;
}

export const EMPTY_HISTORY: TimelineHistory = { entries: [], index: 0, savedIndex: 0 };

export function createHistory(plan: EditPlanDto): TimelineHistory {
  return { entries: [{ plan, op: null }], index: 0, savedIndex: 0 };
}

export function currentPlan(h: TimelineHistory): EditPlanDto | null {
  return h.entries[h.index]?.plan ?? null;
}

export function canUndoHistory(h: TimelineHistory): boolean {
  return h.index > 0;
}

export function canRedoHistory(h: TimelineHistory): boolean {
  return h.index < h.entries.length - 1;
}

export function isHistoryDirty(h: TimelineHistory): boolean {
  return h.index !== h.savedIndex;
}

/** Nova edição: descarta o ramo de refazer. Se o estado salvo estava nesse ramo, fica sujo. */
export function pushHistory(
  h: TimelineHistory,
  plan: EditPlanDto,
  op: PlanEditorialOperationDto | null = null
): TimelineHistory {
  const entries = [...h.entries.slice(0, h.index + 1), { plan, op }];
  const savedIndex = h.savedIndex > h.index ? -1 : h.savedIndex;
  return { entries, index: entries.length - 1, savedIndex };
}

export function undoHistory(h: TimelineHistory): TimelineHistory {
  return canUndoHistory(h) ? { ...h, index: h.index - 1 } : h;
}

export function redoHistory(h: TimelineHistory): TimelineHistory {
  return canRedoHistory(h) ? { ...h, index: h.index + 1 } : h;
}

/**
 * Marca a entrada `idx` (que foi enviada como `sent`) como gravada, trocando-a pela revisão
 * devolvida pelo host. Se a entrada já não existe mais com o mesmo conteúdo, o estado salvo
 * fica indefinido e o histórico permanece sujo.
 */
export function markHistorySaved(
  h: TimelineHistory,
  idx: number,
  sent: EditPlanDto,
  saved: EditPlanDto
): TimelineHistory {
  if (h.entries[idx]?.plan !== sent) return { ...h, savedIndex: -1 };
  const entries = h.entries.map((e, i) => (i === idx ? { ...e, plan: saved } : e));
  return { ...h, entries, savedIndex: idx };
}

/** Operações do ramo atual desde o último salvamento (para o save informativo). */
export function operationsSinceSaved(h: TimelineHistory): PlanEditorialOperationDto[] {
  const from = h.savedIndex < 0 ? 0 : h.savedIndex + 1;
  const ops: PlanEditorialOperationDto[] = [];
  for (let i = from; i <= h.index; i++) {
    const op = h.entries[i]?.op;
    if (op) ops.push(op);
  }
  return ops;
}

export type PlanIncomingMode = 'external' | 'reload';
export type IncomingDecision = 'ignore' | 'adopt' | 'rebase' | 'banner';

/**
 * Decide o que fazer quando chega um plano vindo de fora do editor.
 * - mesma planId/revision da base: nada (eco do próprio save ou mesma revisão);
 * - sem edições: adota a revisão nova;
 * - com edições e recarga explícita do mesmo plano: rebase leve (mantém edições, troca a base);
 * - com edições em qualquer outro caso: mostra aviso e preserva as edições.
 */
export function decideIncoming(
  base: PlanRef | null,
  incoming: PlanRef,
  dirty: boolean,
  mode: PlanIncomingMode
): IncomingDecision {
  if (base && base.planId === incoming.planId && base.revision === incoming.revision) return 'ignore';
  if (!dirty) return 'adopt';
  if (mode === 'reload' && base && base.planId === incoming.planId) return 'rebase';
  return 'banner';
}

export interface SaveErrorInfo {
  code: string | null;
  message: string;
  /** Oferece a ação "Recarregar" (rebase sobre a revisão vigente, sem perder as edições). */
  reloadable: boolean;
}

/** Mensagens PT-BR para os erros de plan.saveManualRevision (contrato F14.7a). */
export function classifySaveError(code: string | null, fallback: string): SaveErrorInfo {
  switch (code) {
    case 'PLAN_STALE_BASE':
      return {
        code,
        message: 'A revisão vigente mudou desde que você começou a editar. Suas edições continuam aqui; recarregue a base e salve de novo.',
        reloadable: true
      };
    case 'PLAN_PROJECT_MISMATCH':
      return {
        code,
        message: 'Este plano pertence a outro projeto. Recarregue o projeto ativo; suas edições continuam aqui.',
        reloadable: true
      };
    case 'PLAN_ID_MISMATCH':
      return {
        code,
        message: 'O plano editado não corresponde ao plano deste projeto. Recarregue; suas edições continuam aqui.',
        reloadable: true
      };
    case 'PLAN_NOT_FOUND':
      return {
        code,
        message: 'Não há plano base para gravar esta edição. Recarregue o plano; suas edições continuam aqui.',
        reloadable: true
      };
    default:
      return { code, message: fallback || 'Falha ao salvar a revisão.', reloadable: false };
  }
}
