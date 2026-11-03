import React, { useState } from 'react';
import {
  Layers,
  Film,
  FastForward,
  Maximize2,
  Volume2,
  Clock,
  Play,
  Pause,
  ZoomIn,
  ZoomOut,
  Sliders
} from 'lucide-react';
import { ProjectSummary } from '../types/ipc';

interface TimelineMontageViewProps {
  project: ProjectSummary | null;
}

interface MockClip {
  id: string;
  name: string;
  startSec: number;
  durationSec: number;
  track: 'V1' | 'A1' | 'A2';
  type: 'dialogue' | 'broll' | 'sped_up' | 'trimmed_cut';
  speed: number;
  hasZoomMarker?: boolean;
}

export const TimelineMontageView: React.FC<TimelineMontageViewProps> = ({ project }) => {
  const [isPlaying, setIsPlaying] = useState(false);
  const [playheadSec, setPlayheadSec] = useState(18.5);
  const [zoomLevel, setZoomLevel] = useState(1); // 1x, 1.5x, 2x

  const mockClips: MockClip[] = [
    {
      id: 'clip-01',
      name: '01_Chegada_Aeroporto.mp4',
      startSec: 0,
      durationSec: 14.2,
      track: 'V1',
      type: 'dialogue',
      speed: 1.0,
      hasZoomMarker: false
    },
    {
      id: 'clip-02',
      name: '01_Chegada_Aeroporto.mp4 (Parte 2)',
      startSec: 14.2,
      durationSec: 27.3,
      track: 'V1',
      type: 'dialogue',
      speed: 1.0,
      hasZoomMarker: true // 1.05x subtle zoom após corte de silêncio
    },
    {
      id: 'clip-03',
      name: '04_Trilha_Montanha_Broll.mp4',
      startSec: 41.5,
      durationSec: 8.25,
      track: 'V1',
      type: 'sped_up',
      speed: 2.0,
      hasZoomMarker: false
    },
    {
      id: 'clip-04',
      name: '05_Entrevista_Guia.mp4',
      startSec: 49.75,
      durationSec: 22.0,
      track: 'V1',
      type: 'dialogue',
      speed: 1.0,
      hasZoomMarker: true
    },
    {
      id: 'clip-05',
      name: '07_Diario_De_Bordo.mp4',
      startSec: 71.75,
      durationSec: 35.5,
      track: 'V1',
      type: 'dialogue',
      speed: 1.0,
      hasZoomMarker: false
    }
  ];

  const totalDurationSec = 120; // 2 minutos para visualização

  const formatTimecode = (sec: number) => {
    const mins = Math.floor(sec / 60);
    const secs = Math.floor(sec % 60);
    const frames = Math.floor((sec % 1) * 30);
    return `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}:${frames.toString().padStart(2, '0')}`;
  };

  return (
    <div className="flex-1 overflow-y-auto p-8 space-y-6 max-w-7xl mx-auto w-full">
      {/* Top Controls & Status */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-studio-900 border border-studio-800 p-6 rounded-xl">
        <div>
          <h1 className="text-xl font-bold text-slate-100 flex items-center space-x-2">
            <Layers className="w-5 h-5 text-amber-400" />
            <span>Montagem & Timeline Virtual</span>
          </h1>
          <p className="text-xs text-slate-400 mt-1">
            Visualização precisa de cortes aplicados, velocidades de B-Roll e marcadores de enquadramento sutil (1.05x).
          </p>
        </div>

        <div className="flex items-center space-x-3">
          <div className="flex items-center space-x-1 bg-studio-950 border border-studio-800 px-3 py-1.5 rounded-lg text-xs font-mono text-amber-400">
            <Clock className="w-3.5 h-3.5 text-slate-400 mr-1" />
            <span>{formatTimecode(playheadSec)}</span>
          </div>

          <button
            onClick={() => setIsPlaying(!isPlaying)}
            className="p-2 bg-studio-800 hover:bg-studio-700 text-slate-200 rounded-lg transition-colors border border-studio-700"
          >
            {isPlaying ? <Pause className="w-4 h-4" /> : <Play className="w-4 h-4" />}
          </button>

          <div className="flex items-center space-x-1 bg-studio-950 border border-studio-800 rounded-lg p-1">
            <button
              onClick={() => setZoomLevel(Math.max(0.75, zoomLevel - 0.25))}
              className="p-1 text-slate-400 hover:text-slate-200"
              title="Reduzir Zoom"
            >
              <ZoomOut className="w-3.5 h-3.5" />
            </button>
            <span className="text-[11px] text-slate-400 px-1 font-mono">{zoomLevel}x</span>
            <button
              onClick={() => setZoomLevel(Math.min(2.5, zoomLevel + 0.25))}
              className="p-1 text-slate-400 hover:text-slate-200"
              title="Aumentar Zoom"
            >
              <ZoomIn className="w-3.5 h-3.5" />
            </button>
          </div>
        </div>
      </div>

      {/* Multitrack Timeline Area */}
      <div className="bg-studio-900 border border-studio-800 rounded-xl overflow-hidden shadow-inner">
        {/* Timeline Header Ruler */}
        <div className="h-8 bg-studio-950 border-b border-studio-800 flex items-center px-24 select-none relative">
          {[0, 15, 30, 45, 60, 75, 90, 105, 120].map((sec) => (
            <div
              key={sec}
              className="absolute text-[10px] text-slate-500 font-mono border-l border-studio-800 pl-1 h-4 flex items-center"
              style={{ left: `${(sec / totalDurationSec) * 100}%` }}
            >
              {formatTimecode(sec)}
            </div>
          ))}

          {/* Playhead Marker */}
          <div
            className="absolute top-0 bottom-0 w-0.5 bg-amber-400 z-20 pointer-events-none"
            style={{ left: `${(playheadSec / totalDurationSec) * 100}%` }}
          >
            <div className="w-2.5 h-2.5 bg-amber-400 rotate-45 -translate-x-[4px] -translate-y-1 shadow-sm" />
          </div>
        </div>

        {/* Tracks Container */}
        <div className="p-4 space-y-3 relative">
          {/* Track V1 (Video) */}
          <div className="flex items-center space-x-3">
            <div className="w-16 text-right text-xs font-mono font-bold text-slate-400 shrink-0">
              V1 Vídeo
            </div>
            <div className="flex-1 h-14 bg-studio-950 border border-studio-800/80 rounded-lg relative overflow-hidden flex items-center p-1">
              {mockClips.map((clip) => {
                const leftPct = (clip.startSec / totalDurationSec) * 100;
                const widthPct = (clip.durationSec / totalDurationSec) * 100;

                return (
                  <div
                    key={clip.id}
                    onClick={() => setPlayheadSec(clip.startSec)}
                    style={{ left: `${leftPct}%`, width: `${widthPct}%` }}
                    className={`absolute h-11 rounded border px-2.5 py-1 text-left flex flex-col justify-between transition-all cursor-pointer ${
                      clip.type === 'sped_up'
                        ? 'bg-blue-950/70 border-blue-600/60 hover:border-blue-400'
                        : 'bg-studio-850 border-studio-700 hover:border-amber-500'
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <span className="text-[11px] font-semibold text-slate-200 truncate pr-1">
                        {clip.name}
                      </span>
                      <div className="flex items-center space-x-1 shrink-0">
                        {clip.speed > 1 && (
                          <span className="bg-blue-500/20 text-blue-300 border border-blue-500/30 text-[9px] px-1 rounded flex items-center space-x-0.5 font-mono">
                            <FastForward className="w-2.5 h-2.5" />
                            <span>{clip.speed}x</span>
                          </span>
                        )}
                        {clip.hasZoomMarker && (
                          <span
                            className="bg-purple-500/20 text-purple-300 border border-purple-500/30 text-[9px] px-1 rounded flex items-center space-x-0.5 font-mono"
                            title="Salto sutil de escala 1.05x para suavizar corte"
                          >
                            <Maximize2 className="w-2.5 h-2.5" />
                            <span>1.05x</span>
                          </span>
                        )}
                      </div>
                    </div>
                    <div className="text-[9px] font-mono text-slate-400">
                      {clip.durationSec.toFixed(1)}s
                    </div>
                  </div>
                );
              })}
            </div>
          </div>

          {/* Track A1 (Áudio Diálogo) */}
          <div className="flex items-center space-x-3">
            <div className="w-16 text-right text-xs font-mono font-bold text-slate-400 shrink-0">
              A1 Diálogo
            </div>
            <div className="flex-1 h-12 bg-studio-950 border border-studio-800/80 rounded-lg relative overflow-hidden flex items-center p-1">
              {mockClips.map((clip) => {
                const leftPct = (clip.startSec / totalDurationSec) * 100;
                const widthPct = (clip.durationSec / totalDurationSec) * 100;

                return (
                  <div
                    key={'a1_' + clip.id}
                    style={{ left: `${leftPct}%`, width: `${widthPct}%` }}
                    className="absolute h-9 rounded bg-emerald-950/40 border border-emerald-800/50 px-2 flex items-center justify-between text-[10px] text-emerald-400 font-mono"
                  >
                    <div className="flex items-center space-x-1 truncate">
                      <Volume2 className="w-3 h-3" />
                      <span className="truncate">{clip.name}</span>
                    </div>
                    <span className="text-[9px] opacity-75">{clip.durationSec.toFixed(1)}s</span>
                  </div>
                );
              })}
            </div>
          </div>

          {/* Track A2 (Áudio Ambiente / Trilha) */}
          <div className="flex items-center space-x-3">
            <div className="w-16 text-right text-xs font-mono font-bold text-slate-400 shrink-0">
              A2 Trilha
            </div>
            <div className="flex-1 h-10 bg-studio-950 border border-studio-800/80 rounded-lg relative overflow-hidden flex items-center p-1">
              <div
                style={{ left: '0%', width: '90%' }}
                className="absolute h-7 rounded bg-indigo-950/40 border border-indigo-800/50 px-2 flex items-center space-x-1 text-[10px] text-indigo-400 font-mono"
              >
                <span>Trilha_Fundo_Aventura_Patagonia.wav (Fade In/Out)</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Clip Sequence Table / Inspector */}
      <div className="bg-studio-900 border border-studio-800 rounded-xl p-6">
        <h2 className="text-base font-semibold text-slate-100 flex items-center space-x-2 mb-4">
          <Film className="w-4 h-4 text-amber-400" />
          <span>Sequência Estruturada de Clipes do EditPlan (R1)</span>
        </h2>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="border-b border-studio-800 text-slate-400 font-mono uppercase tracking-wider">
                <th className="py-2.5 px-3">#</th>
                <th className="py-2.5 px-3">Nome do Arquivo</th>
                <th className="py-2.5 px-3">Trilha</th>
                <th className="py-2.5 px-3">Início Timeline</th>
                <th className="py-2.5 px-3">Duração</th>
                <th className="py-2.5 px-3">Aceleração</th>
                <th className="py-2.5 px-3">Enquadramento</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-studio-800/60 font-mono">
              {mockClips.map((c, idx) => (
                <tr key={c.id} className="hover:bg-studio-950/40 transition-colors">
                  <td className="py-2.5 px-3 text-slate-500">{idx + 1}</td>
                  <td className="py-2.5 px-3 font-semibold text-slate-200">{c.name}</td>
                  <td className="py-2.5 px-3 text-slate-400">{c.track}</td>
                  <td className="py-2.5 px-3 text-slate-300">{formatTimecode(c.startSec)}</td>
                  <td className="py-2.5 px-3 text-slate-300">{c.durationSec.toFixed(1)}s</td>
                  <td className="py-2.5 px-3">
                    {c.speed > 1 ? (
                      <span className="bg-blue-500/20 text-blue-400 border border-blue-500/30 px-1.5 py-0.5 rounded text-[10px]">
                        {c.speed}x
                      </span>
                    ) : (
                      <span className="text-slate-500">1.0x</span>
                    )}
                  </td>
                  <td className="py-2.5 px-3">
                    {c.hasZoomMarker ? (
                      <span className="bg-purple-500/20 text-purple-400 border border-purple-500/30 px-1.5 py-0.5 rounded text-[10px]">
                        Zoom 1.05x
                      </span>
                    ) : (
                      <span className="text-slate-500">Normal</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};
