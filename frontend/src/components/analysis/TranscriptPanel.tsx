import React, { useMemo, useState, useEffect, useCallback, useRef } from 'react';
import {
  Search,
  Scissors,
  ZoomIn,
  Play,
  RotateCcw,
  RotateCw,
  X,
  Trash2,
  Sliders,
  Check
} from 'lucide-react';
import type { HesitationCandidateDto, TranscriptDto } from '../../types/ipc';
import { findWordMatches, formatClock, intervalsOverlap } from './analysisFormat';

export interface RemovedInterval {
  id: string;
  startSec: number;
  endSec: number;
  text: string;
  suggestionId?: string;
}

export interface WordZoomSuggestion {
  id: string;
  startSec: number;
  endSec: number;
  text: string;
  scale: number;
  transitionInSec: number;
  transitionOutSec: number;
  anchor: 'center' | 'top-third';
}

interface TranscriptPanelProps {
  transcript: TranscriptDto;
  hesitations: HesitationCandidateDto[];
  selectedSec: number | null;
  onSeek: (sec: number) => void;
  onDeleteInterval?: (startSec: number, endSec: number, text: string) => Promise<string | void> | void;
  onCreateZoomInterval?: (
    startSec: number,
    endSec: number,
    text: string,
    config: { scale: number; transitionInSec: number; transitionOutSec: number; anchor: 'center' | 'top-third' }
  ) => Promise<string | void> | void;
}

interface HistoryAction {
  type: 'delete' | 'zoom';
  item: RemovedInterval | WordZoomSuggestion;
}

/**
 * Transcrição interativa por palavras (F14.6 Checkpoints C, D e E).
 * Permite selecionar palavras, excluir trecho da montagem (Delete), criar zoom com transição e desfazer/refazer.
 */
export const TranscriptPanel: React.FC<TranscriptPanelProps> = ({
  transcript,
  hesitations,
  selectedSec,
  onSeek,
  onDeleteInterval,
  onCreateZoomInterval
}) => {
  const [query, setQuery] = useState('');
  const [matchCursor, setMatchCursor] = useState(-1);

  // Seleção de palavras por índice de sequência (startSeq até endSeq inclusivo)
  const [selectionRange, setSelectionRange] = useState<{ startSeq: number; endSeq: number } | null>(null);
  const [anchorSeq, setAnchorSeq] = useState<number | null>(null);

  // Trechos excluídos da montagem (marcados visualmente com tachado, não destrutivos)
  const [removedIntervals, setRemovedIntervals] = useState<RemovedInterval[]>([]);

  // Zooms criados a partir da transcrição
  const [wordZooms, setWordZooms] = useState<WordZoomSuggestion[]>([]);

  // Pilhas de Undo/Redo
  const [undoStack, setUndoStack] = useState<HistoryAction[]>([]);
  const [redoStack, setRedoStack] = useState<HistoryAction[]>([]);

  // Configuração contextual do Zoom
  const [isConfiguringZoom, setIsConfiguringZoom] = useState(false);
  const [zoomScale, setZoomScale] = useState(1.08);
  const [zoomTransIn, setZoomTransIn] = useState(0.35);
  const [zoomTransOut, setZoomTransOut] = useState(0.35);
  const [zoomAnchor, setZoomAnchor] = useState<'center' | 'top-third'>('center');

  // Feedback e status
  const [statusMessage, setStatusMessage] = useState<{ text: string; type: 'success' | 'info' | 'error' } | null>(null);

  const containerRef = useRef<HTMLDivElement>(null);

  const words = transcript.words || [];

  const matches = useMemo(() => findWordMatches(words, query), [words, query]);
  const matchSet = useMemo(() => new Set(matches), [matches]);

  const goToMatch = (index: number) => {
    if (matches.length === 0) return;
    const next = ((index % matches.length) + matches.length) % matches.length;
    setMatchCursor(next);
    document.getElementById(`transcript-word-${matches[next]}`)?.scrollIntoView({ block: 'nearest' });
  };

  const handleSearchKey = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      goToMatch(e.shiftKey ? matchCursor - 1 : matchCursor + 1);
    }
  };

  // Resolve as palavras atualmente selecionadas
  const selectedWords = useMemo(() => {
    if (!selectionRange) return [];
    const minSeq = Math.min(selectionRange.startSeq, selectionRange.endSeq);
    const maxSeq = Math.max(selectionRange.startSeq, selectionRange.endSeq);
    return words.filter((w) => w.seq >= minSeq && w.seq <= maxSeq);
  }, [words, selectionRange]);

  const selectionDetails = useMemo(() => {
    if (selectedWords.length === 0) return null;
    const startSec = selectedWords[0].start;
    const endSec = selectedWords[selectedWords.length - 1].end;
    const durationSec = Math.max(0, endSec - startSec);
    const text = selectedWords.map((w) => w.text).join(' ');
    return { startSec, endSec, durationSec, text };
  }, [selectedWords]);

  // Manipulação de clique nas palavras
  const handleWordClick = (seq: number, startSec: number, e: React.MouseEvent) => {
    if (e.shiftKey && anchorSeq !== null) {
      // Extensão de seleção contínua com Shift + Clique
      setSelectionRange({ startSeq: anchorSeq, endSeq: seq });
    } else {
      // Seleção simples de uma palavra ou início de nova seleção
      setAnchorSeq(seq);
      setSelectionRange({ startSeq: seq, endSeq: seq });
      onSeek(startSec);
    }
    setIsConfiguringZoom(false);
  };

  // Limpar seleção ativa
  const clearSelection = useCallback(() => {
    setSelectionRange(null);
    setAnchorSeq(null);
    setIsConfiguringZoom(false);
  }, []);

  // Exclusão editorial por Delete (Checkpoint D)
  const handleDeleteSelection = useCallback(async () => {
    if (!selectionDetails) return;
    const { startSec, endSec, text } = selectionDetails;
    if (endSec <= startSec) return;

    let suggestionId: string | undefined;
    try {
      if (onDeleteInterval) {
        const res = await onDeleteInterval(startSec, endSec, text);
        if (typeof res === 'string') suggestionId = res;
      }
    } catch (err) {
      setStatusMessage({
        text: `Falha ao registrar exclusão: ${err instanceof Error ? err.message : String(err)}`,
        type: 'error'
      });
      return;
    }

    const removedItem: RemovedInterval = {
      id: 'del-' + Date.now() + '-' + Math.random().toString(36).substring(2, 6),
      startSec,
      endSec,
      text,
      suggestionId
    };

    setRemovedIntervals((prev) => [...prev, removedItem]);
    setUndoStack((prev) => [...prev, { type: 'delete', item: removedItem }]);
    setRedoStack([]); // limpa redo após nova ação
    clearSelection();
    setStatusMessage({
      text: `Trecho "${text.substring(0, 30)}${text.length > 30 ? '…' : ''}" removido da montagem (${formatClock(startSec)} – ${formatClock(endSec)}).`,
      type: 'success'
    });
  }, [selectionDetails, onDeleteInterval, clearSelection]);

  // Criação de zoom por palavras selecionadas (Checkpoint E)
  const handleConfirmZoom = useCallback(async () => {
    if (!selectionDetails) return;
    const { startSec, endSec, text } = selectionDetails;
    const config = {
      scale: zoomScale,
      transitionInSec: zoomTransIn,
      transitionOutSec: zoomTransOut,
      anchor: zoomAnchor
    };

    try {
      if (onCreateZoomInterval) {
        await onCreateZoomInterval(startSec, endSec, text, config);
      }
    } catch (err) {
      setStatusMessage({
        text: `Falha ao criar zoom: ${err instanceof Error ? err.message : String(err)}`,
        type: 'error'
      });
      return;
    }

    const zoomItem: WordZoomSuggestion = {
      id: 'zoom-' + Date.now() + '-' + Math.random().toString(36).substring(2, 6),
      startSec,
      endSec,
      text,
      scale: zoomScale,
      transitionInSec: zoomTransIn,
      transitionOutSec: zoomTransOut,
      anchor: zoomAnchor
    };

    setWordZooms((prev) => [...prev, zoomItem]);
    setUndoStack((prev) => [...prev, { type: 'zoom', item: zoomItem }]);
    setRedoStack([]);
    setIsConfiguringZoom(false);
    clearSelection();
    setStatusMessage({
      text: `Zoom ${zoomScale}× sugerido para "${text.substring(0, 30)}${text.length > 30 ? '…' : ''}" (${formatClock(startSec)} – ${formatClock(endSec)}).`,
      type: 'success'
    });
  }, [selectionDetails, zoomScale, zoomTransIn, zoomTransOut, zoomAnchor, onCreateZoomInterval, clearSelection]);

  // Desfazer ação (Ctrl+Z)
  const handleUndo = useCallback(() => {
    if (undoStack.length === 0) return;
    const lastAction = undoStack[undoStack.length - 1];
    setUndoStack((prev) => prev.slice(0, -1));
    setRedoStack((prev) => [...prev, lastAction]);

    if (lastAction.type === 'delete') {
      setRemovedIntervals((prev) => prev.filter((r) => r.id !== lastAction.item.id));
      setStatusMessage({ text: 'Exclusão desfeita. Trecho restaurado na montagem.', type: 'info' });
    } else if (lastAction.type === 'zoom') {
      setWordZooms((prev) => prev.filter((z) => z.id !== lastAction.item.id));
      setStatusMessage({ text: 'Zoom desfeito.', type: 'info' });
    }
  }, [undoStack]);

  // Refazer ação (Ctrl+Y / Ctrl+Shift+Z)
  const handleRedo = useCallback(() => {
    if (redoStack.length === 0) return;
    const nextAction = redoStack[redoStack.length - 1];
    setRedoStack((prev) => prev.slice(0, -1));
    setUndoStack((prev) => [...prev, nextAction]);

    if (nextAction.type === 'delete') {
      setRemovedIntervals((prev) => [...prev, nextAction.item as RemovedInterval]);
      setStatusMessage({ text: 'Exclusão refeita.', type: 'info' });
    } else if (nextAction.type === 'zoom') {
      setWordZooms((prev) => [...prev, nextAction.item as WordZoomSuggestion]);
      setStatusMessage({ text: 'Zoom refeito.', type: 'info' });
    }
  }, [redoStack]);

  // Atalhos de teclado protegidos (Checkpoint D6)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Proteção de teclado: nunca interceptar se o foco estiver em input, textarea ou campo editável
      const activeTag = (document.activeElement as HTMLElement)?.tagName;
      if (['INPUT', 'TEXTAREA', 'SELECT'].includes(activeTag) || (document.activeElement as HTMLElement)?.isContentEditable) {
        return;
      }

      // Atalho Delete / Backspace para excluir seleção de palavras
      if ((e.key === 'Delete' || e.key === 'Backspace') && selectionRange) {
        e.preventDefault();
        handleDeleteSelection();
        return;
      }

      // Escape para limpar seleção
      if (e.key === 'Escape' && selectionRange) {
        e.preventDefault();
        clearSelection();
        return;
      }

      // Ctrl+Z (Undo)
      if ((e.ctrlKey || e.metaKey) && e.key === 'z' && !e.shiftKey) {
        e.preventDefault();
        handleUndo();
        return;
      }

      // Ctrl+Y ou Ctrl+Shift+Z (Redo)
      if ((e.ctrlKey || e.metaKey) && (e.key === 'y' || (e.key === 'z' && e.shiftKey) || (e.key === 'Z' && e.shiftKey))) {
        e.preventDefault();
        handleRedo();
        return;
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [selectionRange, handleDeleteSelection, clearSelection, handleUndo, handleRedo]);

  return (
    <div ref={containerRef} className="flex flex-col min-h-0 h-full relative" tabIndex={0}>
      {/* Cabeçalho de busca e estatísticas */}
      <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
        <div className="text-[10px] text-slate-400 font-mono">
          Idioma: {transcript.language || '—'} · Modelo: {transcript.modelId || '—'} · Áudio: {formatClock(transcript.audioDurationSeconds)}
        </div>
        <div className="flex items-center gap-2">
          {/* Botões de Undo/Redo no topo */}
          <div className="flex items-center gap-1 border-r border-slate-800 pr-2 mr-1">
            <button
              type="button"
              onClick={handleUndo}
              disabled={undoStack.length === 0}
              title="Desfazer exclusão/zoom (Ctrl+Z)"
              className="p-1 rounded bg-slate-800/80 hover:bg-slate-700 disabled:opacity-40 text-slate-300 disabled:hover:bg-slate-800/80"
              aria-label="Desfazer"
            >
              <RotateCcw className="w-3 h-3" />
            </button>
            <button
              type="button"
              onClick={handleRedo}
              disabled={redoStack.length === 0}
              title="Refazer (Ctrl+Y)"
              className="p-1 rounded bg-slate-800/80 hover:bg-slate-700 disabled:opacity-40 text-slate-300 disabled:hover:bg-slate-800/80"
              aria-label="Refazer"
            >
              <RotateCw className="w-3 h-3" />
            </button>
          </div>

          <div className="relative">
            <Search className="w-3.5 h-3.5 text-slate-400 absolute left-2.5 top-2" aria-hidden="true" />
            <input
              type="search"
              aria-label="Buscar na transcrição"
              title="Enter: próxima ocorrência · Shift+Enter: anterior"
              placeholder="Buscar na transcrição..."
              value={query}
              onChange={(e) => {
                setQuery(e.target.value);
                setMatchCursor(-1);
              }}
              onKeyDown={handleSearchKey}
              className="pl-8 pr-2.5 py-1 bg-[#070d18] border border-slate-700 rounded-lg text-xs text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 w-48"
            />
          </div>
          <span className="text-[10px] text-slate-400 font-mono whitespace-nowrap" aria-live="polite">
            {query.trim() ? `${matches.length} ocorrência(s)` : `${words.length} palavras`}
          </span>
        </div>
      </div>

      {/* Barra de ação contextual quando palavras estão selecionadas (Checkpoint C5 & D) */}
      {selectionDetails && (
        <div
          data-testid="transcript-context-toolbar"
          className="sticky top-0 z-20 mb-2 p-2.5 rounded-xl bg-slate-900/95 border border-cyan-500/50 shadow-xl backdrop-blur-md flex flex-wrap items-center justify-between gap-2.5 transition-all animate-in fade-in"
        >
          <div className="min-w-0 flex items-center gap-2">
            <span className="px-1.5 py-0.5 rounded bg-cyan-500/20 text-cyan-300 text-[10px] font-mono font-bold">
              {formatClock(selectionDetails.startSec)} – {formatClock(selectionDetails.endSec)}
            </span>
            <span className="text-[11px] text-slate-300 font-mono">
              ({selectionDetails.durationSec.toFixed(2)}s · {selectedWords.length} palavras)
            </span>
            <span className="text-[11px] text-white font-medium italic truncate max-w-xs md:max-w-md">
              "{selectionDetails.text}"
            </span>
          </div>

          <div className="flex items-center gap-1.5 shrink-0">
            {/* Reproduzir seleção */}
            <button
              type="button"
              onClick={() => onSeek(selectionDetails.startSec)}
              title="Reproduzir trecho selecionado"
              className="px-2 py-1 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-medium flex items-center gap-1 border border-slate-700"
            >
              <Play className="w-3 h-3 text-cyan-400 fill-cyan-400" />
              <span>Ouvir</span>
            </button>

            {/* Criar Zoom */}
            <button
              type="button"
              onClick={() => setIsConfiguringZoom((prev) => !prev)}
              aria-expanded={isConfiguringZoom}
              title="Criar sugestão de zoom editorial para o trecho"
              className={`px-2 py-1 rounded-lg text-xs font-medium flex items-center gap-1 border transition-colors ${
                isConfiguringZoom
                  ? 'bg-purple-600 text-white border-purple-400'
                  : 'bg-purple-950/70 hover:bg-purple-900/90 text-purple-200 border-purple-700/60'
              }`}
            >
              <ZoomIn className="w-3 h-3 text-purple-300" />
              <span>Criar zoom</span>
            </button>

            {/* Excluir da montagem (Delete) */}
            <button
              type="button"
              onClick={handleDeleteSelection}
              title="Excluir trecho da montagem (atalho: Delete)"
              className="px-2.5 py-1 rounded-lg bg-rose-950/80 hover:bg-rose-900 text-rose-200 border border-rose-700/70 text-xs font-semibold flex items-center gap-1 shadow-sm"
            >
              <Scissors className="w-3 h-3 text-rose-400" />
              <span>Excluir (Delete)</span>
            </button>

            {/* Limpar seleção */}
            <button
              type="button"
              onClick={clearSelection}
              title="Limpar seleção (Esc)"
              className="p-1 rounded-lg hover:bg-slate-800 text-slate-400 hover:text-white"
              aria-label="Limpar seleção"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          </div>

          {/* Painel expansível de configuração do Zoom (Checkpoint E) */}
          {isConfiguringZoom && (
            <div className="w-full mt-2 pt-2 border-t border-slate-800/80 flex flex-wrap items-center justify-between gap-3 text-xs bg-slate-950/70 p-2.5 rounded-lg">
              <div className="flex flex-wrap items-center gap-4">
                <div className="flex items-center gap-1.5">
                  <span className="text-[10px] text-slate-400 font-semibold uppercase">Escala:</span>
                  <select
                    value={zoomScale}
                    onChange={(e) => setZoomScale(parseFloat(e.target.value))}
                    className="bg-slate-900 border border-slate-700 text-slate-200 text-xs rounded px-2 py-0.5"
                  >
                    <option value={1.05}>1.05× (Sutil)</option>
                    <option value={1.08}>1.08× (Padrão fala)</option>
                    <option value={1.12}>1.12× (Ênfase)</option>
                    <option value={1.18}>1.18× (Close-up)</option>
                    <option value={1.25}>1.25× (Destaque)</option>
                  </select>
                </div>

                <div className="flex items-center gap-1.5">
                  <span className="text-[10px] text-slate-400 font-semibold uppercase">Entrada:</span>
                  <input
                    type="number"
                    step="0.05"
                    min="0.1"
                    max="1.5"
                    value={zoomTransIn}
                    onChange={(e) => setZoomTransIn(parseFloat(e.target.value) || 0.35)}
                    className="w-16 bg-slate-900 border border-slate-700 text-slate-200 text-xs rounded px-1.5 py-0.5 text-center font-mono"
                  />
                  <span className="text-[10px] text-slate-500">s</span>
                </div>

                <div className="flex items-center gap-1.5">
                  <span className="text-[10px] text-slate-400 font-semibold uppercase">Saída:</span>
                  <input
                    type="number"
                    step="0.05"
                    min="0.1"
                    max="1.5"
                    value={zoomTransOut}
                    onChange={(e) => setZoomTransOut(parseFloat(e.target.value) || 0.35)}
                    className="w-16 bg-slate-900 border border-slate-700 text-slate-200 text-xs rounded px-1.5 py-0.5 text-center font-mono"
                  />
                  <span className="text-[10px] text-slate-500">s</span>
                </div>

                <div className="flex items-center gap-1.5">
                  <span className="text-[10px] text-slate-400 font-semibold uppercase">Foco:</span>
                  <select
                    value={zoomAnchor}
                    onChange={(e) => setZoomAnchor(e.target.value as any)}
                    className="bg-slate-900 border border-slate-700 text-slate-200 text-xs rounded px-2 py-0.5"
                  >
                    <option value="center">Centro</option>
                    <option value="top-third">Terço Superior (Rosto)</option>
                  </select>
                </div>
              </div>

              <button
                type="button"
                onClick={handleConfirmZoom}
                className="px-3 py-1 rounded bg-purple-600 hover:bg-purple-500 text-white font-medium flex items-center gap-1 shadow-sm text-xs"
              >
                <Check className="w-3.5 h-3.5" />
                <span>Aplicar sugestão</span>
              </button>
            </div>
          )}
        </div>
      )}

      {/* Feedback de status */}
      {statusMessage && (
        <div
          role="status"
          className={`mb-2 px-3 py-1.5 rounded-lg text-xs flex items-center justify-between gap-2 ${
            statusMessage.type === 'error'
              ? 'bg-rose-950/70 border border-rose-700 text-rose-200'
              : statusMessage.type === 'info'
                ? 'bg-slate-800 border border-slate-700 text-slate-200'
                : 'bg-emerald-950/70 border border-emerald-700 text-emerald-200'
          }`}
        >
          <span>{statusMessage.text}</span>
          <button
            type="button"
            onClick={() => setStatusMessage(null)}
            className="text-slate-400 hover:text-white"
            aria-label="Fechar mensagem"
          >
            <X className="w-3 h-3" />
          </button>
        </div>
      )}

      {/* Lista de palavras transcritas */}
      <div className="flex-1 min-h-0 overflow-y-auto pr-1 space-y-1 select-text">
        {words.length > 0 ? (
          <div className="leading-relaxed text-sm p-1">
            {words.map((w, index) => {
              const isMatch = matchSet.has(index);
              const isSelected = selectedWords.some((sw) => sw.seq === w.seq);
              const isCurrent = selectedSec !== null && intervalsOverlap(w.start, w.end, selectedSec, selectedSec + 0.1);
              const hesitated = hesitations.some((h) => intervalsOverlap(w.start, w.end, h.startSeconds, h.endSeconds));

              // Verifica se a palavra está em trecho excluído
              const isRemoved = removedIntervals.some((r) => intervalsOverlap(w.start, w.end, r.startSec, r.endSec));

              // Verifica se a palavra tem sugestão de zoom
              const hasZoom = wordZooms.some((z) => intervalsOverlap(w.start, w.end, z.startSec, z.endSec));

              let wordClass = 'text-slate-300 hover:text-white hover:bg-slate-800/60';

              if (isRemoved) {
                wordClass = 'line-through text-rose-400/80 bg-rose-950/30 decoration-rose-500/80 hover:bg-rose-950/50';
              } else if (isSelected) {
                wordClass = 'bg-cyan-500/35 text-white font-semibold ring-1 ring-cyan-400 rounded-sm';
              } else if (isMatch) {
                wordClass = 'bg-yellow-400 text-black font-bold rounded-sm';
              } else if (hasZoom) {
                wordClass = 'text-purple-200 bg-purple-950/40 border-b-2 border-purple-400';
              } else if (hesitated) {
                wordClass = 'text-rose-300 underline decoration-rose-500 decoration-2 underline-offset-2 hover:bg-rose-500/15';
              } else if (isCurrent) {
                wordClass = 'bg-cyan-500/20 text-cyan-200 font-medium rounded-sm';
              }

              return (
                <span
                  key={`w-${w.seq}-${w.start}`}
                  id={`transcript-word-${index}`}
                  onClick={(e) => handleWordClick(w.seq, w.start, e)}
                  title={`${w.text} [${formatClock(w.start)} – ${formatClock(w.end)}]${
                    isRemoved ? ' (Removido da montagem)' : hasZoom ? ' (Com zoom)' : ''
                  }`}
                  className={`inline-block px-1 py-0.5 mx-0.5 rounded cursor-pointer transition-colors text-xs select-none ${wordClass}`}
                >
                  {w.text}
                </span>
              );
            })}
          </div>
        ) : transcript.fullText ? (
          <p className="text-xs text-slate-300 leading-relaxed p-2">{transcript.fullText}</p>
        ) : (
          <p className="text-xs text-slate-500 p-2">Transcrição concluída sem palavras com tempo.</p>
        )}
      </div>
    </div>
  );
};
