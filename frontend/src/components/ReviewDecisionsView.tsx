import React, { useEffect, useMemo, useRef, useState } from 'react';
import { CheckCheck, Scissors, XCircle } from 'lucide-react';
import type { EditSuggestion, SmartZoomParametersDto } from '../types/ipc';
import { ipc } from '../services/ipcBridge';
import { isBatchApprovable } from './analysis/analysisFormat';
import type { CompareMode } from './review/BeforeAfterCompare';
import { ReviewDetail } from './review/ReviewDetail';
import { ReviewQueue } from './review/ReviewQueue';
import { ReviewBatchBar, type PendingBatch } from './review/ReviewBatchBar';
import { ReviewFeedback } from './review/ReviewFeedback';
import { ShortcutLegend } from './review/ShortcutLegend';
import { PreeditControlPanel } from './review/PreeditControlPanel';
import { PREEDIT_OUTCOME_LABEL, derivePreeditSummary, outcomeOf } from './review/preeditSummary';
import { useMediaAnalysis, useProjectMedia } from './review/useReviewData';
import { usePlanSnapshot } from './review/usePlanRemoved';
import type { EditPlanDto } from '../types/ipc';
import { removedIntervalsFromPlan } from './media/mediaTime';
import {
  DEFAULT_REVIEW_FILTERS,
  countByStatus,
  effectiveInterval,
  filterSuggestions,
  kindOptionsOf,
  prioritizePending,
  type ReviewDecisionAction,
  type ReviewErrorInfo,
  type ReviewFilters,
  type ReviewNotice
} from './review/reviewModel';

export interface ReviewDecisionsViewProps {
  projectId: string | null;
  suggestions: EditSuggestion[];
  loading: boolean;
  loadError: string | null;
  /** Erro da última ação (decisão, desfazer, refazer, lote, ajuste). */
  feedbackError: ReviewErrorInfo | null;
  /** Aviso não intrusivo (ex.: plano atualizado, decisão gravada sem regenerar plano). */
  notice: ReviewNotice | null;
  onReload: () => void;
  onClearFeedback: () => void;
  /** Cada handler devolve true quando o host gravou a ação; false mantém o estado e mostra o erro. */
  onDecide: (suggestionId: string, action: ReviewDecisionAction) => Promise<boolean>;
  onBatchDecide: (suggestionIds: string[], action: ReviewDecisionAction) => Promise<boolean>;
  onModify: (suggestionId: string, startSeconds: number, endSeconds: number) => Promise<boolean>;
  onApplyZoom?: (suggestionId: string, zoom: SmartZoomParametersDto) => Promise<boolean>;
  onUndo: (suggestionId: string) => Promise<boolean>;
  onRedo: (suggestionId: string) => Promise<boolean>;
  /** Navega para a Tela 04 (Planejamento da Timeline) após a revisão. */
  onNavigateToTimeline?: () => void;
}

const fileNameOf = (path: string): string => path.split(/[\\/]/).filter(Boolean).pop() ?? path;

const NO_AFTER: { intervals: { start: number; end: number }[]; source: 'candidate' } = { intervals: [], source: 'candidate' };

/**
 * "Depois" do candidato: com plano, pula TODOS os trechos removidos da mídia no plano vigente;
 * sem plano, pula só o intervalo efetivo do candidato (rotulado "só este corte").
 */
export const afterRemovedOf = (plan: EditPlanDto | null, s: EditSuggestion) => {
  if (!plan) return { intervals: [effectiveInterval(s)], source: 'candidate' as const };
  // Candidato que o plano não aplicou (pendente, adiado, não aplicado, conflito): o "Depois" é o original.
  const outcome = outcomeOf(s, plan);
  if (outcome !== 'autoApplied' && outcome !== 'userApplied') return { intervals: [], source: 'original' as const };
  return { intervals: removedIntervalsFromPlan(plan.clips, s.mediaId), source: 'plan' as const };
};

/**
 * Confirmação do lote de silêncios. A faixa "alta" é heurística não calibrada (ADR/docs 07);
 * pausas de ritmo e de humor não entram neste lote.
 */
const SILENCE_BATCH_NOTE =
  'A faixa de confiança "alta" é uma heurística não calibrada. Este lote inclui apenas silêncios; pausas de ritmo ou de humor não entram e seguem para revisão item a item.';

/** Setas vindas de dentro do player de intervalo controlam o player, não a fila. */
const isInsideMediaPlayer = (target: EventTarget | null): boolean =>
  target instanceof Element && target.closest('[data-media-player]') !== null;

/** Campos de texto bloqueiam atalhos de letra; checkbox e botões não. */
const isTextEntryTarget = (el: Element | null): boolean => {
  if (!el) return false;
  if (el instanceof HTMLTextAreaElement || el instanceof HTMLSelectElement) return true;
  if (el instanceof HTMLInputElement) {
    return !['checkbox', 'radio', 'button', 'submit', 'reset', 'range', 'color', 'file'].includes(el.type);
  }
  return (el as HTMLElement).isContentEditable === true;
};

export const ReviewDecisionsView: React.FC<ReviewDecisionsViewProps> = ({
  projectId,
  suggestions,
  loading,
  loadError,
  feedbackError,
  notice,
  onReload,
  onClearFeedback,
  onDecide,
  onBatchDecide,
  onModify,
  onApplyZoom,
  onUndo,
  onRedo,
  onNavigateToTimeline
}) => {
  const [filters, setFilters] = useState<ReviewFilters>(DEFAULT_REVIEW_FILTERS);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(() => new Set());
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [modifyOpen, setModifyOpen] = useState<boolean>(false);
  const [pendingBatch, setPendingBatch] = useState<PendingBatch | null>(null);
  const [compareMode, setCompareMode] = useState<CompareMode>('before');
  const [currentSeconds, setCurrentSeconds] = useState<number | null>(null);
  const [seekTarget, setSeekTarget] = useState<number | null>(null);

  const media = useProjectMedia(projectId);
  // Plano vigente (origem, tempo removido, zooms). Sem store próprio: lido do host a cada decisão.
  const planSnapshot = usePlanSnapshot(projectId, suggestions);
  const plan = planSnapshot.plan;

  const items = useMemo(() => {
    const filtered = filterSuggestions(suggestions, filters, plan);
    return filters.status === 'pending' ? prioritizePending(filtered) : filtered;
  }, [suggestions, filters, plan]);
  const counts = useMemo(() => countByStatus(suggestions, plan), [suggestions, plan]);
  const summary = useMemo(() => derivePreeditSummary(suggestions, plan), [suggestions, plan]);
  const kindOptions = useMemo(() => kindOptionsOf(suggestions), [suggestions]);
  // Lote só para pendentes elegíveis (decisão de elegibilidade do host); já aplicados ficam de fora.
  const silenceIds = useMemo(
    () => suggestions.filter((s) => isBatchApprovable(s) && outcomeOf(s, plan) === 'pending').map((s) => s.suggestionId),
    [suggestions, plan]
  );

  // Item ativo: permanece visível mesmo se os filtros o excluírem; sem escolha, o primeiro da fila.
  const activeItem: EditSuggestion | null =
    suggestions.find((s) => s.suggestionId === activeId) ?? items[0] ?? null;
  const activeEff = activeItem ? effectiveInterval(activeItem) : null;
  const activeTranscript = useMediaAnalysis(projectId, activeItem?.mediaId ?? null);

  // Seleção de lote só vale para candidatos que ainda existem na lista recarregada.
  const validSelectedIds = useMemo(() => {
    const known = new Set(suggestions.map((s) => s.suggestionId));
    return new Set(Array.from(selectedIds).filter((id) => known.has(id)));
  }, [selectedIds, suggestions]);

  const mediaNameOf = (mediaId: string): string => {
    if (media.status === 'ready') {
      const asset = media.data[mediaId];
      if (asset) return fileNameOf(asset.relativePath);
    }
    return mediaId || 'Mídia não informada';
  };

  // Ao trocar de projeto, zera seleção, item ativo e rascunhos.
  useEffect(() => {
    setActiveId(null);
    setSelectedIds(new Set());
    setPendingBatch(null);
    setModifyOpen(false);
    setCurrentSeconds(null);
  }, [projectId]);

  // Ao trocar de candidato: fecha editor e reinicia o cursor de tempo.
  useEffect(() => {
    setModifyOpen(false);
    setCurrentSeconds(null);
  }, [activeItem?.suggestionId]);

  const run = async <T,>(key: string, fn: () => Promise<T>): Promise<T | undefined> => {
    setBusyKey(key);
    try {
      return await fn();
    } finally {
      setBusyKey(null);
    }
  };

  const busy = busyKey !== null;

  // A decisão fixa o item ativo: ele continua selecionado mesmo que saia do filtro,
  // para que Z/Desfazer atue sobre a decisão recém-tomada.
  const decide = (action: ReviewDecisionAction) => {
    if (!activeItem || busy) return;
    const id = activeItem.suggestionId;
    setActiveId(id);
    void run(`decide:${action}`, () => onDecide(id, action));
  };

  const undo = () => {
    if (!activeItem || busy || activeItem.canUndo !== true) return;
    const id = activeItem.suggestionId;
    setActiveId(id);
    void run('undo', () => onUndo(id));
  };

  const redo = () => {
    if (!activeItem || busy || activeItem.canRedo !== true) return;
    const id = activeItem.suggestionId;
    setActiveId(id);
    void run('redo', () => onRedo(id));
  };

  // Desfazer/Restaurar por linha da fila: atuam sobre o candidato da linha, sem depender do ativo.
  const undoItem = (id: string) => {
    if (busy) return;
    setActiveId(id);
    void run('undo', () => onUndo(id));
  };

  const redoItem = (id: string) => {
    if (busy) return;
    setActiveId(id);
    void run('redo', () => onRedo(id));
  };

  const submitModify = async (start: number, end: number): Promise<boolean> => {
    if (!activeItem) return false;
    const id = activeItem.suggestionId;
    const ok = await run('modify', () => onModify(id, start, end));
    if (ok) setModifyOpen(false);
    return ok === true;
  };

  const moveActive = (delta: 1 | -1) => {
    if (items.length === 0) return;
    const index = items.findIndex((s) => s.suggestionId === activeItem?.suggestionId);
    const next = index === -1 ? 0 : Math.min(items.length - 1, Math.max(0, index + delta));
    setActiveId(items[next].suggestionId);
  };

  const handleSeek = (sourceSeconds: number) => {
    // Repetir o mesmo tempo precisa disparar novo seek: zera antes de aplicar.
    setSeekTarget(null);
    window.setTimeout(() => setSeekTarget(sourceSeconds), 0);
  };

  const toggleSelected = (id: string) =>
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const toggleAllVisible = (ids: string[], select: boolean) =>
    setSelectedIds((prev) => {
      const next = new Set(prev);
      for (const id of ids) {
        if (select) next.add(id);
        else next.delete(id);
      }
      return next;
    });

  const confirmBatch = async () => {
    if (!pendingBatch || busy) return;
    const { ids, action } = pendingBatch;
    const ok = await run('batch', () => onBatchDecide(ids, action));
    if (ok) {
      setPendingBatch(null);
      setSelectedIds(new Set());
    }
  };

  const requestSilenceBatch = () => {
    if (silenceIds.length === 0) return;
    setPendingBatch({ ids: silenceIds, action: 'approve', note: SILENCE_BATCH_NOTE });
  };

  // Atalhos globais: só atuam fora de campos de texto (exceto Esc, que apenas fecha painéis).
  const handleKeyRef = useRef<(e: KeyboardEvent) => void>(() => undefined);
  useEffect(() => {
    handleKeyRef.current = (e: KeyboardEvent) => {
      if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return;
      if (e.key === 'Escape') {
        if (pendingBatch) {
          setPendingBatch(null);
          e.preventDefault();
        } else if (modifyOpen) {
          setModifyOpen(false);
          e.preventDefault();
        }
        return;
      }
      if (isTextEntryTarget(document.activeElement)) return;
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        if (isInsideMediaPlayer(e.target)) return;
        moveActive(e.key === 'ArrowDown' ? 1 : -1);
        e.preventDefault();
        return;
      }
      if (e.repeat || busy || !activeItem) return;
      switch (e.key.toLowerCase()) {
        case 'a':
          decide('approve');
          break;
        case 'r':
          decide('reject');
          break;
        case 'd':
          decide('defer');
          break;
        case 'b':
          decide('reopen');
          break;
        case 'm':
          setModifyOpen((open) => !open);
          break;
        case 'z':
          undo();
          break;
        case 'y':
          redo();
          break;
        default:
          return;
      }
      e.preventDefault();
    };
  });

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => handleKeyRef.current(e);
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const hasSuggestions = suggestions.length > 0;
  // Tempo removido: sempre do plano do host (nunca somado localmente).
  const afterRemoved = useMemo(
    () => (activeItem ? afterRemovedOf(plan, activeItem) : NO_AFTER),
    [plan, activeItem]
  );

  let content: React.ReactNode;
  if (!projectId) {
    content = (
      <EmptyState
        title="Abra um projeto para revisar cortes"
        text="A revisão mostra apenas candidatos do projeto aberto."
      />
    );
  } else if (loadError && !hasSuggestions) {
    content = (
      <div className="flex-1 flex flex-col items-center justify-center gap-3 p-8 text-center" role="alert">
        <XCircle className="w-10 h-10 text-rose-400" aria-hidden="true" />
        <p className="text-sm text-rose-200 max-w-md">Não foi possível carregar os candidatos: {loadError}</p>
        <button
          type="button"
          onClick={onReload}
          className="px-3.5 py-1.5 rounded-xl border border-slate-600 text-xs font-semibold text-slate-200 hover:border-cyan-500 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400"
        >
          Tentar novamente
        </button>
      </div>
    );
  } else if (loading && !hasSuggestions) {
    content = <EmptyState title="Carregando candidatos..." text="Lendo a lista de sugestões do projeto." role="status" />;
  } else if (!hasSuggestions) {
    content = (
      <EmptyState
        title="Nenhum candidato a corte ainda — rode a análise na Biblioteca"
        text="Os candidatos vêm da análise real (transcrição e silêncio). Nada é sugerido sem evidência."
      />
    );
  } else {
    const batchBar = (
      <ReviewBatchBar
        selectedCount={validSelectedIds.size}
        pending={pendingBatch}
        busy={busy}
        onRequest={(action) => {
          const ids = Array.from(validSelectedIds);
          // Aprovar em lote algo que não é silêncio corta falas/pausas sem ouvir item a item (docs/07): avisar.
          const nonSilence = suggestions.filter(
            (s) => validSelectedIds.has(s.suggestionId) && s.kind.toLowerCase() !== 'silence'
          ).length;
          const note =
            action === 'approve' && nonSilence > 0
              ? `${nonSilence} item(ns) da seleção não são silêncio (pausa, vício de fala, repetição…). Pausas de ritmo e brincadeiras podem ser cortadas; prefira revisar esses itens um a um.`
              : undefined;
          setPendingBatch({ ids, action, note });
        }}
        onConfirm={confirmBatch}
        onCancelConfirm={() => setPendingBatch(null)}
        onClearSelection={() => setSelectedIds(new Set())}
      />
    );

    content = (
      <div className="flex-1 flex flex-col lg:flex-row min-h-0 overflow-hidden">
        <ReviewQueue
          items={items}
          counts={counts}
          outcomeLabelOf={(s) => PREEDIT_OUTCOME_LABEL[outcomeOf(s, plan)]}
          kindOptions={kindOptions}
          filters={filters}
          onFiltersChange={setFilters}
          activeId={activeItem?.suggestionId ?? null}
          selectedIds={validSelectedIds}
          onSelect={(id) => setActiveId(id)}
          onToggleSelected={toggleSelected}
          onToggleAllVisible={toggleAllVisible}
          mediaName={mediaNameOf}
          onUndoItem={undoItem}
          onRedoItem={redoItem}
          actionsBusy={busy}
          batchBar={batchBar}
          footer={<ShortcutLegend />}
        />

        {activeItem && activeEff && projectId ? (
          <ReviewDetail
            projectId={projectId}
            suggestion={activeItem}
            outOfFilter={!items.some((s) => s.suggestionId === activeItem.suggestionId)}
            mediaName={mediaNameOf(activeItem.mediaId)}
            transcript={activeTranscript}
            compareMode={compareMode}
            onCompareModeChange={setCompareMode}
            seekTarget={seekTarget}
            currentSeconds={currentSeconds}
            onTimeUpdate={setCurrentSeconds}
            onSeek={handleSeek}
            busy={busy}
            modifyOpen={modifyOpen}
            onToggleModify={() => setModifyOpen((open) => !open)}
            onCloseModify={() => setModifyOpen(false)}
            onModifySubmit={submitModify}
            onApplyZoom={onApplyZoom && activeItem ? (zoom) => void run('zoom', () => onApplyZoom(activeItem.suggestionId, zoom)) : undefined}
            onApprove={() => decide('approve')}
            onReject={() => decide('reject')}
            onDefer={() => decide('defer')}
            onReopen={() => decide('reopen')}
            onUndo={undo}
            onRedo={redo}
            afterRemoved={afterRemoved}
          />
        ) : (
          <div className="flex-1 flex flex-col items-center justify-center gap-3 p-8 text-center">
            <p className="text-sm text-slate-400">Nenhum candidato com estes filtros.</p>
            <button
              type="button"
              onClick={() => setFilters(DEFAULT_REVIEW_FILTERS)}
              className="px-3 py-1.5 rounded-xl border border-slate-600 text-xs font-semibold text-slate-200 hover:border-cyan-500 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400"
            >
              Voltar ao filtro padrão (pendentes)
            </button>
          </div>
        )}
      </div>
    );
  }

  return (
    <div className="flex-1 flex flex-col h-full min-h-0 bg-[#070d18] text-slate-100 font-sans overflow-hidden">
      {/* Cabeçalho */}
      <div className="px-4 md:px-6 py-3 border-b border-slate-800 bg-[#0a1222] flex flex-wrap items-center justify-between gap-3 shrink-0">
        <div className="flex flex-wrap items-center gap-3 min-w-0">
          <div className="flex items-center gap-2">
            <Scissors className="w-5 h-5 text-cyan-400" aria-hidden="true" />
            <h2 className="text-sm font-bold text-white">Revisão de cortes</h2>
          </div>
          {ipc.isMock && (
            <span className="px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider bg-amber-500/10 text-amber-300 border border-amber-500/30">
              DEMONSTRAÇÃO
            </span>
          )}
          <span className="text-xs text-slate-400">
            {suggestions.length} {suggestions.length === 1 ? 'candidato a corte' : 'candidatos a corte'}
          </span>
        </div>

        <div className="flex items-center gap-2">
          {onNavigateToTimeline && (
            <button
              type="button"
              onClick={onNavigateToTimeline}
              className="px-3 py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-cyan-300 hover:text-white border border-slate-700 hover:border-cyan-500/50 text-xs font-semibold flex items-center gap-1.5 transition-all focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400"
              title="Avançar para o Planejamento da Timeline (T04) com as decisões tomadas"
            >
              <span>Avançar para Timeline (T04)</span>
            </button>
          )}

          <button
            type="button"
            onClick={requestSilenceBatch}
            disabled={busy || silenceIds.length === 0 || !projectId}
            title={
              silenceIds.length === 0
                ? 'Nenhum silêncio com confiança alta, pendente e sem revisão humana'
                : 'Abre a confirmação para aprovar silêncios de confiança alta (heurística não calibrada; pausas de ritmo ou humor não entram)'
            }
            className="px-3.5 py-1.5 rounded-xl bg-amber-600 hover:bg-amber-500 disabled:opacity-50 text-white text-xs font-semibold flex items-center gap-1.5 shadow-md shadow-amber-950 transition-all focus-visible:outline focus-visible:outline-2 focus-visible:outline-white"
          >
            <CheckCheck className="w-4 h-4" aria-hidden="true" />
            <span>Aprovar silêncios confiáveis ({silenceIds.length})</span>
          </button>
        </div>
      </div>

      {/* Painel de controle da pré-edição: contagens reais do plano vigente + sugestões */}
      <PreeditControlPanel
        summary={summary}
        hasPlan={plan !== null}
        activeStatus={filters.status}
        onSelectStatus={(status) => setFilters((f) => ({ ...f, status }))}
      />

      <ReviewFeedback error={feedbackError} notice={notice} onDismiss={onClearFeedback} />

      {content}
    </div>
  );
};

const EmptyState: React.FC<{ title: string; text: string; role?: 'status' }> = ({ title, text, role }) => (
  <div role={role} className="flex-1 flex flex-col items-center justify-center gap-3 p-8 text-center">
    <Scissors className="w-10 h-10 text-slate-600" aria-hidden="true" />
    <p className="text-sm text-slate-300 max-w-md">{title}</p>
    <p className="text-xs text-slate-500 max-w-md">{text}</p>
  </div>
);

