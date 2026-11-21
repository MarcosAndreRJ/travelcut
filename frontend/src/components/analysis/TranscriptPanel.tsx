import React, { useMemo, useState, useEffect, useCallback, useRef } from 'react';
import {
  Search,
  Scissors,
  ZoomIn,
  Play,
  RotateCcw,
  RotateCw,
  X,
  Check
} from 'lucide-react';
import type { HesitationCandidateDto, TranscriptDto } from '../../types/ipc';
import { findWordMatches, formatClock, intervalsOverlap } from './analysisFormat';
import type { CutFeedback, CutFeedbackAction, CutOutcome, RestoreResult } from './directCutController';
import type { ZoomApplyOutcome, ZoomRemoveOutcome } from './directZoomController';
import { CutFeedbackBanner, CutRestoreButton } from './CutEditControls';
import {
  DEFAULT_ZOOM_RAMP_SECONDS,
  DEFAULT_ZOOM_SCALE,
  ZOOM_RAMP_MAX_SECONDS,
  ZOOM_RAMP_MIN_SECONDS,
  formatZoomScale,
  validateZoomInput,
  type ZoomFocus
} from '../media/zoomWindows';

/** Trecho cortado no host (efetivo aprovado), em segundos da fonte. Marcação derivada, nunca local. */
export interface RemovedInterval {
  /** suggestionId persistido no host: alvo de 'Restaurar trecho'. */
  suggestionId: string;
  startSec: number;
  endSec: number;
}

/** Cortes efetivos que se sobrepõem ao trecho selecionado (os que 'Restaurar trecho' devolve ao plano). */
export const restorableCutsIn = (
  startSec: number,
  endSec: number,
  removed: ReadonlyArray<RemovedInterval>
): RemovedInterval[] => removed.filter((r) => intervalsOverlap(startSec, endSec, r.startSec, r.endSec));

/** Zoom efetivo do host, em segundos da fonte. Marcação derivada do plano/lista, nunca local. */
export interface TranscriptZoomMark {
  /** suggestionId persistido no host: alvo de 'Remover zoom'. */
  suggestionId: string;
  startSec: number;
  endSec: number;
  scale: number;
}

/** Pedido de zoom feito pela transcrição (a view chama zoom.apply com o mediaId do clipe). */
export interface TranscriptZoomRequest {
  startSec: number;
  endSec: number;
  text: string;
  scale: number;
  focus: ZoomFocus;
  transitionInSeconds: number;
  transitionOutSeconds: number;
}

/** Edição por transcrição: zoom direto (aplicar/remover) com confirmação própria. */
export interface TranscriptZoomEditing {
  zoomWindows: ReadonlyArray<TranscriptZoomMark>;
  busy?: boolean;
  /** Confirmação do último zoom/remoção, com Desfazer/Refazer próprio. */
  feedback?: CutFeedback | null;
  onApply: (input: TranscriptZoomRequest) => Promise<ZoomApplyOutcome | null>;
  onRemove: (startSec: number, endSec: number) => Promise<ZoomRemoveOutcome | null>;
  onFeedbackAction?: (action: CutFeedbackAction) => void;
}

/** Edição por transcrição: corte direto e desfazer/refazer do host. */
export interface TranscriptCutEditing {
  removedIntervals: ReadonlyArray<RemovedInterval>;
  canUndo: boolean;
  canRedo: boolean;
  busy?: boolean;
  /** Confirmação do último corte/restauração, com Desfazer/Refazer próprio. */
  feedback?: CutFeedback | null;
  onCut: (startSec: number, endSec: number, text: string) => Promise<CutOutcome | null>;
  onRestore?: (startSec: number, endSec: number) => Promise<RestoreResult | null>;
  onFeedbackAction?: (action: CutFeedbackAction) => void;
  onUndo: () => void;
  onRedo: () => void;
}

interface TranscriptPanelProps {
  transcript: TranscriptDto;
  hesitations: HesitationCandidateDto[];
  selectedSec: number | null;
  onSeek: (sec: number) => void;
  cutEditing?: TranscriptCutEditing;
  zoomEditing?: TranscriptZoomEditing;
}

/**
 * Transcrição interativa por palavras. Selecionar palavras e cortar (Cortar trecho ou Delete) passa pelo host;
 * o tachado vem de cutEditing.removedIntervals (cortes efetivos aprovados). Zoom também passa pelo host
 * (zoomEditing): a marcação vem de zoomWindows (zooms efetivos aprovados).
 */
export const TranscriptPanel: React.FC<TranscriptPanelProps> = ({
  transcript,
  hesitations,
  selectedSec,
  onSeek,
  cutEditing,
  zoomEditing
}) => {
  const [query, setQuery] = useState('');
  const [matchCursor, setMatchCursor] = useState(-1);

  // Seleção de palavras por índice de sequência (startSeq até endSeq inclusivo)
  const [selectionRange, setSelectionRange] = useState<{ startSeq: number; endSeq: number } | null>(null);
  const [anchorSeq, setAnchorSeq] = useState<number | null>(null);

  // Configuração do zoom (painel contextual da seleção)
  const [isConfiguringZoom, setIsConfiguringZoom] = useState(false);
  const [zoomScale, setZoomScale] = useState<number>(DEFAULT_ZOOM_SCALE);
  const [zoomTransIn, setZoomTransIn] = useState<number>(DEFAULT_ZOOM_RAMP_SECONDS);
  const [zoomTransOut, setZoomTransOut] = useState<number>(DEFAULT_ZOOM_RAMP_SECONDS);
  const [zoomFocus, setZoomFocus] = useState<ZoomFocus>('center');

  const containerRef = useRef<HTMLDivElement>(null);

  const words = transcript.words || [];
  const removedIntervals = cutEditing?.removedIntervals ?? [];
  const zoomWindows = zoomEditing?.zoomWindows ?? [];

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

  // Cortes efetivos sobrepostos à seleção (ids do host): alvo de 'Restaurar trecho'.
  const restorableCuts = useMemo(
    () => (selectionDetails ? restorableCutsIn(selectionDetails.startSec, selectionDetails.endSec, removedIntervals) : []),
    [selectionDetails, removedIntervals]
  );

  // Zooms efetivos sobrepostos à seleção (ids do host): alvo de 'Remover zoom'.
  const removableZooms = useMemo(
    () =>
      selectionDetails
        ? zoomWindows.filter((z) => intervalsOverlap(selectionDetails.startSec, selectionDetails.endSec, z.startSec, z.endSec))
        : [],
    [selectionDetails, zoomWindows]
  );

  // Validação da configuração do zoom para a seleção atual (escala, rampas e soma pela duração da fala).
  const zoomError = selectionDetails
    ? validateZoomInput({
        startSec: selectionDetails.startSec,
        endSec: selectionDetails.endSec,
        scale: zoomScale,
        transitionInSeconds: zoomTransIn,
        transitionOutSeconds: zoomTransOut
      })
    : null;

  // Corte direto da seleção (Cortar trecho / Delete). Só limpa a seleção quando o host aplicou o corte.
  const handleCutSelection = useCallback(async () => {
    if (!selectionDetails || !cutEditing) return;
    const { startSec, endSec, text } = selectionDetails;
    if (endSec <= startSec) return;
    const outcome = await cutEditing.onCut(startSec, endSec, text);
    if (outcome?.status === 'applied') clearSelection();
  }, [selectionDetails, cutEditing, clearSelection]);

  // Restaura os cortes sobrepostos à seleção. Erro do host aparece na confirmação (a view trata).
  const handleRestoreSelection = useCallback(async () => {
    if (!selectionDetails || !cutEditing?.onRestore) return;
    const outcome = await cutEditing.onRestore(selectionDetails.startSec, selectionDetails.endSec);
    if (outcome?.status === 'restored') clearSelection();
  }, [selectionDetails, cutEditing, clearSelection]);

  // Zoom direto da seleção (zoom.apply no host). Só limpa a seleção quando o host confirmou (applied).
  const handleApplyZoom = useCallback(async () => {
    if (!selectionDetails || !zoomEditing || zoomError) return;
    const { startSec, endSec, text } = selectionDetails;
    const outcome = await zoomEditing.onApply({
      startSec,
      endSec,
      text,
      scale: zoomScale,
      focus: zoomFocus,
      transitionInSeconds: zoomTransIn,
      transitionOutSeconds: zoomTransOut
    });
    if (outcome?.status === 'applied') clearSelection();
  }, [selectionDetails, zoomEditing, zoomError, zoomScale, zoomFocus, zoomTransIn, zoomTransOut, clearSelection]);

  // Remove o zoom sobreposto à seleção (decision.submit reject do zoom; cortes permanecem).
  const handleRemoveZoom = useCallback(async () => {
    if (!selectionDetails || !zoomEditing) return;
    const outcome = await zoomEditing.onRemove(selectionDetails.startSec, selectionDetails.endSec);
    if (outcome?.status === 'removed') clearSelection();
  }, [selectionDetails, zoomEditing, clearSelection]);

  // Atalhos de teclado protegidos: Delete corta a seleção, Esc limpa. Ctrl+Z/Y ficam na view da Biblioteca.
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const activeTag = (document.activeElement as HTMLElement)?.tagName;
      if (['INPUT', 'TEXTAREA', 'SELECT'].includes(activeTag) || (document.activeElement as HTMLElement)?.isContentEditable) {
        return;
      }

      if ((e.key === 'Delete' || e.key === 'Backspace') && selectionRange) {
        e.preventDefault();
        void handleCutSelection();
        return;
      }

      if (e.key === 'Escape' && selectionRange) {
        e.preventDefault();
        clearSelection();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [selectionRange, handleCutSelection, clearSelection]);

  return (
    <div ref={containerRef} className="flex flex-col min-h-0 h-full relative" tabIndex={0}>
      {/* Cabeçalho de busca e estatísticas */}
      <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
        <div className="text-[10px] text-slate-400 font-mono">
          Idioma: {transcript.language || '—'} · Modelo: {transcript.modelId || '—'} · Áudio: {formatClock(transcript.audioDurationSeconds)}
        </div>
        <div className="flex items-center gap-2">
          {/* Desfazer/Refazer do último corte (host) */}
          <div className="flex items-center gap-1 border-r border-slate-800 pr-2 mr-1">
            <button
              type="button"
              onClick={() => cutEditing?.onUndo()}
              disabled={!cutEditing?.canUndo}
              title="Desfazer corte (Ctrl+Z)"
              className="p-1 rounded bg-slate-800/80 hover:bg-slate-700 disabled:opacity-40 text-slate-300 disabled:hover:bg-slate-800/80"
              aria-label="Desfazer"
            >
              <RotateCcw className="w-3 h-3" />
            </button>
            <button
              type="button"
              onClick={() => cutEditing?.onRedo()}
              disabled={!cutEditing?.canRedo}
              title="Refazer corte (Ctrl+Y)"
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

      {/* Barra de ação contextual quando palavras estão selecionadas */}
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

            {/* Configurar zoom direto (aplicado no host pelo botão do painel) */}
            <button
              type="button"
              onClick={() => setIsConfiguringZoom((prev) => !prev)}
              disabled={!zoomEditing || zoomEditing.busy}
              aria-expanded={isConfiguringZoom}
              title="Aplicar zoom direto ao trecho (efeito gravado no projeto e no plano)"
              className={`px-2 py-1 rounded-lg text-xs font-medium flex items-center gap-1 border transition-colors disabled:opacity-40 ${
                isConfiguringZoom
                  ? 'bg-purple-600 text-white border-purple-400'
                  : 'bg-purple-950/70 hover:bg-purple-900/90 text-purple-200 border-purple-700/60'
              }`}
            >
              <ZoomIn className="w-3 h-3 text-purple-300" />
              <span>Zoom…</span>
            </button>

            {/* Remover zoom: só quando a seleção atinge um zoom efetivo do host */}
            {removableZooms.length > 0 && zoomEditing && (
              <button
                type="button"
                onClick={() => void handleRemoveZoom()}
                disabled={zoomEditing.busy}
                title={`Remove ${removableZooms.length === 1 ? 'o zoom' : `os ${removableZooms.length} zooms`} sobreposto(s); os cortes permanecem`}
                className="px-2.5 py-1 rounded-lg bg-purple-950/80 hover:bg-purple-900 disabled:opacity-40 text-purple-200 border border-purple-700/70 text-xs font-semibold flex items-center gap-1 shadow-sm"
              >
                <X className="w-3 h-3 text-purple-300" aria-hidden="true" />
                <span>Remover zoom</span>
              </button>
            )}

            {/* Restaurar trecho: só quando a seleção atinge palavras já cortadas (cortes efetivos do host) */}
            <CutRestoreButton
              count={cutEditing?.onRestore ? restorableCuts.length : 0}
              busy={cutEditing?.busy}
              onRestore={() => void handleRestoreSelection()}
            />

            {/* Cortar trecho (host; atalho Delete) */}
            <button
              type="button"
              onClick={() => void handleCutSelection()}
              disabled={!cutEditing || cutEditing.busy}
              title="Cortar trecho selecionado (atalho: Delete)"
              className="px-2.5 py-1 rounded-lg bg-rose-950/80 hover:bg-rose-900 disabled:opacity-40 text-rose-200 border border-rose-700/70 text-xs font-semibold flex items-center gap-1 shadow-sm"
            >
              <Scissors className="w-3 h-3 text-rose-400" />
              <span>Cortar trecho</span>
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

          {/* Painel de configuração do zoom direto */}
          {isConfiguringZoom && zoomEditing && (
            <div className="w-full mt-2 pt-2 border-t border-slate-800/80 flex flex-wrap items-center justify-between gap-3 text-xs bg-slate-950/70 p-2.5 rounded-lg">
              <div className="flex flex-wrap items-center gap-4">
                <div className="flex items-center gap-1.5">
                  <span className="text-[10px] text-slate-400 font-semibold uppercase">Escala:</span>
                  <select
                    aria-label="Escala do zoom"
                    value={zoomScale}
                    onChange={(e) => setZoomScale(parseFloat(e.target.value))}
                    className="bg-slate-900 border border-slate-700 text-slate-200 text-xs rounded px-2 py-0.5"
                  >
                    <option value={1.05}>1,05× (Sutil)</option>
                    <option value={1.08}>1,08× (Padrão fala)</option>
                    <option value={1.1}>1,10× (Ênfase)</option>
                    <option value={1.12}>1,12× (Destaque)</option>
                  </select>
                </div>

                <div className="flex items-center gap-1.5">
                  <span className="text-[10px] text-slate-400 font-semibold uppercase">Entrada:</span>
                  <input
                    type="number"
                    aria-label="Rampa de entrada"
                    step="0.05"
                    min={ZOOM_RAMP_MIN_SECONDS}
                    max={ZOOM_RAMP_MAX_SECONDS}
                    value={zoomTransIn}
                    onChange={(e) => setZoomTransIn(parseFloat(e.target.value) || DEFAULT_ZOOM_RAMP_SECONDS)}
                    className="w-16 bg-slate-900 border border-slate-700 text-slate-200 text-xs rounded px-1.5 py-0.5 text-center font-mono"
                  />
                  <span className="text-[10px] text-slate-500">s</span>
                </div>

                <div className="flex items-center gap-1.5">
                  <span className="text-[10px] text-slate-400 font-semibold uppercase">Saída:</span>
                  <input
                    type="number"
                    aria-label="Rampa de saída"
                    step="0.05"
                    min={ZOOM_RAMP_MIN_SECONDS}
                    max={ZOOM_RAMP_MAX_SECONDS}
                    value={zoomTransOut}
                    onChange={(e) => setZoomTransOut(parseFloat(e.target.value) || DEFAULT_ZOOM_RAMP_SECONDS)}
                    className="w-16 bg-slate-900 border border-slate-700 text-slate-200 text-xs rounded px-1.5 py-0.5 text-center font-mono"
                  />
                  <span className="text-[10px] text-slate-500">s</span>
                </div>

                <div className="flex items-center gap-1.5">
                  <span className="text-[10px] text-slate-400 font-semibold uppercase">Foco:</span>
                  <select
                    aria-label="Foco do zoom"
                    value={zoomFocus}
                    onChange={(e) => setZoomFocus(e.target.value as ZoomFocus)}
                    className="bg-slate-900 border border-slate-700 text-slate-200 text-xs rounded px-2 py-0.5"
                  >
                    <option value="center">Centro</option>
                    <option value="top-third">Terço Superior (Rosto)</option>
                  </select>
                </div>
              </div>

              <div className="flex flex-col items-end gap-1">
                {zoomError && (
                  <span role="alert" className="text-[11px] text-rose-300 max-w-xs text-right">
                    {zoomError}
                  </span>
                )}
                <button
                  type="button"
                  onClick={() => void handleApplyZoom()}
                  disabled={!!zoomError || zoomEditing.busy}
                  title={`Aplica o zoom ${formatZoomScale(zoomScale)} ao trecho no host`}
                  className="px-3 py-1 rounded bg-purple-600 hover:bg-purple-500 disabled:opacity-40 text-white font-medium flex items-center gap-1 shadow-sm text-xs"
                >
                  <Check className="w-3.5 h-3.5" />
                  <span>Aplicar zoom</span>
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Confirmação do corte/restauração, com Desfazer ou Refazer na própria mensagem */}
      {cutEditing?.feedback && (
        <div className="mb-2">
          <CutFeedbackBanner
            feedback={cutEditing.feedback}
            busy={cutEditing.busy}
            onAction={cutEditing.onFeedbackAction}
          />
        </div>
      )}

      {/* Confirmação do zoom/remoção, com Desfazer ou Refazer próprio */}
      {zoomEditing?.feedback && (
        <div className="mb-2" data-testid="transcript-zoom-feedback">
          <CutFeedbackBanner
            feedback={zoomEditing.feedback}
            busy={zoomEditing.busy}
            onAction={zoomEditing.onFeedbackAction}
          />
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

              // Palavra dentro de corte confirmado pelo host
              const isRemoved = removedIntervals.some((r) => intervalsOverlap(w.start, w.end, r.startSec, r.endSec));

              // Palavra dentro de zoom efetivo do host (persistente: vem do plano/lista)
              const zoomHere = zoomWindows.find((z) => intervalsOverlap(w.start, w.end, z.startSec, z.endSec)) ?? null;
              const hasZoom = zoomHere !== null;

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
                  data-zoom={hasZoom ? 'true' : undefined}
                  onClick={(e) => handleWordClick(w.seq, w.start, e)}
                  title={`${w.text} [${formatClock(w.start)} – ${formatClock(w.end)}]${
                    isRemoved ? ' (Cortado)' : zoomHere ? ` (Com zoom ${formatZoomScale(zoomHere.scale)})` : ''
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
