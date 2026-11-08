import React, { useState } from 'react';
import { Music, Plus } from 'lucide-react';
import type { EditPlanDto, ProjectSummary } from '../../types/ipc';
import { insertMusicClip } from './multitrackModel';

export interface MultitrackMusicSectionProps {
  plan: EditPlanDto;
  projectSummary?: ProjectSummary | null;
  onPlanChange: (updatedPlan: EditPlanDto) => void;
}

export const MultitrackMusicSection: React.FC<MultitrackMusicSectionProps> = ({
  plan,
  projectSummary,
  onPlanChange
}) => {
  const [selectedMediaId, setSelectedMediaId] = useState<string>('');
  const [startFrame, setStartFrame] = useState<number>(0);
  const [durationFrames, setDurationFrames] = useState<number>(300);
  const [volumeDb, setVolumeDb] = useState<number>(-12);

  const handleInsert = (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedMediaId) return;
    const updated = insertMusicClip(plan, {
      mediaId: selectedMediaId,
      relativePath: `assets/music_${selectedMediaId}.mp3`,
      sourceInUs: 0,
      sourceOutUs: Math.round((durationFrames / 30) * 1_000_000),
      timelineStartFrame: startFrame,
      durationFrames,
      volumeDb
    });
    onPlanChange(updated);
  };

  return (
    <form onSubmit={handleInsert} className="p-3 rounded-lg bg-slate-900/60 border border-slate-800 space-y-3 text-xs">
      <div className="flex items-center gap-1.5 font-semibold text-slate-200">
        <Music className="w-3.5 h-3.5 text-indigo-400" aria-hidden="true" />
        <span>Inserir Trilha Musical (Faixa A2)</span>
      </div>
      <p className="text-[11px] text-slate-400">
        Músicas são reproduzidas em segundo plano com volume ajustável e waveform renderizada.
      </p>
      <div className="space-y-1">
        <label className="text-[11px] text-slate-400">Arquivo de Áudio / Música:</label>
        <input
          type="text"
          value={selectedMediaId}
          onChange={(e) => setSelectedMediaId(e.target.value)}
          placeholder="ID ou nome do arquivo de áudio"
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
      <div className="space-y-1">
        <div className="flex justify-between text-[11px]">
          <span className="text-slate-400">Volume Inicial:</span>
          <span className="font-mono text-indigo-400">{volumeDb} dB</span>
        </div>
        <input
          type="range"
          min={-60}
          max={12}
          step={1}
          value={volumeDb}
          onChange={(e) => setVolumeDb(parseInt(e.target.value, 10))}
          className="w-full accent-indigo-500"
        />
      </div>
      <button
        type="submit"
        disabled={!selectedMediaId}
        className="w-full px-3 py-2 rounded-lg bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white font-semibold flex items-center justify-center gap-1.5 transition-colors"
      >
        <Plus className="w-3.5 h-3.5" aria-hidden="true" />
        <span>Adicionar Música na A2</span>
      </button>
    </form>
  );
};
