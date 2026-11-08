import React from 'react';
import { Link2, Link2Off, RefreshCw, Volume2 } from 'lucide-react';
import type { EditPlanDto, PlanClipDto } from '../../types/ipc';
import {
  isClipLinked,
  findLinkedClip,
  calculateSyncOffset,
  unlinkClip,
  canRelinkClips,
  relinkClips
} from './multitrackModel';

export interface MultitrackClipAudioSectionProps {
  plan: EditPlanDto;
  selectedClip: PlanClipDto | null;
  onPlanChange: (updatedPlan: EditPlanDto) => void;
  onSelectClip: (clipId: string | null) => void;
}

export const MultitrackClipAudioSection: React.FC<MultitrackClipAudioSectionProps> = ({
  plan,
  selectedClip,
  onPlanChange,
  onSelectClip
}) => {
  if (!selectedClip) {
    return (
      <div className="p-4 text-center text-xs text-slate-500">
        Nenhum clipe selecionado na timeline. Clique em um clipe em V1 ou A1 para inspecionar áudio e vínculo.
      </div>
    );
  }

  const linked = isClipLinked(selectedClip, plan);
  const otherClip = findLinkedClip(selectedClip, plan);
  const syncOffset = otherClip ? calculateSyncOffset(selectedClip, otherClip) : null;
  const relinkCheck = otherClip && !linked ? canRelinkClips(selectedClip, otherClip) : null;

  return (
    <div className="space-y-4 text-xs">
      <div className="p-3 rounded-lg bg-slate-900/60 border border-slate-800 space-y-2">
        <div className="flex items-center justify-between">
          <span className="font-semibold text-slate-200">Status de Vinculação</span>
          {linked ? (
            <span className="px-2 py-0.5 rounded text-[10px] font-mono bg-cyan-950/80 border border-cyan-700/60 text-cyan-400 flex items-center gap-1">
              <Link2 className="w-3 h-3" aria-hidden="true" />
              <span>VINCULADO</span>
            </span>
          ) : (
            <span className="px-2 py-0.5 rounded text-[10px] font-mono bg-slate-800 border border-slate-700 text-slate-400 flex items-center gap-1">
              <Link2Off className="w-3 h-3" aria-hidden="true" />
              <span>DESVINCULADO</span>
            </span>
          )}
        </div>

        <p className="text-[11px] text-slate-400">
          {linked
            ? 'Vídeo e áudio original estão vinculados. Operações de corte, movimento e exclusão preservam ambos.'
            : 'Áudio desvinculado. Pode ser movido ou editado de forma independente na timeline.'}
        </p>

        {syncOffset && !linked && (
          <div className="pt-2 border-t border-slate-800/80 flex items-center justify-between text-[11px]">
            <span className="text-slate-400">Desvio de Sincronização:</span>
            <span className={`font-mono font-bold ${syncOffset.inSync ? 'text-slate-300' : 'text-amber-400'}`}>
              {syncOffset.timelineOffsetFrames > 0 ? `+${syncOffset.timelineOffsetFrames}f` : `${syncOffset.timelineOffsetFrames}f`}
              {' '}({(syncOffset.sourceOffsetUs / 1000).toFixed(1)} ms)
            </span>
          </div>
        )}

        <div className="pt-2 flex items-center gap-2">
          {linked ? (
            <button
              type="button"
              onClick={() => onPlanChange(unlinkClip(plan, selectedClip.clipId))}
              className="px-3 py-1.5 rounded-lg bg-amber-950/40 hover:bg-amber-900/60 border border-amber-700/50 text-amber-300 flex items-center gap-1.5 transition-colors"
            >
              <Link2Off className="w-3.5 h-3.5" aria-hidden="true" />
              <span>Desvincular áudio</span>
            </button>
          ) : otherClip ? (
            <button
              type="button"
              onClick={() => onPlanChange(relinkClips(plan, selectedClip.clipId, otherClip.clipId, true))}
              className="px-3 py-1.5 rounded-lg bg-cyan-950/60 hover:bg-cyan-900/80 border border-cyan-700/60 text-cyan-300 flex items-center gap-1.5 transition-colors"
            >
              <RefreshCw className="w-3.5 h-3.5" aria-hidden="true" />
              <span>Revincular e Realinhar</span>
            </button>
          ) : null}
        </div>
      </div>
    </div>
  );
};
