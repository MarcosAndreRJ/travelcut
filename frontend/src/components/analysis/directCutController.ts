import type { CutApplyRequest, CutApplyResponse, DecisionResultDto, EditSuggestion } from '../../types/ipc';

// Controlador do corte direto (edição por transcrição, Incremento 1). Sem React e sem DOM:
// a view assina o estado via subscribe/getSnapshot. Só confirma corte quando o host responde applied=true.

export interface DirectCutApi {
  applyDirectCut(params: CutApplyRequest): Promise<CutApplyResponse>;
  undoDecision(suggestionId: string): Promise<DecisionResultDto>;
  redoDecision(suggestionId: string): Promise<DecisionResultDto>;
  listSuggestions(projectId: string): Promise<EditSuggestion[]>;
}

/** Corte efetivo aprovado no host (intervalo em segundos da fonte). */
export interface ConfirmedCut {
  suggestionId: string;
  mediaId: string;
  startSec: number;
  endSec: number;
}

export interface CutFeedback {
  tone: 'success' | 'warning' | 'error' | 'info';
  text: string;
}

export type CutOutcome =
  | { status: 'applied'; suggestionId: string; alreadyApplied: boolean; planRevision: number | null; warning: string | null }
  | { status: 'not_applied'; reason: string }
  | { status: 'error'; code: string | null; message: string };

export interface DirectCutState {
  confirmed: ConfirmedCut[];
  planRevision: number | null;
  canUndo: boolean;
  canRedo: boolean;
  busy: boolean;
  feedback: CutFeedback | null;
  loadError: string | null;
}

export const PLAN_STALE_BASE_CODE = 'PLAN_STALE_BASE';
export const STALE_BASE_MESSAGE = 'A revisão do plano mudou desde a última leitura. Recarregue o projeto e repita o corte.';
export const MIN_CUT_SECONDS = 0.05;

/** Status efetivo aprovado de um corte (ação 'cut'). */
export const isConfirmedCut = (s: EditSuggestion): boolean => s.status === 'Approved' && s.suggestedAction === 'cut';

/** Cortes efetivos aprovados, com o intervalo efetivo (ajustado) quando existir. */
export function confirmedCutsFrom(suggestions: readonly EditSuggestion[]): ConfirmedCut[] {
  return suggestions
    .filter(isConfirmedCut)
    .map((s) => ({
      suggestionId: s.suggestionId,
      mediaId: s.mediaId,
      startSec: s.effectiveStartSeconds ?? s.startSeconds,
      endSec: s.effectiveEndSeconds ?? s.endSeconds
    }))
    .filter((c) => Number.isFinite(c.startSec) && Number.isFinite(c.endSec) && c.endSec > c.startSec);
}

/** Chave de uma tentativa: mesma mídia, mesmo intervalo e mesma revisão-base. Repetir a chave reutiliza a requestId. */
export const attemptKey = (mediaId: string, startSec: number, endSec: number, baseRevision: number | null): string =>
  [mediaId, startSec.toFixed(3), endSec.toFixed(3), baseRevision ?? 'null'].join('|');

/** UUID v4 gerado no cliente (crypto.randomUUID, com fallback por getRandomValues). */
export function newRequestId(): string {
  const c = (globalThis as { crypto?: Crypto }).crypto;
  if (c && typeof c.randomUUID === 'function') return c.randomUUID();
  const b = new Uint8Array(16);
  if (c && typeof c.getRandomValues === 'function') c.getRandomValues(b);
  else for (let i = 0; i < 16; i++) b[i] = Math.floor(Math.random() * 256);
  b[6] = (b[6] & 0x0f) | 0x40;
  b[8] = (b[8] & 0x3f) | 0x80;
  const hex = Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

const codeOf = (err: unknown): string | null => {
  const code = (err as { code?: unknown } | null)?.code;
  return typeof code === 'string' ? code : null;
};

const messageOf = (err: unknown): string => (err instanceof Error ? err.message : String(err));

export const CUT_NOT_APPLIED_CODE = 'CUT_NOT_APPLIED';

/** Texto PT-BR para os códigos de plano devolvidos pelo host (planError / planSkippedReason). */
export function describePlanCode(code: string): string {
  switch (code) {
    case CUT_NOT_APPLIED_CODE:
      return 'o plano foi gerado, mas o corte não entrou nele (mídia fora do plano ou trecho menor que 1 quadro)';
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

/** Traduz a resposta de cut.apply (DirectCutResultDto). Confirmação só com applied === true e sem CUT_NOT_APPLIED. */
export function interpretCutResponse(res: CutApplyResponse): CutOutcome {
  const planCode = res.planError ?? res.planSkippedReason ?? null;
  if (res.applied !== true || res.planError === CUT_NOT_APPLIED_CODE) {
    return {
      status: 'not_applied',
      reason: planCode ? describePlanCode(planCode) : 'O host não confirmou a aplicação do corte.'
    };
  }
  const warning = planCode ? `plano não regenerado: ${describePlanCode(planCode)}` : null;
  return {
    status: 'applied',
    suggestionId: res.suggestion.suggestionId,
    alreadyApplied: res.alreadyApplied === true,
    planRevision: res.planRevision ?? null,
    warning
  };
}

export class DirectCutController {
  private state: DirectCutState = {
    confirmed: [],
    planRevision: null,
    canUndo: false,
    canRedo: false,
    busy: false,
    feedback: null,
    loadError: null
  };
  private listeners = new Set<() => void>();
  private undoStack: string[] = [];
  private redoStack: string[] = [];
  private attempt: { key: string; requestId: string } | null = null;

  constructor(
    private readonly api: DirectCutApi,
    private readonly projectId: string,
    private readonly makeRequestId: () => string = newRequestId
  ) {}

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  getSnapshot = (): DirectCutState => this.state;

  private set(patch: Partial<DirectCutState>): void {
    this.state = {
      ...this.state,
      ...patch,
      canUndo: this.undoStack.length > 0,
      canRedo: this.redoStack.length > 0
    };
    this.listeners.forEach((l) => l());
  }

  /** Recarrega os cortes efetivos do projeto (todas as mídias). Falha vira loadError, sem apagar o estado. */
  async refresh(): Promise<void> {
    try {
      const list = await this.api.listSuggestions(this.projectId);
      this.set({ confirmed: confirmedCutsFrom(list), loadError: null });
    } catch (err) {
      this.set({ loadError: `Não foi possível carregar os cortes do projeto: ${messageOf(err)}` });
    }
  }

  /** Corte direto do intervalo [startSec, endSec] da mídia. Repetir o mesmo intervalo reutiliza a requestId. */
  async cut(input: { mediaId: string; startSec: number; endSec: number; rationale: string }): Promise<CutOutcome | null> {
    if (this.state.busy) return null;
    const startSec = Math.min(input.startSec, input.endSec);
    const endSec = Math.max(input.startSec, input.endSec);
    if (endSec - startSec < MIN_CUT_SECONDS) {
      const message = `O trecho selecionado deve ter ao menos ${MIN_CUT_SECONDS.toFixed(2).replace('.', ',')}s.`;
      this.set({ feedback: { tone: 'error', text: message } });
      return { status: 'error', code: null, message };
    }

    const baseRevision = this.state.planRevision;
    const key = attemptKey(input.mediaId, startSec, endSec, baseRevision);
    if (!this.attempt || this.attempt.key !== key) this.attempt = { key, requestId: this.makeRequestId() };
    const requestId = this.attempt.requestId;

    this.set({ busy: true, feedback: null });
    try {
      const res = await this.api.applyDirectCut({
        projectId: this.projectId,
        mediaId: input.mediaId,
        startSeconds: startSec,
        endSeconds: endSec,
        rationale: input.rationale,
        requestId,
        baseRevision
      });
      const outcome = interpretCutResponse(res);
      if (outcome.status === 'applied') {
        this.attempt = null;
        if (!outcome.alreadyApplied) {
          this.undoStack.push(outcome.suggestionId);
          this.redoStack = [];
        }
        const text = outcome.alreadyApplied
          ? 'Corte já aplicado anteriormente (repetição da mesma tentativa).'
          : 'Corte aplicado.';
        this.set({
          busy: false,
          planRevision: res.planRevision ?? null,
          feedback: outcome.warning
            ? { tone: 'warning', text: `${text} Atenção: ${outcome.warning}.` }
            : { tone: 'success', text }
        });
      } else if (outcome.status === 'not_applied') {
        this.set({
          busy: false,
          feedback: { tone: 'warning', text: `Corte não aplicado: ${outcome.reason}.` }
        });
      }
      await this.refresh();
      return outcome;
    } catch (err) {
      const code = codeOf(err);
      const message = code === PLAN_STALE_BASE_CODE ? STALE_BASE_MESSAGE : messageOf(err);
      this.set({ busy: false, feedback: { tone: 'error', text: `Corte não aplicado: ${message}` } });
      return { status: 'error', code, message };
    }
  }

  /** Desfaz o último corte confirmado nesta sessão (undoDecision do candidato). */
  async undo(): Promise<void> {
    const id = this.undoStack[this.undoStack.length - 1];
    if (this.state.busy || !id) return;
    this.set({ busy: true, feedback: null });
    try {
      const res = await this.api.undoDecision(id);
      this.undoStack.pop();
      this.redoStack.push(id);
      this.set({
        busy: false,
        planRevision: res.planRevision ?? null,
        feedback: res.planError
          ? { tone: 'warning', text: `Corte desfeito. Atenção: plano não regenerado: ${res.planError}.` }
          : { tone: 'info', text: 'Corte desfeito.' }
      });
    } catch (err) {
      this.set({ busy: false, feedback: { tone: 'error', text: `Não foi possível desfazer: ${messageOf(err)}` } });
    }
    await this.refresh();
  }

  /** Refaz o último corte desfeito (redoDecision do candidato). */
  async redo(): Promise<void> {
    const id = this.redoStack[this.redoStack.length - 1];
    if (this.state.busy || !id) return;
    this.set({ busy: true, feedback: null });
    try {
      const res = await this.api.redoDecision(id);
      this.redoStack.pop();
      this.undoStack.push(id);
      this.set({
        busy: false,
        planRevision: res.planRevision ?? null,
        feedback: res.planError
          ? { tone: 'warning', text: `Corte refeito. Atenção: plano não regenerado: ${res.planError}.` }
          : { tone: 'info', text: 'Corte refeito.' }
      });
    } catch (err) {
      this.set({ busy: false, feedback: { tone: 'error', text: `Não foi possível refazer: ${messageOf(err)}` } });
    }
    await this.refresh();
  }
}
