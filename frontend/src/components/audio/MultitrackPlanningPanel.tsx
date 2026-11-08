import React, { useState } from 'react';
import { Layers, Plus, Link2, Video, Music, AlertTriangle } from 'lucide-react';
import type { EditPlanDto, PlanClipDto, ProjectSummary } from '../../types/ipc';
import {
  type TrackAudioState,
  addDynamicTrack,
  validateMultitrackExportCapabilities
} from './multitrackModel';
import { MultitrackClipAudioSection } from './MultitrackClipAudioSection';
import { MultitrackBrollSection } from './MultitrackBrollSection';
import { MultitrackMusicSection } from './MultitrackMusicSection';
import { MultitrackExportReport } from './MultitrackExportReport';

export interface MultitrackPlanningPanelProps {
  plan: EditPlanDto;
  selectedClipId?: string | null;
  projectSummary?: ProjectSummary | null;
  trackStates?: Record<string, TrackAudioState>;
  initialTab?: 'tracks' | 'clip' | 'insert' | 'export';
  onPlanChange: (updatedPlan: EditPlanDto) => void;
  onTrackStatesChange?: (states: Record<string, TrackAudioState>) => void;
  onSelectClip?: (clipId: string | null) => void;
}

export const MultitrackPlanningPanel: React.FC<MultitrackPlanningPanelProps> = ({
  plan,
  selectedClipId,
  projectSummary,
  trackStates = {},
  initialTab,
  onPlanChange,
  onTrackStatesChange,
  onSelectClip
}) => {
  const [activeTab, setActiveTab] = useState<'tracks' | 'clip' | 'insert' | 'export'>(
    initialTab ?? (selectedClipId ? 'clip' : 'tracks')
  );

  const selectedClip = selectedClipId ? plan.clips.find((c) => c.clipId === selectedClipId) ?? null : null;
  const exportReport = validateMultitrackExportCapabilities(plan);

  const handleAddTrack = (kind: 'video' | 'audio') => {
    const { plan: updated } = addDynamicTrack(plan, kind);
    onPlanChange(updated);
  };

  return (
    <div className="bg-[#0b1120] border border-slate-800 rounded-xl overflow-hidden flex flex-col text-slate-200">
      <div className="flex items-center justify-between px-3 py-2 border-b border-slate-800 bg-[#0d1527]">
        <div className="flex items-center gap-1.5 font-bold text-xs text-cyan-400">
          <Layers className="w-3.5 h-3.5" aria-hidden="true" />
          <span>Painel Multifaixa e Áudio (F14.3)</span>
        </div>
        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={() => handleAddTrack('video')}
            className="px-2 py-1 rounded bg-slate-800 hover:bg-slate-700 text-[10px] font-semibold text-slate-300 flex items-center gap-1 transition-colors"
          >
            <Plus className="w-3 h-3 text-cyan-400" aria-hidden="true" />
            <span>+ Vídeo</span>
          </button>
          <button
            type="button"
            onClick={() => handleAddTrack('audio')}
            className="px-2 py-1 rounded bg-slate-800 hover:bg-slate-700 text-[10px] font-semibold text-slate-300 flex items-center gap-1 transition-colors"
          >
            <Plus className="w-3 h-3 text-indigo-400" aria-hidden="true" />
            <span>+ Áudio</span>
          </button>
        </div>
      </div>

      <div className="flex border-b border-slate-800 text-xs bg-slate-950/60 overflow-x-auto">
        <button
          type="button"
          onClick={() => setActiveTab('tracks')}
          className={`px-3 py-2 font-medium border-b-2 whitespace-nowrap transition-colors ${
            activeTab === 'tracks'
              ? 'border-cyan-500 text-cyan-400 bg-slate-900/50'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          Trilhas ({plan.tracks.length})
        </button>
        <button
          type="button"
          onClick={() => setActiveTab('clip')}
          className={`px-3 py-2 font-medium border-b-2 whitespace-nowrap transition-colors flex items-center gap-1 ${
            activeTab === 'clip'
              ? 'border-cyan-500 text-cyan-400 bg-slate-900/50'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <Link2 className="w-3 h-3" aria-hidden="true" />
          <span>Áudio &amp; Vínculo</span>
        </button>
        <button
          type="button"
          onClick={() => setActiveTab('insert')}
          className={`px-3 py-2 font-medium border-b-2 whitespace-nowrap transition-colors flex items-center gap-1 ${
            activeTab === 'insert'
              ? 'border-cyan-500 text-cyan-400 bg-slate-900/50'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <Video className="w-3 h-3 text-cyan-400" aria-hidden="true" />
          <Music className="w-3 h-3 text-indigo-400" aria-hidden="true" />
          <span>B-roll &amp; Música</span>
        </button>
        <button
          type="button"
          onClick={() => setActiveTab('export')}
          className={`px-3 py-2 font-medium border-b-2 whitespace-nowrap transition-colors flex items-center gap-1 ${
            activeTab === 'export'
              ? 'border-cyan-500 text-cyan-400 bg-slate-900/50'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <AlertTriangle className={`w-3 h-3 ${exportReport.isFullyExportable ? 'text-emerald-400' : 'text-amber-400'}`} aria-hidden="true" />
          <span>Exportação</span>
        </button>
      </div>

      <div className="p-3">
        {activeTab === 'tracks' && (
          <div className="space-y-2">
            <span className="text-[11px] text-slate-400 block">
              Estrutura de trilhas editorial. V1 e A1 representam o vídeo e diálogo original vinculados.
            </span>
            <div className="space-y-1.5">
              {plan.tracks.map((t) => {
                const count = plan.clips.filter((c) => c.trackId === t.trackId).length;
                return (
                  <div key={t.trackId} className="flex items-center justify-between p-2 rounded bg-slate-950/70 border border-slate-800 text-xs">
                    <div className="flex items-center gap-2">
                      <span className="font-mono font-bold text-cyan-300">{t.trackId}</span>
                      <span className="text-slate-300">{t.label}</span>
                      <span className="text-[10px] text-slate-500 bg-slate-900 px-1.5 py-0.5 rounded border border-slate-800">
                        {t.kind}
                      </span>
                    </div>
                    <span className="font-mono text-slate-400 text-[11px]">{count} {count === 1 ? 'clipe' : 'clipes'}</span>
                  </div>
                );
              })}
            </div>
          </div>
        )}

        {activeTab === 'clip' && (
          <MultitrackClipAudioSection
            plan={plan}
            selectedClip={selectedClip}
            onPlanChange={onPlanChange}
            onSelectClip={(id) => onSelectClip?.(id)}
          />
        )}

        {activeTab === 'insert' && (
          <div className="space-y-3">
            <MultitrackBrollSection plan={plan} projectSummary={projectSummary} onPlanChange={onPlanChange} />
            <MultitrackMusicSection plan={plan} projectSummary={projectSummary} onPlanChange={onPlanChange} />
          </div>
        )}

        {activeTab === 'export' && (
          <MultitrackExportReport report={exportReport} />
        )}
      </div>
    </div>
  );
};
