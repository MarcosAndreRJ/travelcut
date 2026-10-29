import React, { useState } from 'react';
import {
  MessageSquare,
  Search,
  Sparkles,
  Scissors,
  Check,
  X,
  Volume2,
  Film,
  Info,
  Sliders,
  Camera,
  MapPin,
  Calendar,
  Layers
} from 'lucide-react';
import { LibraryClip, InspectorTab, TranscriptLine } from '../types';

interface TranscriptInspectorProps {
  clip: LibraryClip;
  currentTimeSec: number;
  onSeek: (timeSec: number) => void;
  onAcceptCut?: (cutLine: TranscriptLine) => void;
  onRejectCut?: (cutLine: TranscriptLine) => void;
}

export const TranscriptInspector: React.FC<TranscriptInspectorProps> = ({
  clip,
  currentTimeSec,
  onSeek,
  onAcceptCut,
  onRejectCut
}) => {
  const [activeTab, setActiveTab] = useState<InspectorTab>('transcription');
  const [searchQuery, setSearchQuery] = useState('');
  const [language, setLanguage] = useState<'pt' | 'en' | 'es'>('pt');
  const [acceptedCuts, setAcceptedCuts] = useState<Record<string, boolean>>({});

  const handleToggleCut = (lineId: string, line: TranscriptLine) => {
    const isNowAccepted = !acceptedCuts[lineId];
    setAcceptedCuts((prev) => ({ ...prev, [lineId]: isNowAccepted }));
    if (isNowAccepted) {
      onAcceptCut?.(line);
    } else {
      onRejectCut?.(line);
    }
  };

  const filteredTranscript = clip.transcript.filter((line) => {
    if (!searchQuery.trim()) return true;
    return line.text.toLowerCase().includes(searchQuery.toLowerCase());
  });

  return (
    <div className="bg-slate-900/90 border border-slate-800/90 rounded-xl overflow-hidden shadow-sm flex flex-col flex-1">
      {/* Top 4-Tab Navigation */}
      <div className="flex items-center border-b border-slate-800/80 bg-slate-950/70 px-2 overflow-x-auto scrollbar-none">
        <button
          onClick={() => setActiveTab('transcription')}
          className={`flex items-center space-x-1.5 px-3 py-2.5 text-xs font-semibold border-b-2 transition-all whitespace-nowrap ${
            activeTab === 'transcription'
              ? 'border-blue-500 text-blue-400 bg-blue-500/5'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <MessageSquare className="w-3.5 h-3.5" />
          <span>Transcrição</span>
        </button>

        <button
          onClick={() => setActiveTab('analysis')}
          className={`flex items-center space-x-1.5 px-3 py-2.5 text-xs font-semibold border-b-2 transition-all whitespace-nowrap ${
            activeTab === 'analysis'
              ? 'border-blue-500 text-blue-400 bg-blue-500/5'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <Sparkles className="w-3.5 h-3.5" />
          <span>Análise</span>
        </button>

        <button
          onClick={() => setActiveTab('metadata')}
          className={`flex items-center space-x-1.5 px-3 py-2.5 text-xs font-semibold border-b-2 transition-all whitespace-nowrap ${
            activeTab === 'metadata'
              ? 'border-blue-500 text-blue-400 bg-blue-500/5'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <Info className="w-3.5 h-3.5" />
          <span>Metadados</span>
        </button>

        <button
          onClick={() => setActiveTab('thumbnails')}
          className={`flex items-center space-x-1.5 px-3 py-2.5 text-xs font-semibold border-b-2 transition-all whitespace-nowrap ${
            activeTab === 'thumbnails'
              ? 'border-blue-500 text-blue-400 bg-blue-500/5'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <Film className="w-3.5 h-3.5" />
          <span>Miniaturas</span>
        </button>
      </div>

      {/* Tab 1: Transcrição (Whisper large-v3) */}
      {activeTab === 'transcription' && (
        <div className="flex flex-col flex-1 p-3 space-y-3">
          {/* Subheader: Search & Language selector */}
          <div className="flex items-center justify-between gap-2">
            <div className="relative flex-1">
              <Search className="w-3.5 h-3.5 absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Buscar na transcrição..."
                className="w-full bg-slate-950 border border-slate-800 rounded-lg pl-7 pr-3 py-1 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-blue-500"
              />
              {searchQuery && (
                <button
                  onClick={() => setSearchQuery('')}
                  className="absolute right-2 top-1/2 -translate-y-1/2 text-slate-500 hover:text-white text-xs"
                >
                  ✕
                </button>
              )}
            </div>

            <select
              value={language}
              onChange={(e) => setLanguage(e.target.value as any)}
              className="bg-slate-950 border border-slate-800 rounded-lg px-2.5 py-1 text-xs text-slate-300 focus:outline-none focus:border-blue-500"
            >
              <option value="pt">Português (BR)</option>
              <option value="en">Inglês (EN)</option>
              <option value="es">Espanhol (ES)</option>
            </select>
          </div>

          {/* Transcript Lines List */}
          <div className="flex-1 overflow-y-auto space-y-2 max-h-[360px] pr-1">
            {filteredTranscript.map((line) => {
              const isCut = line.isCutCandidate;
              const isAccepted = acceptedCuts[line.id];
              const isCurrent =
                currentTimeSec >= line.seconds &&
                currentTimeSec < line.seconds + 6;

              return (
                <div
                  key={line.id}
                  className={`p-2.5 rounded-lg border text-xs transition-all ${
                    isCurrent
                      ? 'bg-blue-600/15 border-blue-500/50 shadow-sm'
                      : isCut
                      ? isAccepted
                        ? 'bg-red-950/20 border-red-500/40 opacity-70'
                        : 'bg-red-500/10 border-red-500/30'
                      : line.isAudioEvent
                      ? 'bg-slate-950/40 border-slate-800/60'
                      : 'bg-slate-950/60 border-slate-800/80 hover:border-slate-700'
                  }`}
                >
                  <div className="flex items-start justify-between gap-2">
                    {/* Timestamp button (click to seek) */}
                    <button
                      onClick={() => onSeek(line.seconds)}
                      className={`font-mono font-semibold text-[11px] px-1.5 py-0.5 rounded transition-colors shrink-0 ${
                        isCurrent
                          ? 'bg-blue-500 text-white font-bold'
                          : 'bg-slate-800/80 text-blue-400 hover:bg-blue-500/20'
                      }`}
                      title="Pular para este segundo"
                    >
                      {line.timecode}
                    </button>

                    {/* Sentence body */}
                    <div className="flex-1 min-w-0">
                      {line.speaker && (
                        <span className="font-semibold text-slate-300 mr-1.5">
                          {line.speaker}:
                        </span>
                      )}

                      {line.isAudioEvent ? (
                        <span className="italic text-slate-400 flex items-center space-x-1">
                          <Volume2 className="w-3 h-3 text-cyan-400 inline shrink-0" />
                          <span>{line.text}</span>
                        </span>
                      ) : isCut ? (
                        <span
                          className={`font-medium ${
                            isAccepted
                              ? 'line-through text-red-400/80'
                              : 'text-red-300 font-semibold'
                          }`}
                        >
                          {line.text}
                        </span>
                      ) : (
                        <span className="text-slate-200">{line.text}</span>
                      )}

                      {/* Cut Candidate Reason and Accept CTA */}
                      {isCut && (
                        <div className="mt-1.5 pt-1.5 border-t border-red-500/20 flex items-center justify-between">
                          <span className="text-[10px] text-red-400 font-medium flex items-center space-x-1">
                            <Scissors className="w-3 h-3" />
                            <span>{line.cutReason}</span>
                          </span>

                          <button
                            onClick={() => handleToggleCut(line.id, line)}
                            className={`px-2 py-0.5 rounded text-[10px] font-bold flex items-center space-x-1 transition-all ${
                              isAccepted
                                ? 'bg-red-600 text-white'
                                : 'bg-red-500/20 hover:bg-red-500/30 text-red-300 border border-red-500/40'
                            }`}
                          >
                            {isAccepted ? (
                              <>
                                <Check className="w-3 h-3" />
                                <span>Corte Aplicado</span>
                              </>
                            ) : (
                              <>
                                <Scissors className="w-3 h-3" />
                                <span>Cortar Hesitação</span>
                              </>
                            )}
                          </button>
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* Tab 2: Análise IA */}
      {activeTab === 'analysis' && (
        <div className="p-3 space-y-3 flex-1 overflow-y-auto text-xs">
          <div className="bg-slate-950/60 border border-slate-800 rounded-lg p-3 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-slate-400">Score de Qualidade IA</span>
              <span className="text-emerald-400 font-bold">{clip.aiScore}% (Excelente)</span>
            </div>
            <div className="w-full bg-slate-800 h-1.5 rounded-full overflow-hidden">
              <div
                className="bg-emerald-500 h-full rounded-full"
                style={{ width: `${clip.aiScore}%` }}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-2">
            <div className="bg-slate-950/60 border border-slate-800 p-2.5 rounded-lg flex flex-col">
              <span className="text-slate-400 text-[10px]">Estabilização</span>
              <span className="text-white font-semibold mt-0.5">Gimbal Suave</span>
              <span className="text-slate-500 text-[10px]">Gyro Sync Ok</span>
            </div>

            <div className="bg-slate-950/60 border border-slate-800 p-2.5 rounded-lg flex flex-col">
              <span className="text-slate-400 text-[10px]">Iluminação</span>
              <span className="text-white font-semibold mt-0.5">Luz Natural 5600K</span>
              <span className="text-slate-500 text-[10px]">Sem estouro de realces</span>
            </div>

            <div className="bg-slate-950/60 border border-slate-800 p-2.5 rounded-lg flex flex-col">
              <span className="text-slate-400 text-[10px]">Rostos Identificados</span>
              <span className="text-white font-semibold mt-0.5">1 Rosto em Foco</span>
              <span className="text-slate-500 text-[10px]">Rastreamento facial ativo</span>
            </div>

            <div className="bg-slate-950/60 border border-slate-800 p-2.5 rounded-lg flex flex-col">
              <span className="text-slate-400 text-[10px]">LUT Recomendada</span>
              <span className="text-white font-semibold mt-0.5 truncate">Rec.709 Neutral</span>
              <span className="text-slate-500 text-[10px]">DaVinci Color Managed</span>
            </div>
          </div>
        </div>
      )}

      {/* Tab 3: Metadados */}
      {activeTab === 'metadata' && (
        <div className="p-3 space-y-2 flex-1 overflow-y-auto text-xs divide-y divide-slate-800/60">
          <div className="flex justify-between py-1.5">
            <span className="text-slate-400">Nome do Arquivo</span>
            <span className="text-white font-mono">{clip.filename}</span>
          </div>
          <div className="flex justify-between py-1.5">
            <span className="text-slate-400">Resolução</span>
            <span className="text-white font-mono">{clip.resolution}</span>
          </div>
          <div className="flex justify-between py-1.5">
            <span className="text-slate-400">Câmera</span>
            <span className="text-white">{clip.camera}</span>
          </div>
          <div className="flex justify-between py-1.5">
            <span className="text-slate-400">Localização</span>
            <span className="text-white">{clip.location}</span>
          </div>
          <div className="flex justify-between py-1.5">
            <span className="text-slate-400">Data de Gravação</span>
            <span className="text-white">{clip.date}</span>
          </div>
          <div className="flex justify-between py-1.5">
            <span className="text-slate-400">Codec & Container</span>
            <span className="text-white font-mono">H.265 / HEVC 10-bit</span>
          </div>
          <div className="flex justify-between py-1.5">
            <span className="text-slate-400">Áudio</span>
            <span className="text-white font-mono">48kHz 24-bit Stereo</span>
          </div>
        </div>
      )}

      {/* Tab 4: Miniaturas */}
      {activeTab === 'thumbnails' && (
        <div className="p-3 flex-1 overflow-y-auto">
          <div className="grid grid-cols-3 gap-2">
            {[0, 15, 30, 45, 60, 75, 90, 105, 120].map((sec) => (
              <div
                key={sec}
                onClick={() => onSeek(sec)}
                className="group relative rounded-lg overflow-hidden border border-slate-800 hover:border-blue-500 cursor-pointer aspect-video bg-slate-950"
              >
                <img
                  src={clip.thumbnail}
                  alt={`Thumbnail ${sec}s`}
                  className="w-full h-full object-cover group-hover:scale-105 transition-transform"
                />
                <span className="absolute bottom-1 right-1 text-[9px] font-mono bg-slate-950/80 px-1 rounded text-slate-300">
                  {Math.floor(sec / 60)}:{(sec % 60).toString().padStart(2, '0')}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
};
