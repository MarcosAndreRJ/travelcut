import React, { useMemo, useState } from 'react';
import { Search } from 'lucide-react';
import type { HesitationCandidateDto, TranscriptDto } from '../../types/ipc';
import { findWordMatches, formatClock, intervalsOverlap } from './analysisFormat';

interface TranscriptPanelProps {
  transcript: TranscriptDto;
  hesitations: HesitationCandidateDto[];
  selectedSec: number | null;
  onSeek: (sec: number) => void;
}

/** Transcrição real por palavras. Clique seleciona o tempo; não há player funcional nesta versão. */
export const TranscriptPanel: React.FC<TranscriptPanelProps> = ({ transcript, hesitations, selectedSec, onSeek }) => {
  const [query, setQuery] = useState('');
  const [matchCursor, setMatchCursor] = useState(-1);

  const matches = useMemo(() => findWordMatches(transcript.words, query), [transcript.words, query]);
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

  return (
    <div className="flex flex-col min-h-0 h-full">
      <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
        <div className="text-[10px] text-slate-400 font-mono">
          Idioma: {transcript.language || '—'} · Modelo: {transcript.modelId || '—'} · Áudio: {formatClock(transcript.audioDurationSeconds)}
        </div>
        <div className="flex items-center gap-2">
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
            {query.trim() ? `${matches.length} ocorrência(s)` : `${transcript.words.length} palavras`}
          </span>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto min-h-0 p-3 rounded-xl bg-[#0a1424] border border-slate-800/80">
        {transcript.words.length > 0 ? (
          <div className="flex flex-wrap gap-x-0.5 gap-y-1 text-xs leading-relaxed">
            {transcript.words.map((w) => {
              const isMatch = matchSet.has(w.seq);
              const isSelected = selectedSec !== null && selectedSec >= w.start && selectedSec < w.end;
              const hesitated = hesitations.some((h) => intervalsOverlap(w.start, w.end, h.startSeconds, h.endSeconds));

              const tone = isMatch
                ? 'bg-yellow-400 text-black font-bold'
                : hesitated
                  ? 'text-rose-300 underline decoration-rose-500 decoration-2 underline-offset-2 hover:bg-rose-500/15'
                  : 'text-slate-300 hover:bg-slate-800';

              return (
                <button
                  key={w.seq}
                  id={`transcript-word-${w.seq}`}
                  type="button"
                  onClick={() => onSeek(w.start)}
                  title={`${formatClock(w.start)} – ${formatClock(w.end)}`}
                  className={`px-1 py-0.5 rounded transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400 ${tone} ${
                    isSelected ? 'ring-1 ring-cyan-400 bg-cyan-500/20 text-white' : ''
                  }`}
                >
                  {w.text}
                  {hesitated && <span className="sr-only"> (hesitação)</span>}
                </button>
              );
            })}
          </div>
        ) : transcript.fullText ? (
          <p className="text-xs text-slate-300 leading-relaxed">{transcript.fullText}</p>
        ) : (
          <p className="text-xs text-slate-500">Transcrição concluída sem palavras com tempo.</p>
        )}
      </div>
    </div>
  );
};
