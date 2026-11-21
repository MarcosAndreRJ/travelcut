import type {
  DecisionResultDto,
  EditPlanDto,
  EditSuggestion,
  SubmitDecisionRequest,
  ZoomApplyRequest,
  ZoomApplyResponse
} from '../../types/ipc';
import {
  resolveZoomWindows,
  formatZoomScale,
  validateZoomInput,
  type SourceZoomWindow
} from '../media/zoomWindows';
import { intervalsOverlap } from './analysisFormat';
import { newRequestId, STALE_BASE_MESSAGE, PLAN_STALE_BASE_CODE, type CutFeedback, type CutFeedbackAction } from './directCutController';

// Controlador do zoom direto (edição por transcrição). Sem React e sem DOM.
// Só confirma zoom quando o host responde applied === true. Fonte da verdade: plano vigente (refresh),
// com fallback para suggestions.list. Remover zoom = decision.submit reject (preserva cortes).

export interface DirectZoomApi {
  applyDirectZoom(params: ZoomApplyRequest): Promise<ZoomApplyResponse>;
  undoDecision(suggestionId: string): Promise<DecisionResultDto>;
  redoDecision(suggestionId: string): Promise<DecisionResultDto>;
  submitDecision(request: SubmitDecisionRequest): Promise<DecisionResultDto>;
  listSuggestions(projectId: string): Promise<EditSuggestion[]>;
  getEditPlan(projectId: string, revision?: number | null): Promise<EditPlanDto | null>;
}

export const ZOOM_REVIEWER = 'Editor';
const REMOVE_NOTES = 'remover zoom (edição por transcrição)';
export const ZOOM_CLIP_OCCUPIED_CODE = 'ZOOM_CLIP_OCCUPIED';
export const ZOOM_CLIP_OCCUPIED_MESSAGE =
  'Este trecho já tem outro zoom. Só é permitido 1 zoom por trecho: remova o zoom existente antes de aplicar outro.';

export interface DirectZoomState {
  windows: SourceZoomWindow[];
  planRevision: number | null;
  canUndo: boolean;
  canRedo: boolean;
  busy: boolean;
  feedback: CutFeedback | null;
  loadError: string | null;
}

export interface ZoomApplyInput {
  mediaId: string;
  startSec: number;
  endSec: number;
  scale: number;
  focalPointX: number;
  focalPointY: number;
  transitionInSeconds: number;
  transitionOutSeconds: number;
  rationale: string;
}

export type ZoomApplyOutcome =
  | {
      status: 'applied';
      suggestionId: string;
      alreadyApplied: boolean;
      /** Janela efetiva no plano (fonte), para conferir o zoom na prévia. Null quando o host não devolveu. */
      effective: { startSec: number; endSec: number; transitionInSeconds: number } | null;
    }
  | { status: 'not_applied'; message: string }
  | { status: 'invalid'; message: string }
  | { status: 'error'; code: string | null; message: string };

export type ZoomRemoveOutcome =
  | { status: 'removed'; suggestionIds: string[] }
  | { status: 'none' }
  | { status: 'error'; message: string };

interface HistoryEntry {
  suggestionId: string;
  kind: 'apply' | 'remove';
}

const messageOf = (err: unknown): string => (err instanceof Error ? err.message : String(err));
const codeOf = (err: unknown): string | null => {
  const code = (err as { code?: unknown } | null)?.code;
  return typeof code === 'string' ? code : null;
};

/** Texto PT-BR para os códigos de zoom/plano devolvidos pelo host. */
export function describeZoomPlanCode(code: string): string {
  switch (code) {
    case 'ZOOM_NOT_APPLIED':
      return 'o plano foi gerado, mas o zoom não entrou nele';
    case ZOOM_CLIP_OCCUPIED_CODE:
      return ZOOM_CLIP_OCCUPIED_MESSAGE;
    case 'PLAN_MANUAL_PROTECTED':
      return 'o plano tem edições manuais protegidas e não foi regenerado';
    case 'PLAN_MANUAL_EDITS_UNREADABLE':
      return 'as edições manuais do plano não puderam ser lidas; o plano não foi regenerado';
    case 'PLAN_REGENERATION_FAILED':
      return 'falha ao regenerar o plano';
    default:
      return code;
  }
}

const MISSING_ZOOM_TEXT = 'Zoom não aplicado: o host não confirmou a aplicação.';

export class DirectZoomController {
  private state: DirectZoomState = {
    windows: [],
    planRevision: null,
    canUndo: false,
    canRedo: false,
    busy: false,
    feedback: null,
    loadError: null
  };
  private listeners = new Set<() => void>();
  private undoStack: HistoryEntry[] = [];
  private redoStack: HistoryEntry[] = [];
  private attempt: { key: string; requestId: string } | null = null;
  private refreshSeq = 0;

  constructor(
    private readonly api: DirectZoomApi,
    private readonly projectId: string,
    private readonly makeRequestId: () => string = newRequestId
  ) {}

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  getSnapshot = (): DirectZoomState => this.state;

  private set(patch: Partial<DirectZoomState>): void {
    this.state = {
      ...this.state,
      ...patch,
      canUndo: this.undoStack.length > 0,
      canRedo: this.redoStack.length > 0
    };
    this.listeners.forEach((l) => l());
  }

  /** Recarrega zooms efetivos (plano vigente, com fallback para sugestões aprovadas). Falha vira loadError. */
  async refresh(): Promise<void> {
    const seq = ++this.refreshSeq;
    try {
      const [list, plan] = await Promise.all([
        this.api.listSuggestions(this.projectId),
        this.api.getEditPlan(this.projectId, null).catch(() => null)
      ]);
      if (seq !== this.refreshSeq) return;
      this.set({
        windows: resolveZoomWindows(plan, list),
        planRevision: plan?.revision ?? null,
        loadError: null
      });
    } catch (err) {
      if (seq !== this.refreshSeq) return;
      this.set({ loadError: `Não foi possível carregar os zooms do projeto: ${messageOf(err)}` });
    }
  }

  /** Aplica o zoom no intervalo da fonte. Repetir a mesma tentativa reutiliza a requestId. */
  async apply(input: ZoomApplyInput): Promise<ZoomApplyOutcome | null> {
    if (this.state.busy) return null;
    const startSec = Math.min(input.startSec, input.endSec);
    const endSec = Math.max(input.startSec, input.endSec);
    const invalid = validateZoomInput({ ...input, startSec, endSec });
    if (invalid) {
      this.set({ feedback: { tone: 'error', text: `Zoom não aplicado: ${invalid}` } });
      return { status: 'invalid', message: invalid };
    }

    const baseRevision = this.state.planRevision;
    const key = [
      input.mediaId,
      startSec.toFixed(3),
      endSec.toFixed(3),
      input.scale,
      input.focalPointX,
      input.focalPointY,
      input.transitionInSeconds,
      input.transitionOutSeconds,
      baseRevision ?? 'null'
    ].join('|');
    if (!this.attempt || this.attempt.key !== key) this.attempt = { key, requestId: this.makeRequestId() };
    const requestId = this.attempt.requestId;

    this.set({ busy: true, feedback: null });
    try {
      const res = await this.api.applyDirectZoom({
        projectId: this.projectId,
        mediaId: input.mediaId,
        startSeconds: startSec,
        endSeconds: endSec,
        scale: input.scale,
        focalPointX: input.focalPointX,
        focalPointY: input.focalPointY,
        transitionInSeconds: input.transitionInSeconds,
        transitionOutSeconds: input.transitionOutSeconds,
        rationale: input.rationale,
        requestId,
        baseRevision
      });
      const planCode = res.planError ?? res.planSkippedReason ?? null;
      const effective = res.zoom
        ? {
            startSec: res.zoom.sourceStartSeconds,
            endSec: res.zoom.sourceEndSeconds,
            transitionInSeconds: res.zoom.transitionInSeconds
          }
        : null;

      if (res.applied === true) {
        this.attempt = null;
        if (!res.alreadyApplied) {
          this.undoStack.push({ suggestionId: res.suggestion.suggestionId, kind: 'apply' });
          this.redoStack = [];
        }
        const base = res.alreadyApplied
          ? 'Zoom já aplicado anteriormente (repetição da mesma tentativa).'
          : `Zoom aplicado ${formatZoomScale(input.scale)}.`;
        const warning = planCode && !res.alreadyApplied ? ` Atenção: plano não regenerado: ${describeZoomPlanCode(planCode)}.` : '';
        const action: CutFeedbackAction | null = res.alreadyApplied
          ? null
          : { label: 'Desfazer', op: 'undo', suggestionIds: [res.suggestion.suggestionId] };
        this.set({
          busy: false,
          planRevision: res.planRevision ?? this.state.planRevision,
          feedback: { tone: warning ? 'warning' : 'success', text: `${base}${warning}`, action }
        });
        await this.refresh();
        return { status: 'applied', suggestionId: res.suggestion.suggestionId, alreadyApplied: res.alreadyApplied, effective };
      }

      // applied=false: a sugestão pode ter ficado aprovada (ex.: falha ao regenerar o plano). Oferece desfazer nesse caso.
      const message = planCode ? describeZoomPlanCode(planCode) : MISSING_ZOOM_TEXT;
      const stayedApproved = res.suggestion?.status === 'Approved';
      if (stayedApproved) {
        this.undoStack.push({ suggestionId: res.suggestion.suggestionId, kind: 'apply' });
        this.redoStack = [];
      }
      this.set({
        busy: false,
        planRevision: res.planRevision ?? this.state.planRevision,
        feedback: {
          tone: 'warning',
          text: stayedApproved ? `Zoom gravado, mas ${message}. Desfaça para voltar ao estado anterior.` : `Zoom não aplicado: ${message}.`,
          action: stayedApproved ? { label: 'Desfazer', op: 'undo', suggestionIds: [res.suggestion.suggestionId] } : null
        }
      });
      await this.refresh();
      return { status: 'not_applied', message };
    } catch (err) {
      const code = codeOf(err);
      let message = messageOf(err);
      if (code === PLAN_STALE_BASE_CODE) message = STALE_BASE_MESSAGE;
      else if (code === ZOOM_CLIP_OCCUPIED_CODE) message = ZOOM_CLIP_OCCUPIED_MESSAGE;
      this.set({ busy: false, feedback: { tone: 'error', text: `Zoom não aplicado: ${message}` } });
      return { status: 'error', code, message };
    }
  }

  /**
   * Remove o zoom dos trechos que se sobrepõem ao intervalo: decision.submit reject SOMENTE nos zooms
   * (cortes permanecem). Lê o host antes, então funciona após reabrir o projeto.
   */
  async removeOverlapping(input: { mediaId: string; startSec: number; endSec: number }): Promise<ZoomRemoveOutcome | null> {
    if (this.state.busy) return null;
    const startSec = Math.min(input.startSec, input.endSec);
    const endSec = Math.max(input.startSec, input.endSec);

    this.set({ busy: true, feedback: null });
    await this.refresh();
    if (this.state.loadError) {
      const message = this.state.loadError;
      this.set({ busy: false, feedback: { tone: 'error', text: `Não foi possível remover o zoom: ${message}` } });
      return { status: 'error', message };
    }

    const targets = this.state.windows.filter(
      (w) => w.mediaId === input.mediaId && intervalsOverlap(startSec, endSec, w.startSec, w.endSec)
    );
    if (targets.length === 0) {
      this.set({ busy: false, feedback: { tone: 'info', text: 'Nenhum zoom aplicado neste trecho para remover.' } });
      return { status: 'none' };
    }

    const removed: string[] = [];
    let planRevision = this.state.planRevision;
    let warning: string | null = null;
    try {
      for (const target of targets) {
        const res = await this.api.submitDecision({
          suggestionId: target.suggestionId,
          action: 'reject',
          reviewer: ZOOM_REVIEWER,
          notes: REMOVE_NOTES
        });
        removed.push(target.suggestionId);
        planRevision = res.planRevision ?? planRevision;
        if (res.planError) warning = res.planError;
      }
    } catch (err) {
      const message = messageOf(err);
      for (const id of removed) this.undoStack.push({ suggestionId: id, kind: 'remove' });
      if (removed.length > 0) this.redoStack = [];
      this.set({
        busy: false,
        planRevision,
        feedback: {
          tone: 'error',
          text: `Não foi possível remover o zoom: ${message}`,
          action: removed.length > 0 ? { label: 'Desfazer', op: 'undo', suggestionIds: removed } : null
        }
      });
      await this.refresh();
      return { status: 'error', message };
    }

    for (const id of removed) this.undoStack.push({ suggestionId: id, kind: 'remove' });
    this.redoStack = [];
    const text = warning
      ? `Zoom removido. Atenção: plano não regenerado: ${describeZoomPlanCode(warning)}.`
      : 'Zoom removido. Os cortes do trecho foram mantidos.';
    this.set({
      busy: false,
      planRevision,
      feedback: {
        tone: warning ? 'warning' : 'success',
        text,
        action: { label: 'Desfazer', op: 'undo', suggestionIds: removed }
      }
    });
    await this.refresh();
    return { status: 'removed', suggestionIds: removed };
  }

  /** Desfaz (undo) ou refaz (redo) os ids indicados, na pilha correspondente. */
  async runHistory(op: 'undo' | 'redo', suggestionIds: string[]): Promise<void> {
    if (this.state.busy || suggestionIds.length === 0) return;
    const source = op === 'undo' ? this.undoStack : this.redoStack;
    if (!suggestionIds.every((id) => source.some((e) => e.suggestionId === id))) {
      this.set({ feedback: { tone: 'info', text: op === 'undo' ? 'Nada para desfazer neste zoom.' : 'Nada para refazer neste zoom.' } });
      return;
    }
    const kind = source.find((e) => e.suggestionId === suggestionIds[0])?.kind ?? 'apply';

    this.set({ busy: true, feedback: null });
    let planRevision = this.state.planRevision;
    let warning: string | null = null;
    try {
      for (const id of suggestionIds) {
        const res = op === 'undo' ? await this.api.undoDecision(id) : await this.api.redoDecision(id);
        this.moveEntry(op, id);
        planRevision = res.planRevision ?? planRevision;
        if (res.planError) warning = res.planError;
      }
    } catch (err) {
      this.set({
        busy: false,
        planRevision,
        feedback: { tone: 'error', text: `Não foi possível ${op === 'undo' ? 'desfazer' : 'refazer'}: ${messageOf(err)}` }
      });
      await this.refresh();
      return;
    }

    const text =
      kind === 'remove'
        ? op === 'undo' ? 'Remoção desfeita: zoom restaurado.' : 'Remoção refeita: zoom removido.'
        : op === 'undo' ? 'Zoom desfeito.' : 'Zoom refeito.';
    const action: CutFeedbackAction = {
      label: op === 'undo' ? 'Refazer' : 'Desfazer',
      op: op === 'undo' ? 'redo' : 'undo',
      suggestionIds
    };
    this.set({
      busy: false,
      planRevision,
      feedback: warning
        ? { tone: 'warning', text: `${text} Atenção: plano não regenerado: ${describeZoomPlanCode(warning)}.`, action }
        : { tone: 'info', text, action }
    });
    await this.refresh();
  }

  /** Ação do botão da própria mensagem (Desfazer/Refazer). */
  runFeedbackAction(action: CutFeedbackAction): Promise<void> {
    return this.runHistory(action.op, action.suggestionIds);
  }

  private moveEntry(op: 'undo' | 'redo', suggestionId: string): void {
    const from = op === 'undo' ? this.undoStack : this.redoStack;
    const to = op === 'undo' ? this.redoStack : this.undoStack;
    for (let i = from.length - 1; i >= 0; i--) {
      if (from[i].suggestionId === suggestionId) {
        const [entry] = from.splice(i, 1);
        to.push(entry);
        return;
      }
    }
  }
}
