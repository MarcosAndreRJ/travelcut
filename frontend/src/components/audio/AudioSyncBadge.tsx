import React from 'react';
import { Link2, Link2Off } from 'lucide-react';
import type { SyncOffsetResult } from './multitrackModel';

export interface AudioSyncBadgeProps {
  isLinked: boolean;
  syncOffset?: SyncOffsetResult | null;
  className?: string;
}

/**
 * Indicador visual de vinculação e sincronização de clipes na timeline.
 * Exibe ícone de elo quando vinculado e badge de desvio quando desvinculado.
 */
export const AudioSyncBadge: React.FC<AudioSyncBadgeProps> = ({
  isLinked,
  syncOffset,
  className = ''
}) => {
  if (isLinked) {
    return (
      <span
        title="Áudio e vídeo vinculados (V1/A1)"
        className={`inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[9px] font-mono bg-cyan-950/80 border border-cyan-700/60 text-cyan-400 ${className}`}
      >
        <Link2 className="w-2.5 h-2.5" aria-hidden="true" />
        <span>LINK</span>
      </span>
    );
  }

  if (syncOffset) {
    const offsetFrames = syncOffset.timelineOffsetFrames;
    const sign = offsetFrames > 0 ? '+' : '';
    const label = offsetFrames === 0 ? '±0f' : `${sign}${offsetFrames}f`;
    const toneClass = offsetFrames === 0
      ? 'bg-slate-800/80 border-slate-700/60 text-slate-300'
      : 'bg-amber-950/80 border-amber-700/60 text-amber-400';

    return (
      <span
        title={`Desvio de sincronização: ${label} (${(syncOffset.sourceOffsetUs / 1000).toFixed(1)} ms)`}
        className={`inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[9px] font-mono border ${toneClass} ${className}`}
      >
        <Link2Off className="w-2.5 h-2.5" aria-hidden="true" />
        <span>{label}</span>
      </span>
    );
  }

  return (
    <span
      title="Áudio desvinculado"
      className={`inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[9px] font-mono bg-slate-800/80 border border-slate-700/60 text-slate-400 ${className}`}
    >
      <Link2Off className="w-2.5 h-2.5" aria-hidden="true" />
      <span>UNLINK</span>
    </span>
  );
};
