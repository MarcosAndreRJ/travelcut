import React, { useState } from 'react';
import { Maximize2, Minimize2 } from 'lucide-react';
import { MediaIntervalPlayer } from '../media/MediaIntervalPlayer';
import { WaveformStrip } from '../media/WaveformStrip';
import { formatDraft, playerWindowOf, PLAYER_CONTEXT_SECONDS, type Interval } from './reviewModel';

export type CompareMode = 'before' | 'after';

interface BeforeAfterCompareProps {
  projectId: string;
  mediaId: string;
  /** Intervalo efetivo (ajustado quando houver "modify"). */
  interval: Interval;
  mode: CompareMode;
  onModeChange: (mode: CompareMode) => void;
  /** Alvo de busca externa (clique numa palavra da transcrição). */
  seekTarget: number | null;
  /** Tempo atual da mídia (segundos da fonte), vindo do player. */
  currentSeconds: number | null;
  onTimeUpdate: (sourceSeconds: number) => void;
  zoomTransform?: {
    scale: number;
    offsetX: number;
    offsetY: number;
    showSafeArea?: boolean;
  } | null;
}

const MODE_OPTIONS: { value: CompareMode; label: string; hint: string }[] = [
  { value: 'before', label: 'Antes', hint: 'Trecho inteiro com contexto' },
  { value: 'after', label: 'Depois', hint: 'Pula o intervalo, como ficaria com o corte' }
];

/** Antes/depois do candidato: player de intervalo + waveform sincronizado. Prévia de revisão, não frame-accurate. */
export const BeforeAfterCompare: React.FC<BeforeAfterCompareProps> = ({
  projectId,
  mediaId,
  interval,
  mode,
  onModeChange,
  seekTarget,
  currentSeconds,
  onTimeUpdate,
  zoomTransform = null
}) => {
  const windowSeconds = playerWindowOf(interval);
  // Altura limitada para vídeo, controles, intervalo e decisões caberem juntos; o usuário pode expandir.
  const [expanded, setExpanded] = useState(false);

  return (
    <section aria-label="Antes e depois do candidato" className="rounded-2xl border border-slate-800 bg-[#0a1222] p-4 space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-xs font-bold text-white">Antes e depois</h3>
        <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          aria-pressed={expanded}
          className="px-2 py-1 rounded-lg text-[11px] font-semibold border border-slate-700 text-slate-300 hover:text-white flex items-center gap-1"
        >
          {expanded ? <Minimize2 className="w-3 h-3" aria-hidden="true" /> : <Maximize2 className="w-3 h-3" aria-hidden="true" />}
          {expanded ? 'Reduzir vídeo' : 'Expandir vídeo'}
        </button>
        <div role="group" aria-label="Modo de comparação" className="flex gap-1">
          {MODE_OPTIONS.map((opt) => {
            const pressed = mode === opt.value;
            return (
              <button
                key={opt.value}
                type="button"
                aria-pressed={pressed}
                title={opt.hint}
                onClick={() => onModeChange(opt.value)}
                className={`px-3 py-1 rounded-lg text-[11px] font-semibold border transition-all focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400 ${
                  pressed
                    ? 'bg-cyan-500/20 text-cyan-200 border-cyan-500/50'
                    : 'text-slate-400 border-slate-700 hover:text-white'
                }`}
              >
                {opt.label}
              </button>
            );
          })}
        </div>
        </div>
      </div>

      <p className="text-[10px] text-slate-500">
        {MODE_OPTIONS.find((o) => o.value === mode)?.hint} · contexto de {PLAYER_CONTEXT_SECONDS} s · prévia de revisão, não é frame-accurate nem a timeline do DaVinci.
      </p>

      <MediaIntervalPlayer
        projectId={projectId}
        mediaId={mediaId}
        startSeconds={interval.start}
        endSeconds={interval.end}
        contextSeconds={PLAYER_CONTEXT_SECONDS}
        mode={mode}
        seekToSeconds={seekTarget}
        onTimeUpdate={onTimeUpdate}
        zoomTransform={zoomTransform}
        videoAreaClassName={expanded ? 'h-[65vh]' : 'h-[22vh] min-h-32 max-h-72'}
      />

      <div className="space-y-1">
        <div className="flex items-center justify-between text-[10px] font-mono text-slate-400">
          <span>Onda sonora · janela</span>
          <span>
            {formatDraft(windowSeconds.start)} s → {formatDraft(windowSeconds.end)} s
          </span>
        </div>
        <WaveformStrip
          projectId={projectId}
          mediaId={mediaId}
          startSeconds={windowSeconds.start}
          endSeconds={windowSeconds.end}
          highlight={{ start: interval.start, end: interval.end }}
          currentSeconds={currentSeconds}
        />
      </div>
    </section>
  );
};
