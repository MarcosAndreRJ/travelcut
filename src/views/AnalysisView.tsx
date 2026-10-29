import React, { useState } from 'react';
import {
  Search,
  Sparkles,
  Check,
  X,
  Play,
  Pause,
  RotateCcw,
  Volume2,
  VolumeX,
  Sliders,
  Scissors,
  Wand2,
  Clock,
  MapPin,
  Camera,
  Layers,
  ChevronRight,
  Eye,
  FileText,
  Smile,
  Zap,
  ArrowRight
} from 'lucide-react';
import { MediaClip, ClipCategory, SuggestedCut } from '../types';

interface AnalysisViewProps {
  clips: MediaClip[];
  selectedClip: MediaClip;
  onSelectClip: (clip: MediaClip) => void;
  onAcceptCut: (clipId: string, cut: SuggestedCut) => void;
  onNavigateToTimeline?: () => void;
  onNavigateToStoryboard?: () => void;
}

export const AnalysisView: React.FC<AnalysisViewProps> = ({
  clips,
  selectedClip,
  onSelectClip,
  onAcceptCut,
  onNavigateToTimeline,
  onNavigateToStoryboard,
}) => {
  const [selectedCategory, setSelectedCategory] = useState<ClipCategory>('Todas');
  const [searchQuery, setSearchQuery] = useState('');
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTimeSec, setCurrentTimeSec] = useState(4.2);
  const [isMuted, setIsMuted] = useState(false);
  const [playbackSpeed, setPlaybackSpeed] = useState<number>(1);
  const [transcriptSearch, setTranscriptSearch] = useState('');
  const [showNotification, setShowNotification] = useState<string | null>(null);

  // Filter clips
  const filteredClips = clips.filter((clip) => {
    const matchesCategory = selectedCategory === 'Todas' || clip.category === selectedCategory;
    const matchesSearch =
      clip.title.toLowerCase().includes(searchQuery.toLowerCase()) ||
      clip.location.toLowerCase().includes(searchQuery.toLowerCase()) ||
      clip.filename.toLowerCase().includes(searchQuery.toLowerCase());
    return matchesCategory && matchesSearch;
  });

  const triggerToast = (msg: string) => {
    setShowNotification(msg);
    setTimeout(() => setShowNotification(null), 3000);
  };

  // Format seconds to HH:MM:SS
  const formatTime = (secs: number) => {
    const m = Math.floor(secs / 60);
    const s = Math.floor(secs % 60);
    const ms = Math.floor((secs % 1) * 30); // 30fps frames
    return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}:${ms.toString().padStart(2, '0')}`;
  };

  const handleSeek = (e: React.ChangeEvent<HTMLInputElement>) => {
    setCurrentTimeSec(parseFloat(e.target.value));
  };

  const handleJumpToTime = (time: number) => {
    setCurrentTimeSec(time);
    setIsPlaying(true);
  };

  return (
    <div className="flex-1 flex overflow-hidden bg-studio-bg relative">
      {/* Toast Notification */}
      {showNotification && (
        <div className="absolute top-4 right-4 z-50 bg-indigo-600/95 text-white px-4 py-2.5 rounded-xl shadow-panel border border-indigo-400/40 flex items-center space-x-2 text-xs font-semibold backdrop-blur-md animate-fade-in">
          <Sparkles className="w-4 h-4 text-amber-300" />
          <span>{showNotification}</span>
        </div>
      )}

      {/* LEFT COLUMN: Media Library & AI Ingestion */}
      <div className="w-80 md:w-96 border-r border-studio-border bg-studio-panel flex flex-col shrink-0">
        {/* Search & Header */}
        <div className="p-3 border-b border-studio-border space-y-2.5">
          <div className="flex items-center justify-between">
            <div className="flex items-center space-x-2">
              <Sparkles className="w-4 h-4 text-indigo-400" />
              <h2 className="text-xs font-bold uppercase tracking-wider text-studio-text">
                Clipes Importados
              </h2>
            </div>
            <span className="text-[11px] font-mono font-bold bg-studio-card border border-studio-border px-2 py-0.5 rounded text-indigo-300">
              {filteredClips.length} itens
            </span>
          </div>

          <div className="relative">
            <Search className="w-3.5 h-3.5 absolute left-3 top-1/2 -translate-y-1/2 text-studio-subtle" />
            <input
              type="text"
              placeholder="Buscar por local, tag ou nome..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="w-full bg-studio-card border border-studio-border rounded-lg pl-8 pr-3 py-1.5 text-xs text-studio-text placeholder-studio-subtle focus:outline-none focus:border-indigo-500 transition-colors"
            />
          </div>

          {/* Categories */}
          <div className="flex items-center gap-1.5 overflow-x-auto pb-1 text-[11px]">
            {(['Todas', 'Paisagem', 'Monumentos', 'Gastronomia', 'Transporte'] as ClipCategory[]).map((cat) => (
              <button
                key={cat}
                onClick={() => setSelectedCategory(cat)}
                className={`px-2.5 py-1 rounded-md font-medium whitespace-nowrap transition-colors ${
                  selectedCategory === cat
                    ? 'bg-indigo-600 text-white'
                    : 'bg-studio-card text-studio-muted hover:text-studio-text hover:bg-studio-cardHover'
                }`}
              >
                {cat}
              </button>
            ))}
          </div>
        </div>

        {/* Clip Cards List */}
        <div className="flex-1 overflow-y-auto p-3 space-y-2.5">
          {filteredClips.map((clip) => {
            const isSelected = clip.id === selectedClip.id;
            return (
              <div
                key={clip.id}
                onClick={() => {
                  onSelectClip(clip);
                  setCurrentTimeSec(0);
                }}
                className={`group relative rounded-xl border p-2 cursor-pointer transition-all duration-150 ${
                  isSelected
                    ? 'bg-indigo-950/40 border-indigo-500/80 shadow-glow-indigo'
                    : 'bg-studio-card border-studio-border hover:border-studio-borderLight hover:bg-studio-cardHover'
                }`}
              >
                <div className="flex space-x-3">
                  {/* Thumbnail container */}
                  <div className="relative w-28 h-20 rounded-lg overflow-hidden shrink-0 bg-black/60 border border-white/5">
                    <img
                      src={clip.thumbnail}
                      alt={clip.title}
                      className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-200"
                    />
                    <div className="absolute inset-0 bg-gradient-to-t from-black/80 via-transparent to-transparent"></div>

                    {/* Duration Badge */}
                    <span className="absolute bottom-1 right-1 font-mono text-[10px] font-bold bg-black/80 px-1 py-0.5 rounded text-white">
                      {clip.durationFormatted}
                    </span>

                    {/* Resolution Tag */}
                    <span className="absolute top-1 left-1 text-[9px] font-bold bg-amber-500/90 text-black px-1 rounded uppercase font-mono">
                      {clip.resolution.split(' ')[0]}
                    </span>
                  </div>

                  {/* Info */}
                  <div className="flex-1 min-w-0 flex flex-col justify-between py-0.5">
                    <div>
                      <div className="flex items-center justify-between">
                        <span className="text-[10px] font-bold uppercase tracking-wider text-indigo-400">
                          {clip.category}
                        </span>
                        {/* AI Score */}
                        <div className="flex items-center space-x-1 bg-emerald-500/10 border border-emerald-500/30 px-1.5 py-0.5 rounded text-[10px] text-emerald-400 font-mono font-bold">
                          <Sparkles className="w-2.5 h-2.5" />
                          <span>{clip.aiScore}%</span>
                        </div>
                      </div>
                      <h3 className="text-xs font-bold text-studio-text truncate mt-0.5 group-hover:text-white">
                        {clip.title}
                      </h3>
                      <p className="text-[11px] text-studio-subtle truncate flex items-center gap-1 mt-0.5">
                        <MapPin className="w-3 h-3 text-studio-subtle" />
                        {clip.location}
                      </p>
                    </div>

                    <div className="flex items-center justify-between pt-1 border-t border-studio-border/50 text-[10px] text-studio-subtle font-mono">
                      <span>{clip.camera.split(' ')[1] || 'Sony FX3'}</span>
                      <span className="text-emerald-400 font-semibold">{clip.stability}</span>
                    </div>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* CENTER COLUMN: Interactive Preview Player & AI Cut Decisioning */}
      <div className="flex-1 flex flex-col overflow-hidden bg-studio-bg border-r border-studio-border">
        {/* Player Header Bar */}
        <div className="h-11 border-b border-studio-border px-4 flex items-center justify-between bg-studio-panel/70">
          <div className="flex items-center space-x-3">
            <span className="text-xs font-bold text-studio-text truncate max-w-sm">
              {selectedClip.title}
            </span>
            <span className="bg-indigo-500/10 border border-indigo-500/30 text-indigo-300 text-[10px] px-2 py-0.5 rounded font-mono font-semibold">
              {selectedClip.resolution}
            </span>
            <span className="text-studio-subtle text-xs">|</span>
            <span className="text-studio-subtle text-xs font-mono">{selectedClip.filename}</span>
          </div>

          <div className="flex items-center space-x-2">
            <span className="text-[11px] text-studio-muted font-medium">Proporção:</span>
            <span className="bg-studio-card border border-studio-border text-studio-text text-xs px-2 py-0.5 rounded font-mono font-bold">
              16:9 Cinema
            </span>
          </div>
        </div>

        {/* Video Canvas Preview */}
        <div className="flex-1 relative bg-black flex items-center justify-center overflow-hidden group">
          <img
            src={selectedClip.thumbnail}
            alt="Preview"
            className="w-full h-full object-contain select-none"
          />

          {/* Video Overlays (Safe margins, timecode, metadata) */}
          <div className="absolute inset-4 pointer-events-none border border-white/10 rounded flex flex-col justify-between p-3">
            <div className="flex items-center justify-between">
              <span className="bg-black/70 backdrop-blur-md border border-white/10 text-white font-mono text-xs px-2.5 py-1 rounded">
                REC • 4K UHD 60P LOG
              </span>
              <span className="bg-black/70 backdrop-blur-md border border-white/10 text-emerald-400 font-mono text-xs px-2.5 py-1 rounded flex items-center gap-1.5">
                <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse"></span>
                IA VISION ATIVA
              </span>
            </div>

            {/* Subtitle preview if active */}
            <div className="flex justify-center mb-2">
              <span className="bg-black/85 border border-indigo-500/40 text-amber-200 font-bold px-4 py-1.5 rounded-lg text-sm shadow-panel backdrop-blur-md">
                "{selectedClip.transcription[0]?.text || 'Japão 2024: A Jornada Começa Aqui'}"
              </span>
            </div>
          </div>

          {/* Giant Center Play/Pause on hover */}
          <button
            onClick={() => setIsPlaying(!isPlaying)}
            className="absolute inset-0 m-auto w-16 h-16 rounded-full bg-indigo-600/90 text-white flex items-center justify-center shadow-glow-indigo opacity-0 group-hover:opacity-100 hover:scale-110 transition-all duration-200"
          >
            {isPlaying ? <Pause className="w-7 h-7" /> : <Play className="w-7 h-7 ml-1" />}
          </button>
        </div>

        {/* Player Controls & Scrubber */}
        <div className="bg-studio-panel border-t border-studio-border p-3 space-y-2">
          {/* Time Scrubber Slider */}
          <div className="space-y-1">
            <div className="flex items-center justify-between text-xs font-mono">
              <span className="text-indigo-400 font-bold">{formatTime(currentTimeSec)}</span>
              <span className="text-studio-subtle">{selectedClip.durationFormatted}</span>
            </div>
            <div className="relative flex items-center">
              <input
                type="range"
                min="0"
                max={selectedClip.durationSec}
                step="0.1"
                value={currentTimeSec}
                onChange={handleSeek}
                className="w-full accent-indigo-500 h-1.5 bg-studio-card rounded-lg appearance-none cursor-pointer"
              />
              {/* Markers for Suggested Cuts */}
              {selectedClip.suggestedCuts.map((cut) => {
                const leftPercent = (cut.startSec / selectedClip.durationSec) * 100;
                const widthPercent = ((cut.endSec - cut.startSec) / selectedClip.durationSec) * 100;
                return (
                  <div
                    key={cut.id}
                    style={{ left: `${leftPercent}%`, width: `${widthPercent}%` }}
                    className="absolute h-1.5 bg-emerald-400/80 rounded pointer-events-none"
                    title={`Corte Sugerido: ${cut.durationFormatted}`}
                  />
                );
              })}
            </div>
          </div>

          {/* Transport Buttons */}
          <div className="flex items-center justify-between pt-1">
            <div className="flex items-center space-x-2">
              <button
                onClick={() => setCurrentTimeSec(0)}
                className="p-1.5 hover:bg-studio-card rounded text-studio-muted hover:text-white transition-colors"
                title="Voltar ao Início"
              >
                <RotateCcw className="w-4 h-4" />
              </button>

              <button
                onClick={() => setIsPlaying(!isPlaying)}
                className="p-2 bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg transition-colors shadow-glow-indigo"
                title="Reproduzir / Pausar (Espaço)"
              >
                {isPlaying ? <Pause className="w-4 h-4" /> : <Play className="w-4 h-4 ml-0.5" />}
              </button>

              <button
                onClick={() => setIsMuted(!isMuted)}
                className="p-1.5 hover:bg-studio-card rounded text-studio-muted hover:text-white transition-colors"
              >
                {isMuted ? <VolumeX className="w-4 h-4 text-rose-400" /> : <Volume2 className="w-4 h-4" />}
              </button>
            </div>

            {/* Speed selection */}
            <div className="flex items-center space-x-1.5 bg-studio-card border border-studio-border px-2 py-0.5 rounded-lg text-xs font-mono">
              <span className="text-studio-subtle">Vel:</span>
              {[0.5, 1, 2].map((spd) => (
                <button
                  key={spd}
                  onClick={() => setPlaybackSpeed(spd)}
                  className={`px-1.5 py-0.5 rounded text-[11px] ${
                    playbackSpeed === spd ? 'bg-indigo-600 text-white font-bold' : 'text-studio-muted hover:text-white'
                  }`}
                >
                  {spd}x
                </button>
              ))}
            </div>
          </div>
        </div>

        {/* BOTTOM AI CUT DECISION CARD */}
        <div className="bg-studio-panel/90 border-t border-studio-border p-3.5 space-y-3">
          <div className="flex items-center justify-between">
            <div className="flex items-center space-x-2">
              <div className="w-2 h-2 rounded-full bg-emerald-400 animate-ping"></div>
              <h4 className="text-xs font-bold text-studio-text flex items-center gap-1.5 uppercase tracking-wider">
                <Wand2 className="w-3.5 h-3.5 text-indigo-400" />
                Decisão Inteligente de Cortes (IA)
              </h4>
            </div>
            <span className="text-[11px] text-studio-muted font-medium">
              Baseado em: Emoção + Estabilidade + Regra dos Terços
            </span>
          </div>

          {/* Best Suggested Cut Box */}
          {selectedClip.suggestedCuts.length > 0 ? (
            selectedClip.suggestedCuts.map((cut) => (
              <div
                key={cut.id}
                className="bg-studio-card border border-studio-border rounded-xl p-3 space-y-2.5 hover:border-indigo-500/50 transition-all"
              >
                <div className="flex items-start justify-between">
                  <div>
                    <div className="flex items-center space-x-2">
                      <span className="bg-emerald-500/10 border border-emerald-500/40 text-emerald-400 text-[10px] px-2 py-0.5 rounded font-mono font-bold">
                        SCORE {cut.score}% RECOMENDADO
                      </span>
                      <span className="text-xs font-mono font-semibold text-studio-text">
                        Intervalo: {cut.timecodeStart} ➔ {cut.timecodeEnd} ({cut.durationFormatted})
                      </span>
                    </div>
                    <p className="text-xs text-studio-muted mt-1.5 leading-relaxed">
                      {cut.reason}
                    </p>
                  </div>
                </div>

                {/* Tags and Action Buttons */}
                <div className="flex items-center justify-between pt-1">
                  <div className="flex items-center gap-1.5">
                    {cut.tags.map((tag) => (
                      <span
                        key={tag}
                        className="bg-studio-panel border border-studio-border text-[10px] text-studio-subtle px-2 py-0.5 rounded font-medium"
                      >
                        #{tag}
                      </span>
                    ))}
                  </div>

                  <div className="flex items-center space-x-2">
                    <button
                      onClick={() => handleJumpToTime(cut.startSec)}
                      className="flex items-center space-x-1 bg-studio-panel hover:bg-studio-cardHover border border-studio-border text-xs px-2.5 py-1.5 rounded-lg text-studio-text transition-colors"
                      title="Pré-visualizar intervalo do corte"
                    >
                      <Play className="w-3 h-3 text-indigo-400" />
                      <span>Ver Trecho</span>
                    </button>

                    <button
                      onClick={() => {
                        onAcceptCut(selectedClip.id, cut);
                        triggerToast(`Corte "${selectedClip.title}" adicionado à montagem!`);
                      }}
                      className="flex items-center space-x-1.5 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white text-xs px-3 py-1.5 rounded-lg font-bold shadow-glow-emerald transition-all"
                    >
                      <Check className="w-3.5 h-3.5" />
                      <span>Aceitar Corte</span>
                    </button>
                  </div>
                </div>
              </div>
            ))
          ) : (
            <div className="p-4 text-center text-xs text-studio-subtle bg-studio-card rounded-lg">
              Nenhuma sugestão automática pendente para este clipe.
            </div>
          )}
        </div>
      </div>

      {/* RIGHT COLUMN: Whisper AI Transcription & Keyword Highlights */}
      <div className="w-80 md:w-96 border-l border-studio-border bg-studio-panel flex flex-col shrink-0">
        {/* Transcription Header */}
        <div className="p-3 border-b border-studio-border space-y-2">
          <div className="flex items-center justify-between">
            <div className="flex items-center space-x-2">
              <FileText className="w-4 h-4 text-indigo-400" />
              <h3 className="text-xs font-bold uppercase tracking-wider text-studio-text">
                Transcrição Whisper IA
              </h3>
            </div>
            <span className="bg-purple-500/10 border border-purple-500/30 text-purple-400 text-[10px] px-2 py-0.5 rounded font-mono font-bold">
              PT-BR (99.4%)
            </span>
          </div>

          <div className="relative">
            <Search className="w-3.5 h-3.5 absolute left-3 top-1/2 -translate-y-1/2 text-studio-subtle" />
            <input
              type="text"
              placeholder="Buscar fala ou palavra-chave..."
              value={transcriptSearch}
              onChange={(e) => setTranscriptSearch(e.target.value)}
              className="w-full bg-studio-card border border-studio-border rounded-lg pl-8 pr-3 py-1 text-xs text-studio-text placeholder-studio-subtle focus:outline-none focus:border-indigo-500 transition-colors"
            />
          </div>
        </div>

        {/* Transcript Segments List */}
        <div className="flex-1 overflow-y-auto p-3 space-y-3">
          {selectedClip.transcription.length > 0 ? (
            selectedClip.transcription.map((seg) => (
              <div
                key={seg.id}
                onClick={() => handleJumpToTime(seg.start)}
                className="bg-studio-card hover:bg-studio-cardHover border border-studio-border hover:border-indigo-500/60 rounded-xl p-3 cursor-pointer transition-all group"
              >
                <div className="flex items-center justify-between text-[11px] mb-1.5">
                  <span className="font-bold text-indigo-400 flex items-center gap-1">
                    <Smile className="w-3 h-3 text-amber-400" />
                    {seg.speaker}
                  </span>
                  <span className="font-mono text-studio-subtle group-hover:text-studio-text">
                    {seg.timecodeStart} ➔ {seg.timecodeEnd}
                  </span>
                </div>

                <p className="text-xs text-studio-text font-medium leading-relaxed">
                  {seg.text}
                </p>

                {/* Highlight badges */}
                {seg.highlightWords && seg.highlightWords.length > 0 && (
                  <div className="flex flex-wrap gap-1 mt-2 pt-2 border-t border-studio-border/50">
                    {seg.highlightWords.map((word) => (
                      <span
                        key={word}
                        className="bg-amber-400/10 text-amber-300 border border-amber-400/20 text-[10px] px-1.5 py-0.5 rounded font-mono"
                      >
                        {word}
                      </span>
                    ))}
                  </div>
                )}
              </div>
            ))
          ) : (
            <div className="p-6 text-center text-xs text-studio-subtle space-y-2">
              <p>Nenhuma fala detectada neste clipe (apenas áudio ambiente).</p>
            </div>
          )}
        </div>

        {/* Transcription Actions */}
        <div className="p-3 border-t border-studio-border bg-studio-panel space-y-2">
          <button
            onClick={() => triggerToast('Legendas dinâmicas geradas com estilos temáticos de viagem!')}
            className="w-full flex items-center justify-center space-x-2 bg-studio-card hover:bg-studio-cardHover border border-studio-border text-xs py-2 rounded-lg text-studio-text font-semibold transition-colors"
          >
            <Sparkles className="w-3.5 h-3.5 text-indigo-400" />
            <span>Gerar Legendas Animadas (Karaokê)</span>
          </button>

          <button
            onClick={() => triggerToast('2 pausas longas e silêncios removidos automaticamente!')}
            className="w-full flex items-center justify-center space-x-2 bg-studio-card hover:bg-studio-cardHover border border-studio-border text-xs py-2 rounded-lg text-studio-text font-semibold transition-colors"
          >
            <Scissors className="w-3.5 h-3.5 text-rose-400" />
            <span>Limpar Silêncios & Hesitações</span>
          </button>

          {/* Quick Flow to Storyboard */}
          {onNavigateToStoryboard && (
            <button
              onClick={onNavigateToStoryboard}
              className="w-full flex items-center justify-center space-x-2 bg-gradient-to-r from-indigo-600 to-indigo-500 text-white text-xs py-2 rounded-lg font-bold shadow-glow-indigo transition-all mt-1"
            >
              <span>Ir para Roteiro & Storyboard</span>
              <ArrowRight className="w-3.5 h-3.5" />
            </button>
          )}
        </div>
      </div>
    </div>
  );
};
