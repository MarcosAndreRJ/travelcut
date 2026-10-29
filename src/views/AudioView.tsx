import React, { useState } from 'react';
import {
  Music,
  Volume2,
  VolumeX,
  Sparkles,
  Sliders,
  Play,
  Pause,
  RotateCcw,
  Headphones,
  Mic,
  Wind,
  Radio,
  Zap,
  CheckCircle2,
  ListMusic,
  Download,
  Flame,
  ArrowRight
} from 'lucide-react';
import { AudioTrackConfig, MusicTrack } from '../types';
import { INITIAL_AUDIO_TRACKS, MOCK_MUSIC_LIBRARY } from '../data/mockData';

interface AudioViewProps {
  onNavigateToNLE?: () => void;
  onNavigateToExport?: () => void;
}

export const AudioView: React.FC<AudioViewProps> = ({
  onNavigateToNLE,
  onNavigateToExport,
}) => {
  const [tracks, setTracks] = useState<AudioTrackConfig[]>(INITIAL_AUDIO_TRACKS);
  const [musicTracks, setMusicTracks] = useState<MusicTrack[]>(MOCK_MUSIC_LIBRARY);
  const [selectedMusicId, setSelectedMusicId] = useState<string>(musicTracks[0].id);
  const [isPlayingMusic, setIsPlayingMusic] = useState(false);
  const [activePreset, setActivePreset] = useState<'clarity' | 'cinematic' | 'podcast' | 'vlog'>('vlog');
  const [toastMessage, setToastMessage] = useState<string | null>(null);

  const selectedMusic = musicTracks.find((m) => m.id === selectedMusicId) || musicTracks[0];

  const triggerToast = (msg: string) => {
    setToastMessage(msg);
    setTimeout(() => setToastMessage(null), 3500);
  };

  const updateTrack = (trackId: string, updates: Partial<AudioTrackConfig>) => {
    setTracks(tracks.map((t) => (t.id === trackId ? { ...t, ...updates } : t)));
  };

  const handleApplyPreset = (preset: 'clarity' | 'cinematic' | 'podcast' | 'vlog') => {
    setActivePreset(preset);
    if (preset === 'clarity') {
      updateTrack('track-speech', { speechEnhance: 95, noiseReductionDb: -22, eqMid: 4.0 });
      triggerToast('Preset "Voz Cristalina": Isolamento máximo de fala aplicado na faixa A1.');
    } else if (preset === 'cinematic') {
      updateTrack('track-music', { volumeDb: -6, eqLow: 3.5 });
      updateTrack('track-ambient', { volumeDb: -3 });
      triggerToast('Preset "Cinema Imersivo": Graves da trilha e ambiência valorizados.');
    } else {
      updateTrack('track-speech', { speechEnhance: 85, noiseReductionDb: -18 });
      updateTrack('track-music', { autoDucking: true, duckingAmountDb: -16 });
      triggerToast('Preset "Vlog de Viagem Equilibrado": Auto-Ducking automático e voz nítida.');
    }
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

      {/* Top Banner: Audio Suite Mode & Quick Presets */}
      <div className="h-16 border-b border-studio-border bg-studio-panel px-6 flex items-center justify-between shrink-0">
        <div className="flex items-center space-x-4">
          <div className="flex items-center space-x-2">
            <Headphones className="w-5 h-5 text-indigo-400" />
            <h2 className="text-sm font-bold text-studio-text tracking-wide uppercase">
              Mesa de Mixagem & IA de Áudio
            </h2>
          </div>

          {/* Quick preset buttons */}
          <div className="flex items-center space-x-1.5 bg-studio-card p-1 rounded-xl border border-studio-border text-xs">
            {[
              { id: 'vlog', label: 'Vlog Equilibrado' },
              { id: 'clarity', label: 'Isolamento de Voz Cristalina' },
              { id: 'cinematic', label: 'Cinema Imersivo' },
            ].map((p) => (
              <button
                key={p.id}
                onClick={() => handleApplyPreset(p.id as any)}
                className={`px-3 py-1 rounded-lg font-medium transition-colors ${
                  activePreset === p.id
                    ? 'bg-indigo-600 text-white font-semibold shadow-glow-indigo'
                    : 'text-studio-muted hover:text-white'
                }`}
              >
                {p.label}
              </button>
            ))}
          </div>
        </div>

        {/* Master Output Meter Stats */}
        <div className="flex items-center space-x-5">
          <div className="flex items-center space-x-3 bg-studio-card border border-studio-border px-3.5 py-1.5 rounded-xl font-mono text-xs">
            <span className="text-studio-subtle">Target Loudness:</span>
            <span className="text-emerald-400 font-bold">-14.0 LUFS</span>
            <span className="text-studio-subtle">|</span>
            <span className="text-studio-subtle">True Peak:</span>
            <span className="text-indigo-400 font-bold">-1.2 dBTP</span>
          </div>

          <button
            onClick={() => triggerToast('Cortes da timeline alinhados aos picos rítmicos de 118 BPM!')}
            className="flex items-center space-x-1.5 bg-gradient-to-r from-indigo-600 to-indigo-500 hover:from-indigo-500 hover:to-indigo-400 text-white text-xs px-3.5 py-1.5 rounded-lg font-bold shadow-glow-indigo transition-all"
          >
            <Zap className="w-3.5 h-3.5 text-amber-300" />
            <span>Alinhar Cortes aos Beats da Trilha</span>
          </button>
        </div>
      </div>

      {/* Main Workspace: Tracks Faders + Music Library */}
      <div className="flex-1 flex overflow-hidden">
        {/* Left / Center: Multi-track Faders & DSP */}
        <div className="flex-1 flex flex-col overflow-y-auto p-6 space-y-4">
          <div className="flex items-center justify-between">
            <h3 className="text-xs font-bold uppercase tracking-wider text-studio-muted flex items-center gap-2">
              <Sliders className="w-4 h-4 text-indigo-400" />
              Faixas de Áudio do Projeto (4 Canais Independentes)
            </h3>
            <span className="text-[11px] text-studio-subtle">
              Faders de ganho digital em tempo real com auto-ducking e equalizador paramétrico
            </span>
          </div>

          <div className="space-y-3.5">
            {tracks.map((track) => {
              const getIcon = () => {
                if (track.type === 'speech') return <Mic className="w-4 h-4 text-indigo-400" />;
                if (track.type === 'ambient') return <Wind className="w-4 h-4 text-emerald-400" />;
                if (track.type === 'music') return <Music className="w-4 h-4 text-amber-400" />;
                return <Radio className="w-4 h-4 text-purple-400" />;
              };

              return (
                <div
                  key={track.id}
                  className="bg-studio-panel border border-studio-border hover:border-studio-borderLight rounded-2xl p-4 transition-all"
                >
                  <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-4">
                    {/* Track Info & Mute/Solo */}
                    <div className="flex items-center space-x-3 min-w-[240px]">
                      <div className="p-2 bg-studio-card rounded-xl border border-studio-border">
                        {getIcon()}
                      </div>
                      <div>
                        <h4 className="text-sm font-bold text-studio-text">{track.name}</h4>
                        <div className="flex items-center space-x-2 mt-1">
                          <button
                            onClick={() => updateTrack(track.id, { isMuted: !track.isMuted })}
                            className={`px-2 py-0.5 rounded text-[10px] font-mono font-bold transition-colors ${
                              track.isMuted
                                ? 'bg-rose-500 text-white'
                                : 'bg-studio-card border border-studio-border text-studio-muted hover:text-white'
                            }`}
                          >
                            MUTE
                          </button>
                          <button
                            onClick={() => updateTrack(track.id, { isSolo: !track.isSolo })}
                            className={`px-2 py-0.5 rounded text-[10px] font-mono font-bold transition-colors ${
                              track.isSolo
                                ? 'bg-amber-500 text-black'
                                : 'bg-studio-card border border-studio-border text-studio-muted hover:text-white'
                            }`}
                          >
                            SOLO
                          </button>
                          {track.autoDucking && (
                            <span className="text-[10px] font-mono bg-indigo-500/10 border border-indigo-500/30 text-indigo-300 px-1.5 py-0.5 rounded">
                              Ducking {track.duckingAmountDb}dB
                            </span>
                          )}
                        </div>
                      </div>
                    </div>

                    {/* Volume Fader & Digital Display */}
                    <div className="flex-1 flex items-center space-x-4 max-w-md">
                      <Volume2 className="w-4 h-4 text-studio-subtle shrink-0" />
                      <input
                        type="range"
                        min="-40"
                        max="12"
                        step="0.5"
                        value={track.volumeDb}
                        onChange={(e) => updateTrack(track.id, { volumeDb: parseFloat(e.target.value) })}
                        className="w-full accent-indigo-500 h-2 bg-studio-card rounded-lg cursor-pointer"
                      />
                      <span className="font-mono text-xs font-bold text-studio-text w-16 text-right">
                        {track.volumeDb > 0 ? `+${track.volumeDb.toFixed(1)}` : track.volumeDb.toFixed(1)} dB
                      </span>
                    </div>

                    {/* AI Speech & Noise Treatment Controls */}
                    <div className="flex items-center space-x-4 text-xs">
                      {track.type === 'speech' && (
                        <>
                          <div className="flex flex-col space-y-1">
                            <span className="text-[10px] text-studio-subtle">Clareza IA:</span>
                            <div className="flex items-center space-x-1 font-mono text-indigo-400 font-bold bg-studio-card px-2 py-0.5 rounded border border-studio-border">
                              <Sparkles className="w-3 h-3 text-amber-300" />
                              <span>{track.speechEnhance}%</span>
                            </div>
                          </div>

                          <div className="flex flex-col space-y-1">
                            <span className="text-[10px] text-studio-subtle">Redução Ruído:</span>
                            <div className="font-mono text-emerald-400 font-bold bg-studio-card px-2 py-0.5 rounded border border-studio-border">
                              {track.noiseReductionDb} dB
                            </div>
                          </div>
                        </>
                      )}

                      {/* Equalizer Bands */}
                      <div className="flex items-center space-x-1.5 bg-studio-card border border-studio-border p-1.5 rounded-xl font-mono text-[10px]">
                        <span className="text-studio-subtle px-1">EQ:</span>
                        <span className="text-studio-muted">L: {track.eqLow > 0 ? `+${track.eqLow}` : track.eqLow}</span>
                        <span className="text-studio-muted">M: {track.eqMid > 0 ? `+${track.eqMid}` : track.eqMid}</span>
                        <span className="text-studio-muted">H: {track.eqHigh > 0 ? `+${track.eqHigh}` : track.eqHigh}</span>
                      </div>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        {/* Right Sidebar: Music Library & Beat Marker */}
        <div className="w-80 md:w-96 border-l border-studio-border bg-studio-panel flex flex-col shrink-0">
          <div className="p-4 border-b border-studio-border">
            <div className="flex items-center space-x-2">
              <ListMusic className="w-4 h-4 text-indigo-400" />
              <h3 className="text-xs font-bold uppercase tracking-wider text-studio-text">
                Biblioteca de Trilhas & Beats
              </h3>
            </div>
            <p className="text-xs text-studio-subtle mt-1">
              Trilhas livres de direitos autorais com detecção automática de BPM e compassos
            </p>
          </div>

          <div className="flex-1 overflow-y-auto p-4 space-y-3">
            {musicTracks.map((music) => {
              const isSelected = music.id === selectedMusic.id;
              return (
                <div
                  key={music.id}
                  onClick={() => setSelectedMusicId(music.id)}
                  className={`border rounded-xl p-3 cursor-pointer transition-all ${
                    isSelected
                      ? 'bg-indigo-950/30 border-indigo-500 shadow-glow-indigo'
                      : 'bg-studio-card border-studio-border hover:border-studio-borderLight hover:bg-studio-cardHover'
                  }`}
                >
                  <div className="flex items-center justify-between mb-1.5">
                    <span className="text-xs font-bold text-studio-text truncate max-w-[180px]">
                      {music.title}
                    </span>
                    <span className="font-mono text-xs font-bold bg-amber-500/10 border border-amber-500/30 text-amber-400 px-2 py-0.5 rounded">
                      {music.bpm} BPM
                    </span>
                  </div>

                  <p className="text-[11px] text-studio-subtle">{music.artist}</p>

                  <div className="flex items-center justify-between mt-2 pt-2 border-t border-studio-border/50 text-[10px] text-studio-subtle">
                    <span>{music.genre}</span>
                    <span className="font-mono">{music.durationFormatted}</span>
                  </div>

                  {isSelected && (
                    <div className="mt-2.5 flex items-center justify-between pt-2 border-t border-indigo-500/30">
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          setIsPlayingMusic(!isPlayingMusic);
                        }}
                        className="flex items-center space-x-1 text-xs text-indigo-300 hover:text-white font-semibold"
                      >
                        {isPlayingMusic ? <Pause className="w-3.5 h-3.5" /> : <Play className="w-3.5 h-3.5" />}
                        <span>{isPlayingMusic ? 'Pausar Prévia' : 'Ouvir Prévia'}</span>
                      </button>

                      <span className="text-[10px] text-emerald-400 font-semibold flex items-center gap-1">
                        <CheckCircle2 className="w-3 h-3" />
                        Trilha Selecionada
                      </span>
                    </div>
                  )}
                </div>
              );
            })}
          </div>

          {/* Footer Actions */}
          <div className="p-4 border-t border-studio-border bg-studio-panel space-y-2">
            {onNavigateToNLE && (
              <button
                onClick={onNavigateToNLE}
                className="w-full flex items-center justify-center space-x-2 bg-gradient-to-r from-indigo-600 to-indigo-500 hover:from-indigo-500 hover:to-indigo-400 text-white text-xs py-2.5 rounded-xl font-bold shadow-glow-indigo transition-all"
              >
                <span>Ver Montagem na Timeline</span>
                <ArrowRight className="w-3.5 h-3.5" />
              </button>
            )}

            {onNavigateToExport && (
              <button
                onClick={onNavigateToExport}
                className="w-full flex items-center justify-center space-x-2 bg-studio-card hover:bg-studio-cardHover border border-studio-border text-xs py-2 rounded-lg text-studio-text font-semibold transition-colors"
              >
                <Download className="w-3.5 h-3.5 text-emerald-400" />
                <span>Prosseguir para Exportação</span>
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
