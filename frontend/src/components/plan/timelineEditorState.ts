import type { EditPlanDto, PlanClipDto, PlanEditorialOperationDto } from '../../types/ipc';
import { deleteClips, duplicateClips, moveClips, splitClips } from './timelineOperations';

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

/** Maior revisão conhecida entre as fontes informadas (ignora nulos). */
export function resolveLatestRevision(...values: (number | null | undefined)[]): number | null {
  let max: number | null = null;
  for (const v of values) {
    if (typeof v === 'number' && (max === null || v > max)) max = v;
  }
  return max;
}

/** A base do editor é anterior à revisão mais nova conhecida: edição bloqueada. */
export function isBaseOutdated(base: PlanRef | null, latest: number | null): boolean {
  return base !== null && latest !== null && base.revision < latest;
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

/** Recalcula a duração da timeline no summary (mesma regra do commit do editor). */
export function withTimelineDuration(plan: EditPlanDto): EditPlanDto {
  const maxEnd = (plan.clips ?? []).reduce((acc, c) => Math.max(acc, c.timelineStartFrame + c.durationFrames), 0);
  return { ...plan, summary: { ...plan.summary, timelineDurationFrames: maxEnd } };
}

function operationClipIds(op: PlanEditorialOperationDto): Set<string> {
  return new Set([op.clipId, ...(op.linkedClipId ? [op.linkedClipId] : [])]);
}

/**
 * Reaplica UMA operação do log do editor sobre a lista de clipes da revisão nova.
 * Retorna null quando a operação não é aplicável (clipe ou par vinculado inexistente,
 * limites inválidos, tipo sem suporte). Reutiliza as funções puras de timelineOperations.
 */
export function applyOperationToClips(
  clips: PlanClipDto[],
  op: PlanEditorialOperationDto,
  timeBase: EditPlanDto['timeBase'],
  idSuffix: string
): PlanClipDto[] | null {
  if (!clips.some((c) => c.clipId === op.clipId)) return null;
  if (op.linkedClipId && !clips.some((c) => c.clipId === op.linkedClipId)) return null;

  switch (op.type) {
    case 'MOVE': {
      if (op.newStartFrame == null) return null;
      return moveClips(clips, op.clipId, op.newStartFrame);
    }
    case 'TRIM': {
      const { newStartFrame, newDurationFrames, sourceInUs, sourceOutUs } = op;
      if (newStartFrame == null || newDurationFrames == null || sourceInUs == null || sourceOutUs == null) return null;
      if (newDurationFrames <= 0 || sourceOutUs <= sourceInUs) return null;
      const ids = operationClipIds(op);
      return clips.map((c) =>
        ids.has(c.clipId)
          ? { ...c, timelineStartFrame: newStartFrame, durationFrames: newDurationFrames, sourceInUs, sourceOutUs }
          : c
      );
    }
    case 'SPLIT': {
      if (op.splitAtFrame == null) return null;
      const out = splitClips(clips, op.clipId, op.splitAtFrame, timeBase, idSuffix);
      return out === clips ? null : out;
    }
    case 'DUPLICATE':
      return duplicateClips(clips, op.clipId, idSuffix);
    case 'DELETE':
      return deleteClips(clips, op.clipId);
    default:
      return null;
  }
}

export interface ReapplyResult {
  /** Histórico sobre a revisão nova: entrada 0 = revisão nova; demais = operações reaplicadas. */
  history: TimelineHistory;
  /** Operações que não puderam ser reaplicadas (descartadas). */
  dropped: PlanEditorialOperationDto[];
}

/**
 * Reaplica as operações do usuário sobre a revisão nova. O delta contra a revisão nova passa a
 * conter só a intenção do usuário; o salvamento seguinte nunca usa o snapshot da base antiga.
 * `idStamp` torna os IDs das partes novas únicos dentro da reaplicação.
 */
export function reapplyOperations(
  latest: EditPlanDto,
  ops: PlanEditorialOperationDto[],
  idStamp: string = String(Date.now())
): ReapplyResult {
  let h = createHistory(latest);
  const dropped: PlanEditorialOperationDto[] = [];
  let seq = 0;
  for (const op of ops) {
    const current = currentPlan(h);
    seq += 1;
    const clips = current ? applyOperationToClips(current.clips ?? [], op, current.timeBase, `${idStamp}_${seq}`) : null;
    if (!current || !clips) {
      dropped.push(op);
      continue;
    }
    h = pushHistory(h, withTimelineDuration({ ...current, clips }), op);
  }
  return { history: h, dropped };
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
