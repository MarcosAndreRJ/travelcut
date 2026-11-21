import type {
  CutApplyRequest,
  CutApplyResponse,
  DecisionResultDto,
  EditSuggestion,
  SubmitDecisionRequest
} from '../../types/ipc';
import { intervalsOverlap } from './analysisFormat';

// Controlador do corte direto (edição por transcrição). Sem React e sem DOM:
// a view assina o estado via subscribe/getSnapshot. Só confirma corte quando o host responde applied=true.
// Fonte da verdade dos cortes efetivos: suggestions.list (refresh). A pilha de sessão só guarda o que
// esta instância fez, para desfazer/refazer; restaurar usa sempre o suggestionId persistido no host.

export interface DirectCutApi {
  applyDirectCut(params: CutApplyRequest): Promise<CutApplyResponse>;
  undoDecision(suggestionId: string): Promise<DecisionResultDto>;
  redoDecision(suggestionId: string): Promise<DecisionResultDto>;
  listSuggestions(projectId: string): Promise<EditSuggestion[]>;
  submitDecision(request: SubmitDecisionRequest): Promise<DecisionResultDto>;
}

/** Corte efetivo aprovado no host (intervalo em segundos da fonte). */
export interface ConfirmedCut {
  suggestionId: string;
  mediaId: string;
  startSec: number;
  endSec: number;
}

export const CUT_REVIEWER = 'Editor';
const RESTORE_NOTES = 'restaurar trecho (edição por transcrição)';

/** Ação de uma confirmação: desfazer ou refazer os suggestionIds indicados. */
export interface CutFeedbackAction {
  label: 'Desfazer' | 'Refazer';
  op: 'undo' | 'redo';
  suggestionIds: string[];
}

export interface CutFeedback {
  tone: 'success' | 'warning' | 'error' | 'info';
  text: string;
  action?: CutFeedbackAction | null;
}

export type CutActionKind = 'cut' | 'restore';

/** Entrada da pilha de sessão: um suggestionId e se a ação foi um corte ou uma restauração. */
export interface HistoryEntry {
  suggestionId: string;
  kind: CutActionKind;
}

export type CutOutcome =
  | { status: 'applied'; suggestionId: string; alreadyApplied: boolean; planRevision: number | null; warning: string | null }
  | { status: 'not_applied'; reason: string }
  | { status: 'error'; code: string | null; message: string };

export type RestoreResult =
  | { status: 'restored'; suggestionIds: string[] }
  | { status: 'none' }
  | { status: 'error'; message: string };

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

/** Cortes efetivos da mídia que se sobrepõem ao intervalo (os que 'Restaurar trecho' devolve ao plano). */
export function overlappingConfirmedCuts(
  cuts: readonly ConfirmedCut[],
  mediaId: string,
  startSec: number,
  endSec: number
): ConfirmedCut[] {
  return cuts.filter((c) => c.mediaId === mediaId && intervalsOverlap(startSec, endSec, c.startSec, c.endSec));
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

/** Texto da confirmação de desfazer/refazer, conforme o tipo da ação. */
function historyText(op: 'undo' | 'redo', kind: CutActionKind): string {
  if (kind === 'restore') {
    return op === 'undo' ? 'Restauração desfeita: corte reaplicado.' : 'Restauração refeita: trecho restaurado.';
  }
  return op === 'undo' ? 'Corte desfeito.' : 'Corte refeito.';
}

const undoActionFor = (suggestionIds: string[]): CutFeedbackAction => ({ label: 'Desfazer', op: 'undo', suggestionIds });

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
  private undoStack: HistoryEntry[] = [];
  private redoStack: HistoryEntry[] = [];
  private attempt: { key: string; requestId: string } | null = null;
  /** Sequência das leituras: só a leitura mais recente pode atualizar o estado (respostas antigas são descartadas). */
  private refreshSeq = 0;

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
    const seq = ++this.refreshSeq;
    try {
      const list = await this.api.listSuggestions(this.projectId);
      if (seq !== this.refreshSeq) return;
      this.set({ confirmed: confirmedCutsFrom(list), loadError: null });
    } catch (err) {
      if (seq !== this.refreshSeq) return;
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
          this.undoStack.push({ suggestionId: outcome.suggestionId, kind: 'cut' });
          this.redoStack = [];
        }
        const text = outcome.alreadyApplied
          ? 'Corte já aplicado anteriormente (repetição da mesma tentativa).'
          : 'Corte aplicado.';
        const action = outcome.alreadyApplied ? null : undoActionFor([outcome.suggestionId]);
        this.set({
          busy: false,
          planRevision: res.planRevision ?? null,
          feedback: outcome.warning
            ? { tone: 'warning', text: `${text} Atenção: ${outcome.warning}.`, action }
            : { tone: 'success', text, action }
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

  /**
   * Restaura o trecho [startSec, endSec] da mídia: rejeita (decision.submit reject) SOMENTE os cortes efetivos que se
   * sobrepõem, usando os suggestionIds do host. Lê o host antes, então funciona após reabrir o projeto ou trocar de clipe.
   * Os demais cortes permanecem. Falha do host vira erro com a mensagem real.
   */
  async restoreOverlapping(input: { mediaId: string; startSec: number; endSec: number }): Promise<RestoreResult | null> {
    if (this.state.busy) return null;
    const startSec = Math.min(input.startSec, input.endSec);
    const endSec = Math.max(input.startSec, input.endSec);

    this.set({ busy: true, feedback: null });
    await this.refresh();
    if (this.state.loadError) {
      const message = this.state.loadError;
      this.set({ busy: false, feedback: { tone: 'error', text: `Não foi possível restaurar o trecho: ${message}` } });
      return { status: 'error', message };
    }

    const targets = overlappingConfirmedCuts(this.state.confirmed, input.mediaId, startSec, endSec);
    if (targets.length === 0) {
      this.set({ busy: false, feedback: { tone: 'info', text: 'Nenhum corte aplicado neste trecho para restaurar.' } });
      return { status: 'none' };
    }

    const restored: string[] = [];
    let planRevision = this.state.planRevision;
    let warning: string | null = null;
    try {
      for (const target of targets) {
        const res = await this.api.submitDecision({
          suggestionId: target.suggestionId,
          action: 'reject',
          reviewer: CUT_REVIEWER,
          notes: RESTORE_NOTES
        });
        restored.push(target.suggestionId);
        planRevision = res.planRevision ?? null;
        if (res.planError) warning = res.planError;
      }
    } catch (err) {
      const message = messageOf(err);
      for (const id of restored) this.undoStack.push({ suggestionId: id, kind: 'restore' });
      if (restored.length > 0) this.redoStack = [];
      this.set({
        busy: false,
        planRevision,
        feedback: {
          tone: 'error',
          text: `Não foi possível restaurar o trecho: ${message}`,
          action: restored.length > 0 ? undoActionFor(restored) : null
        }
      });
      await this.refresh();
      return { status: 'error', message };
    }

    for (const id of restored) this.undoStack.push({ suggestionId: id, kind: 'restore' });
    this.redoStack = [];
    const text = warning ? `Trecho restaurado. Atenção: plano não regenerado: ${warning}.` : 'Trecho restaurado.';
    this.set({
      busy: false,
      planRevision,
      feedback: { tone: warning ? 'warning' : 'success', text, action: undoActionFor(restored) }
    });
    await this.refresh();
    return { status: 'restored', suggestionIds: restored };
  }

  /**
   * Desfaz (op 'undo') ou refaz (op 'redo') os suggestionIds indicados, via undoDecision/redoDecision de cada um.
   * Cada id sai da pilha de origem e entra na oposta; ids fora da pilha são ignorados com aviso.
   */
  async runHistory(op: 'undo' | 'redo', suggestionIds: string[]): Promise<void> {
    if (this.state.busy || suggestionIds.length === 0) return;
    const source = op === 'undo' ? this.undoStack : this.redoStack;
    const hasAll = suggestionIds.every((id) => source.some((e) => e.suggestionId === id));
    if (!hasAll) {
      this.set({
        feedback: { tone: 'info', text: op === 'undo' ? 'Nada para desfazer neste corte.' : 'Nada para refazer neste corte.' }
      });
      return;
    }
    const kind = source.find((e) => e.suggestionId === suggestionIds[0])?.kind ?? 'cut';

    this.set({ busy: true, feedback: null });
    let planRevision = this.state.planRevision;
    let warning: string | null = null;
    try {
      for (const id of suggestionIds) {
        const res = op === 'undo' ? await this.api.undoDecision(id) : await this.api.redoDecision(id);
        this.moveEntry(op, id);
        planRevision = res.planRevision ?? null;
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

    const text = historyText(op, kind);
    const action: CutFeedbackAction = {
      label: op === 'undo' ? 'Refazer' : 'Desfazer',
      op: op === 'undo' ? 'redo' : 'undo',
      suggestionIds
    };
    this.set({
      busy: false,
      planRevision,
      feedback: warning
        ? { tone: 'warning', text: `${text} Atenção: plano não regenerado: ${warning}.`, action }
        : { tone: 'info', text, action }
    });
    await this.refresh();
  }

  /** Move a entrada mais recente do suggestionId entre as pilhas (desfazer: undo → redo; refazer: redo → undo). */
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

  /** Confirmação com botão (Desfazer/Refazer) da própria mensagem. */
  runFeedbackAction(action: CutFeedbackAction): Promise<void> {
    return this.runHistory(action.op, action.suggestionIds);
  }

  /** Desfaz a última ação desta sessão (corte ou restauração). */
  async undo(): Promise<void> {
    const top = this.undoStack[this.undoStack.length - 1];
    if (!top) return;
    await this.runHistory('undo', [top.suggestionId]);
  }

  /** Refaz a última ação desfeita nesta sessão. */
  async redo(): Promise<void> {
    const top = this.redoStack[this.redoStack.length - 1];
    if (!top) return;
    await this.runHistory('redo', [top.suggestionId]);
  }
}
