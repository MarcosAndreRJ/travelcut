import React from 'react';
import { Volume2, VolumeX, Lock, Unlock, Link2 } from 'lucide-react';
import type { TrackAudioState } from './multitrackModel';

export interface TrackHeaderControlsProps {
  trackId: string;
  label: string;
  kind: string;
  audioState: TrackAudioState;
  onVolumeChange: (trackId: string, volumeDb: number) => void;
  onMuteToggle: (trackId: string) => void;
  onLockToggle: (trackId: string) => void;
  className?: string;
}

/**
 * Controles de cabeçalho da trilha na timeline:
 * - Identificação da trilha (V1, V2, A1, A2, etc.)
 * - Nome e tipo (Vídeo, Diálogo, Música, B-roll)
 * - Botão de Mute (M)
 * - Botão de Lock (L)
 * - Controle de volume em dB (-60 dB a +12 dB) com indicação precisa
 * - Indicador de vinculação quando associada a outra trilha
 */
export const TrackHeaderControls: React.FC<TrackHeaderControlsProps> = ({
  trackId,
  label,
  kind,
  audioState,
  onVolumeChange,
  onMuteToggle,
  onLockToggle,
  className = ''
}) => {
  const isAudioTrack = trackId.startsWith('A') || kind === 'audio' || kind === 'music' || kind === 'effects';
  const isMuted = audioState.isMuted;
  const isLocked = audioState.isLocked;
  const volumeDb = audioState.volumeDb;

  return (
    <div
      className={`flex flex-col justify-between p-1.5 h-full border-r border-slate-800 bg-[#080d19] text-slate-200 select-none ${className}`}
      data-testid={`track-header-${trackId}`}
    >
      {/* Linha superior: ID, nome e link */}
      <div className="flex items-center justify-between gap-1 min-w-0">
        <div className="flex items-center gap-1 min-w-0">
          <span className="font-bold text-xs text-slate-100 font-mono px-1 py-0.5 rounded bg-slate-800/80">
            {trackId}
          </span>
          <span className="text-[11px] font-medium text-slate-300 truncate" title={label}>
            {label}
          </span>
        </div>
        {audioState.linkedTrackId && (
          <span
            title={`Trilha vinculada à ${audioState.linkedTrackId}`}
            className="text-cyan-400 p-0.5"
            aria-label={`Vinculada à ${audioState.linkedTrackId}`}
          >
            <Link2 className="w-3 h-3" />
          </span>
        )}
      </div>

      {/* Linha inferior: Mute, Lock e Volume Slider */}
      <div className="flex items-center justify-between gap-1.5 mt-1 pt-1 border-t border-slate-800/50">
        <div className="flex items-center gap-1">
          {/* Mute toggle */}
          <button
            type="button"
            onClick={() => onMuteToggle(trackId)}
            className={`px-1.5 py-0.5 rounded text-[10px] font-bold font-mono transition-colors focus:outline-none focus-visible:ring-1 focus-visible:ring-cyan-400 ${
              isMuted
                ? 'bg-rose-950/80 border border-rose-700/60 text-rose-300'
                : 'bg-slate-800/60 hover:bg-slate-700 text-slate-300'
            }`}
            title={isMuted ? 'Desmutar faixa' : 'Mutar faixa'}
            aria-label={`Mutar ${trackId}`}
          >
            {isMuted ? <VolumeX className="w-3 h-3" /> : 'M'}
          </button>

          {/* Lock toggle */}
          <button
            type="button"
            onClick={() => onLockToggle(trackId)}
            className={`px-1.5 py-0.5 rounded text-[10px] font-bold font-mono transition-colors focus:outline-none focus-visible:ring-1 focus-visible:ring-cyan-400 ${
              isLocked
                ? 'bg-amber-950/80 border border-amber-700/60 text-amber-300'
                : 'bg-slate-800/60 hover:bg-slate-700 text-slate-400'
            }`}
            title={isLocked ? 'Destrancar faixa' : 'Trancar faixa'}
            aria-label={`Trancar ${trackId}`}
          >
            {isLocked ? <Lock className="w-3 h-3" /> : 'L'}
          </button>
        </div>

        {/* Volume slider para faixas de áudio */}
        {isAudioTrack && (
          <div className="flex items-center gap-1 min-w-0" title={`Volume: ${volumeDb > 0 ? '+' : ''}${volumeDb} dB`}>
            <Volume2 className="w-2.5 h-2.5 text-slate-500 shrink-0" />
            <input
              type="range"
              min="-60"
              max="12"
              step="1"
              value={volumeDb}
              disabled={isLocked}
              onChange={(e) => onVolumeChange(trackId, parseFloat(e.target.value))}
              className="w-14 h-1 bg-slate-700 rounded-lg appearance-none cursor-pointer accent-cyan-400 disabled:opacity-40"
              aria-label={`Volume da faixa ${trackId}`}
            />
            <span className="font-mono text-[9px] text-slate-400 w-7 text-right shrink-0">
              {volumeDb <= -60 ? '-inf' : `${volumeDb > 0 ? '+' : ''}${volumeDb}`}
            </span>
          </div>
        )}
      </div>
    </div>
  );
};
