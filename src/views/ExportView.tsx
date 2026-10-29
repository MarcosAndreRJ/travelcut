import React, { useState } from 'react';
import {
  Download,
  CheckCircle2,
  Clock,
  HardDrive,
  Cpu,
  Layers,
  FileCode,
  Share2,
  FolderOpen,
  Play,
  Pause,
  AlertCircle,
  Sparkles,
  Youtube,
  Instagram,
  Video,
  Film,
  Sliders,
  Check
} from 'lucide-react';
import { ExportPreset, ExportJob } from '../types';
import { EXPORT_PRESETS, INITIAL_EXPORT_QUEUE } from '../data/mockData';

interface ExportViewProps {
  onOpenFolder?: () => void;
}

export const ExportView: React.FC<ExportViewProps> = ({ onOpenFolder }) => {
  const [presets, setPresets] = useState<ExportPreset[]>(EXPORT_PRESETS);
  const [selectedPresetId, setSelectedPresetId] = useState<string>(presets[0].id);
  const [queue, setQueue] = useState<ExportJob[]>(INITIAL_EXPORT_QUEUE);
  const [burnSubtitles, setBurnSubtitles] = useState<'burned' | 'srt' | 'none'>('burned');
  const [toastMessage, setToastMessage] = useState<string | null>(null);

  const selectedPreset = presets.find((p) => p.id === selectedPresetId) || presets[0];

  const triggerToast = (msg: string) => {
    setToastMessage(msg);
    setTimeout(() => setToastMessage(null), 3500);
  };

  const handleStartRender = () => {
    const newJob: ExportJob = {
      id: `job-${Date.now()}`,
      presetName: `${selectedPreset.name} (${selectedPreset.resolution})`,
      resolution: selectedPreset.resolution,
      codec: selectedPreset.codec,
      progress: 0,
      status: 'rendering',
      etaSec: 35,
      outputFilename: `Japao_Viagem_${selectedPreset.platform}_${Date.now().toString().slice(-4)}.mp4`,
      filesizeMb: selectedPreset.estimatedSizeMb,
    };
    setQueue([newJob, ...queue]);
    triggerToast(`Renderização iniciada: ${newJob.outputFilename}`);
  };

  const handleExportNleXml = (software: string) => {
    triggerToast(`Arquivo de timeline gerado para ${software}! Pronto para pós-produção.`);
  };

  return (
    <div className="flex-1 flex flex-col overflow-hidden bg-studio-bg select-none">
      {/* Toast */}
      {toastMessage && (
        <div className="absolute top-4 right-4 z-50 bg-indigo-600/95 text-white px-4 py-2.5 rounded-xl shadow-panel border border-indigo-400/40 flex items-center space-x-2 text-xs font-semibold backdrop-blur-md animate-fade-in">
          <Sparkles className="w-4 h-4 text-amber-300" />
          <span>{toastMessage}</span>
        </div>
      )}

      {/* Top Banner */}
      <div className="h-16 border-b border-studio-border bg-studio-panel px-6 flex items-center justify-between shrink-0">
        <div className="flex items-center space-x-3">
          <Download className="w-5 h-5 text-indigo-400" />
          <div>
            <h2 className="text-sm font-bold text-studio-text uppercase tracking-wide">
              Central de Exportação & Entrega
            </h2>
            <p className="text-xs text-studio-subtle">
              Renderização acelerada por GPU (NVENC/CUDA) e empacotamento multiplataforma
            </p>
          </div>
        </div>

        {/* Action button */}
        <div className="flex items-center space-x-3">
          <button
            onClick={handleStartRender}
            className="flex items-center space-x-2 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white text-xs px-4 py-2 rounded-xl font-bold shadow-glow-emerald transition-all"
          >
            <Play className="w-3.5 h-3.5 fill-current" />
            <span>Renderizar Selecionado ({selectedPreset.platform})</span>
          </button>
        </div>
      </div>

      {/* Main Container */}
      <div className="flex-1 flex overflow-hidden">
        {/* Left Column: Preset Cards & Settings */}
        <div className="flex-1 flex flex-col overflow-y-auto p-6 space-y-6">
          {/* Preset Cards Selection */}
          <div className="space-y-3">
            <h3 className="text-xs font-bold uppercase tracking-wider text-studio-muted flex items-center gap-2">
              <Share2 className="w-4 h-4 text-indigo-400" />
              Predefinições Otimizadas por Destino
            </h3>

            <div className="grid grid-cols-1 md:grid-cols-2 2xl:grid-cols-4 gap-4">
              {presets.map((preset) => {
                const isSelected = preset.id === selectedPreset.id;
                const getPlatformIcon = () => {
                  if (preset.platform === 'YouTube') return <Youtube className="w-5 h-5 text-rose-500" />;
                  if (preset.platform === 'Instagram') return <Instagram className="w-5 h-5 text-pink-500" />;
                  if (preset.platform === 'TikTok') return <Video className="w-5 h-5 text-cyan-400" />;
                  return <Film className="w-5 h-5 text-indigo-400" />;
                };

                return (
                  <div
                    key={preset.id}
                    onClick={() => setSelectedPresetId(preset.id)}
                    className={`border rounded-2xl p-4 cursor-pointer transition-all ${
                      isSelected
                        ? 'bg-indigo-950/30 border-indigo-500 shadow-glow-indigo'
                        : 'bg-studio-panel border-studio-border hover:border-studio-borderLight hover:bg-studio-card/80'
                    }`}
                  >
                    <div className="flex items-center justify-between mb-3">
                      <div className="p-2 bg-studio-card rounded-xl border border-studio-border">
                        {getPlatformIcon()}
                      </div>
                      <span className="font-mono text-xs font-bold bg-studio-card border border-studio-border px-2 py-0.5 rounded text-studio-text">
                        {preset.ratio}
                      </span>
                    </div>

                    <h4 className="text-sm font-bold text-studio-text mb-1">{preset.name}</h4>
                    <p className="font-mono text-xs text-indigo-400 font-semibold">{preset.resolution}</p>

                    <div className="mt-4 pt-3 border-t border-studio-border/70 flex items-center justify-between text-[11px] text-studio-subtle font-mono">
                      <span>{preset.codec}</span>
                      <span className="text-emerald-400 font-bold">~{preset.estimatedSizeMb} MB</span>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>

          {/* Preset Customization Specs */}
          <div className="bg-studio-panel border border-studio-border rounded-2xl p-5 space-y-4">
            <h4 className="text-xs font-bold uppercase tracking-wider text-studio-text flex items-center gap-2">
              <Sliders className="w-4 h-4 text-indigo-400" />
              Parâmetros de Codificação: {selectedPreset.name}
            </h4>

            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 text-xs">
              <div className="space-y-1.5">
                <label className="text-studio-muted font-semibold">Codec de Vídeo</label>
                <div className="bg-studio-card border border-studio-border px-3 py-2 rounded-lg font-mono text-studio-text">
                  {selectedPreset.codec}
                </div>
              </div>

              <div className="space-y-1.5">
                <label className="text-studio-muted font-semibold">Bitrate de Vídeo Alvo</label>
                <div className="bg-studio-card border border-studio-border px-3 py-2 rounded-lg font-mono text-studio-text">
                  {selectedPreset.bitrateMbps} Mbps (VBR 2-Pass)
                </div>
              </div>

              <div className="space-y-1.5">
                <label className="text-studio-muted font-semibold">Aceleração de Render</label>
                <div className="bg-studio-card border border-studio-border px-3 py-2 rounded-lg font-mono text-emerald-400 font-semibold flex items-center gap-1.5">
                  <Cpu className="w-3.5 h-3.5" />
                  <span>NVIDIA NVENC (CUDA)</span>
                </div>
              </div>
            </div>

            {/* Subtitles Option */}
            <div className="pt-3 border-t border-studio-border space-y-2">
              <label className="text-xs text-studio-muted font-semibold">Legendas Automáticas da Transcrição:</label>
              <div className="flex flex-wrap gap-2 text-xs">
                {[
                  { id: 'burned', label: 'Queimar Legendas no Vídeo (Hardsub Estilizado)' },
                  { id: 'srt', label: 'Exportar Arquivo .SRT Separado' },
                  { id: 'none', label: 'Sem Legendas' },
                ].map((opt) => (
                  <button
                    key={opt.id}
                    onClick={() => setBurnSubtitles(opt.id as any)}
                    className={`px-3 py-1.5 rounded-lg border transition-colors ${
                      burnSubtitles === opt.id
                        ? 'bg-indigo-600 border-indigo-500 text-white font-semibold'
                        : 'bg-studio-card border-studio-border text-studio-muted hover:text-white'
                    }`}
                  >
                    {opt.label}
                  </button>
                ))}
              </div>
            </div>
          </div>

          {/* NLE Timeline Export (XML / FCPXML / EDL) */}
          <div className="bg-studio-panel border border-studio-border rounded-2xl p-5 space-y-3">
            <div className="flex items-center justify-between">
              <div>
                <h4 className="text-xs font-bold uppercase tracking-wider text-studio-text flex items-center gap-2">
                  <FileCode className="w-4 h-4 text-indigo-400" />
                  Exportar Linha do Tempo para Softwares Profissionais (NLE)
                </h4>
                <p className="text-xs text-studio-subtle mt-0.5">
                  Transfira todos os cortes, marcadores, áudios e transcrições sem re-renderizar
                </p>
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 pt-2">
              <button
                onClick={() => handleExportNleXml('DaVinci Resolve')}
                className="flex items-center justify-center space-x-2 bg-studio-card hover:bg-studio-cardHover border border-studio-border text-xs py-2.5 rounded-xl text-studio-text font-semibold transition-colors"
              >
                <span>DaVinci Resolve (.xml)</span>
              </button>

              <button
                onClick={() => handleExportNleXml('Adobe Premiere Pro')}
                className="flex items-center justify-center space-x-2 bg-studio-card hover:bg-studio-cardHover border border-studio-border text-xs py-2.5 rounded-xl text-studio-text font-semibold transition-colors"
              >
                <span>Premiere Pro (.xml)</span>
              </button>

              <button
                onClick={() => handleExportNleXml('Final Cut Pro')}
                className="flex items-center justify-center space-x-2 bg-studio-card hover:bg-studio-cardHover border border-studio-border text-xs py-2.5 rounded-xl text-studio-text font-semibold transition-colors"
              >
                <span>Final Cut Pro (.fcpxml)</span>
              </button>
            </div>
          </div>
        </div>

        {/* Right Column: Live Render Queue */}
        <div className="w-80 md:w-96 border-l border-studio-border bg-studio-panel flex flex-col shrink-0">
          <div className="p-4 border-b border-studio-border flex items-center justify-between">
            <div className="flex items-center space-x-2">
              <Clock className="w-4 h-4 text-indigo-400" />
              <h3 className="text-xs font-bold uppercase tracking-wider text-studio-text">
                Fila de Renderização
              </h3>
            </div>
            <span className="font-mono text-xs font-bold bg-studio-card border border-studio-border px-2 py-0.5 rounded text-indigo-300">
              {queue.length} tarefas
            </span>
          </div>

          {/* Queue Jobs List */}
          <div className="flex-1 overflow-y-auto p-4 space-y-3">
            {queue.map((job) => (
              <div
                key={job.id}
                className="bg-studio-card border border-studio-border rounded-xl p-3 space-y-2.5"
              >
                <div className="flex items-start justify-between">
                  <div className="min-w-0">
                    <h5 className="text-xs font-bold text-studio-text truncate">{job.outputFilename}</h5>
                    <p className="text-[11px] text-studio-subtle font-mono mt-0.5">{job.presetName}</p>
                  </div>
                  {job.status === 'completed' ? (
                    <span className="text-[10px] font-bold text-emerald-400 flex items-center gap-1 bg-emerald-500/10 px-1.5 py-0.5 rounded">
                      <CheckCircle2 className="w-3 h-3" />
                      Pronto
                    </span>
                  ) : job.status === 'rendering' ? (
                    <span className="text-[10px] font-bold text-amber-400 flex items-center gap-1 bg-amber-500/10 px-1.5 py-0.5 rounded animate-pulse">
                      Renderizando
                    </span>
                  ) : (
                    <span className="text-[10px] font-bold text-studio-subtle bg-studio-panel px-1.5 py-0.5 rounded">
                      Na Fila
                    </span>
                  )}
                </div>

                {/* Progress Bar */}
                <div className="space-y-1">
                  <div className="h-2 bg-studio-bg rounded-full overflow-hidden">
                    <div
                      style={{ width: `${job.progress}%` }}
                      className={`h-full transition-all duration-300 ${
                        job.status === 'completed'
                          ? 'bg-emerald-500'
                          : 'bg-gradient-to-r from-indigo-500 to-emerald-400'
                      }`}
                    />
                  </div>
                  <div className="flex items-center justify-between text-[10px] font-mono text-studio-subtle">
                    <span>{job.progress}%</span>
                    <span>{job.status === 'rendering' ? `ETA: ${job.etaSec}s` : `${job.filesizeMb} MB`}</span>
                  </div>
                </div>
              </div>
            ))}
          </div>

          {/* Open Folder Footer */}
          <div className="p-4 border-t border-studio-border bg-studio-panel space-y-2">
            <button
              onClick={() => triggerToast('Pasta do projeto aberta no Explorer: D:\\TravelCut_Studio\\Exports\\')}
              className="w-full flex items-center justify-center space-x-2 bg-studio-card hover:bg-studio-cardHover border border-studio-border text-xs py-2 rounded-lg text-studio-text font-semibold transition-colors"
            >
              <FolderOpen className="w-3.5 h-3.5 text-indigo-400" />
              <span>Abrir Pasta de Exportações</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};
