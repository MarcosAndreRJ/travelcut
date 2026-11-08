import React, { useState } from 'react';
import { Video, Plus } from 'lucide-react';
import type { EditPlanDto, ProjectSummary } from '../../types/ipc';
import { insertBrollClip } from './multitrackModel';

export interface MultitrackBrollSectionProps {
  plan: EditPlanDto;
  projectSummary?: ProjectSummary | null;
  onPlanChange: (updatedPlan: EditPlanDto) => void;
}

export const MultitrackBrollSection: React.FC<MultitrackBrollSectionProps> = ({
  plan,
  projectSummary,
  onPlanChange
}) => {
  const [selectedMediaId, setSelectedMediaId] = useState<string>('');
  const [startFrame, setStartFrame] = useState<number>(0);
  const [durationFrames, setDurationFrames] = useState<number>(90);
  const [enableAudio, setEnableAudio] = useState<boolean>(false);

  const handleInsert = (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedMediaId) return;
    const updated = insertBrollClip(plan, {
      mediaId: selectedMediaId,
      relativePath: `assets/broll_${selectedMediaId}.mp4`,
      sourceInUs: 0,
      sourceOutUs: Math.round((durationFrames / 30) * 1_000_000),
      timelineStartFrame: startFrame,
      durationFrames,
      enableAudio
    });
    onPlanChange(updated);
  };

  return (
    <form onSubmit={handleInsert} className="p-3 rounded-lg bg-slate-900/60 border border-slate-800 space-y-3 text-xs">
      <div className="flex items-center gap-1.5 font-semibold text-slate-200">
        <Video className="w-3.5 h-3.5 text-amber-400" aria-hidden="true" />
        <span>Inserir B-roll sobreposto (Faixa V2)</span>
      </div>
      <p className="text-[11px] text-slate-400">
        B-rolls sobrepõem visualmente a faixa V1 no intervalo definido, preservando o diálogo da A1.
      </p>
      <div className="space-y-1">
        <label className="text-[11px] text-slate-400">Mídia / Asset de Vídeo:</label>
        <input
          type="text"
          value={selectedMediaId}
          onChange={(e) => setSelectedMediaId(e.target.value)}
          placeholder="ID ou nome da mídia de B-roll"
          className="w-full px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700 text-slate-200 focus:outline-none focus:border-cyan-500 font-mono text-xs"
          required
        />
      </div>
      <div className="grid grid-cols-2 gap-2">
        <div className="space-y-1">
          <label className="text-[11px] text-slate-400">Início (frame):</label>
          <input
            type="number"
            min={0}
            value={startFrame}
            onChange={(e) => setStartFrame(Math.max(0, parseInt(e.target.value, 10) || 0))}
            className="w-full px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700 text-slate-200 focus:outline-none focus:border-cyan-500 font-mono text-xs"
          />
        </div>
        <div className="space-y-1">
          <label className="text-[11px] text-slate-400">Duração (frames):</label>
          <input
            type="number"
            min={1}
            value={durationFrames}
            onChange={(e) => setDurationFrames(Math.max(1, parseInt(e.target.value, 10) || 1))}
            className="w-full px-2.5 py-1.5 rounded bg-slate-950 border border-slate-700 text-slate-200 focus:outline-none focus:border-cyan-500 font-mono text-xs"
          />
        </div>
      </div>
      <label className="flex items-center gap-2 cursor-pointer pt-1">
        <input
          type="checkbox"
          checked={enableAudio}
          onChange={(e) => setEnableAudio(e.target.checked)}
          className="rounded border-slate-700 bg-slate-950 text-cyan-500 focus:ring-0"
        />
        <span className="text-[11px] text-slate-300">Reproduzir áudio original do B-roll (se desmarcado, fica silenciado)</span>
      </label>
      <button
        type="submit"
        disabled={!selectedMediaId}
        className="w-full px-3 py-2 rounded-lg bg-amber-600 hover:bg-amber-500 disabled:opacity-40 text-slate-950 font-semibold flex items-center justify-center gap-1.5 transition-colors"
      >
        <Plus className="w-3.5 h-3.5" aria-hidden="true" />
        <span>Adicionar B-roll na V2</span>
      </button>
    </form>
  );
};
