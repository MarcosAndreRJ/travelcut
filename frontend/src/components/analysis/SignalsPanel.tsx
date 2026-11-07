import React, { useId } from 'react';
import { stripPositionToSeconds } from '../media/mediaClipModel';
import type { HesitationCandidateDto, VadSilenceSegmentDto } from '../../types/ipc';
import {
  confidenceText,
  confidenceTone,
  formatClock,
  formatSeconds,
  hesitationTypeLabel
} from './analysisFormat';

interface SignalsListProps {
  silences: VadSilenceSegmentDto[];
  silenceReason: string | null;
  silenceCompleted: boolean;
  hesitations: HesitationCandidateDto[];
  transcriptionCompleted: boolean;
  transcriptionReason: string | null;
  selectedSec: number | null;
  onSeek: (sec: number) => void;
}

const HesitationItem: React.FC<{ item: HesitationCandidateDto; onSeek: (sec: number) => void }> = ({ item, onSeek }) => {
  const tipId = useId();
  return (
    <li className="relative group">
      <button
        type="button"
        onClick={() => onSeek(item.startSeconds)}
        aria-describedby={tipId}
        className="w-full text-left flex flex-wrap items-center gap-x-2 gap-y-0.5 px-2.5 py-1.5 rounded-lg bg-rose-500/10 border border-rose-500/30 text-[11px] text-rose-200 hover:bg-rose-500/20 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400"
      >
        <span className="font-mono font-bold">{item.detectedWord || '—'}</span>
        <span className="text-slate-300">{hesitationTypeLabel(item.type)}</span>
        <span className="font-mono text-rose-300/80">
          {formatClock(item.startSeconds)} → {formatClock(item.endSeconds)}
        </span>
        <span className={`px-1.5 py-0.5 rounded border text-[10px] font-bold ${confidenceTone(item.confidenceBand)}`}>
          Confiança {confidenceText(item.confidenceBand)}
        </span>
        {item.needsHumanReview && <span className="text-[10px] font-semibold text-amber-300">Revisão humana</span>}
      </button>
      <div
        id={tipId}
        role="tooltip"
        className="pointer-events-none absolute left-0 top-full z-20 mt-1 hidden w-80 max-w-[90vw] rounded-lg border border-slate-700 bg-[#050a14] p-2.5 text-[11px] text-slate-200 shadow-xl group-hover:block group-focus-within:block"
      >
        <p>
          <span className="text-slate-400">Antes: </span>"{item.contextBefore || '—'}"
        </p>
        <p className="mt-1">
          <span className="text-slate-400">Depois: </span>"{item.contextAfter || '—'}"
        </p>
      </div>
    </li>
  );
};

/** Silêncios e hesitações reais. Estados não concluídos mostram o motivo do host, sem lista inventada. */
export const SignalsList: React.FC<SignalsListProps> = ({
  silences,
  silenceReason,
  silenceCompleted,
  hesitations,
  transcriptionCompleted,
  transcriptionReason,
  onSeek
}) => (
  <div className="space-y-4">
    <section aria-labelledby="silences-title" className="space-y-2">
      <h4 id="silences-title" className="text-[11px] font-bold text-amber-300 uppercase tracking-wider">
        Silêncios ({silences.length})
      </h4>
      {!silenceCompleted ? (
        <p className="text-[11px] text-slate-400">{silenceReason ?? 'Silêncios ainda não disponíveis para este clipe.'}</p>
      ) : silences.length === 0 ? (
        <p className="text-[11px] text-slate-500">Nenhum silêncio detectado.</p>
      ) : (
        <ul className="space-y-1.5">
          {silences.map((s, idx) => (
            <li key={`${s.startSeconds}-${idx}`}>
              <button
                type="button"
                onClick={() => onSeek(s.startSeconds)}
                className="w-full text-left flex flex-wrap items-center gap-x-2 gap-y-0.5 px-2.5 py-1.5 rounded-lg bg-amber-500/10 border border-amber-500/30 text-[11px] text-amber-200 hover:bg-amber-500/20 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400"
              >
                <span className="font-mono font-bold">{formatSeconds(s.durationSeconds)}</span>
                <span className="font-mono text-amber-300/80">
                  {formatClock(s.startSeconds)} → {formatClock(s.endSeconds)}
                </span>
                <span className="text-slate-300">ruído {Number.isFinite(s.noiseDb) ? `${s.noiseDb.toFixed(0)} dB` : '—'}</span>
                <span className={`px-1.5 py-0.5 rounded border text-[10px] font-bold ${confidenceTone(s.confidence)}`}>
                  Confiança {confidenceText(s.confidence)}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>

    <section aria-labelledby="hesitations-title" className="space-y-2">
      <h4 id="hesitations-title" className="text-[11px] font-bold text-rose-300 uppercase tracking-wider">
        Hesitações ({hesitations.length})
      </h4>
      {!transcriptionCompleted ? (
        <p className="text-[11px] text-slate-400">
          Hesitações dependem da transcrição: {transcriptionReason ?? 'ainda não disponíveis para este clipe.'}
        </p>
      ) : hesitations.length === 0 ? (
        <p className="text-[11px] text-slate-500">Nenhuma hesitação detectada.</p>
      ) : (
        <ul className="space-y-1.5">
          {hesitations.map((h, idx) => (
            <HesitationItem key={`${h.startSeconds}-${idx}`} item={h} onSeek={onSeek} />
          ))}
        </ul>
      )}
    </section>
  </div>
);

interface ClipTimelineStripProps {
  durationSeconds: number;
  silences: VadSilenceSegmentDto[];
  hesitations: HesitationCandidateDto[];
  selectedSec: number | null;
  onSeek: (sec: number) => void;
}

/** Linha do tempo do clipe com marcadores reais de silêncio (âmbar) e hesitação (rosa). */
export const ClipTimelineStrip: React.FC<ClipTimelineStripProps> = ({
  durationSeconds,
  silences,
  hesitations,
  selectedSec,
  onSeek
}) => {
  if (!(durationSeconds > 0)) {
    return (
      <div className="h-8 w-full rounded-lg border border-slate-800 bg-[#0a1222] flex items-center px-3 text-[10px] text-slate-500">
        Duração indisponível: marcadores não exibidos.
      </div>
    );
  }

  const pct = (sec: number): string => `${Math.min(100, Math.max(0, (sec / durationSeconds) * 100))}%`;

  const handleStripClick = (event: React.MouseEvent<HTMLDivElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    const seconds = stripPositionToSeconds(event.clientX, rect.left, rect.width, durationSeconds);
    if (seconds !== null) onSeek(seconds);
  };

  return (
    <div
      role="group"
      aria-label="Linha do clipe com silêncios e hesitações"
      onClick={handleStripClick}
      className="relative h-8 w-full rounded-lg border border-slate-800 bg-[#0a1222] overflow-hidden cursor-pointer"
    >
      {silences.map((s, idx) => (
        <button
          key={`s-${idx}`}
          type="button"
          onClick={(event) => {
            event.stopPropagation();
            onSeek(s.startSeconds);
          }}
          title={`Silêncio ${formatSeconds(s.durationSeconds)} em ${formatClock(s.startSeconds)}`}
          aria-label={`Silêncio de ${formatSeconds(s.durationSeconds)} em ${formatClock(s.startSeconds)}`}
          className="absolute top-1 bottom-1 rounded-sm bg-amber-500/50 hover:bg-amber-400/70 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400"
          style={{
            left: pct(s.startSeconds),
            width: `${Math.max(0.4, (s.durationSeconds / durationSeconds) * 100)}%`
          }}
        />
      ))}
      {hesitations.map((h, idx) => (
        <button
          key={`h-${idx}`}
          type="button"
          onClick={(event) => {
            event.stopPropagation();
            onSeek(h.startSeconds);
          }}
          title={`Hesitação "${h.detectedWord}" em ${formatClock(h.startSeconds)}`}
          aria-label={`Hesitação "${h.detectedWord}" em ${formatClock(h.startSeconds)}`}
          className="absolute top-0 bottom-0 w-1 bg-rose-500 hover:bg-rose-300 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400"
          style={{ left: pct(h.startSeconds) }}
        />
      ))}
      {selectedSec !== null && (
        <div className="absolute top-0 bottom-0 w-0.5 bg-cyan-400 pointer-events-none" style={{ left: pct(selectedSec) }} />
      )}
    </div>
  );
};
