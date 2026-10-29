import React, { useState } from 'react';
import {
  Layers,
  Sparkles,
  Clock,
  MapPin,
  Music,
  Plus,
  Trash2,
  ChevronRight,
  Sliders,
  MoveHorizontal,
  Flame,
  Volume2,
  ArrowRight,
  CheckCircle,
  HelpCircle,
  Wand2,
  Tv
} from 'lucide-react';
import { StoryboardScene, MediaClip } from '../types';

interface NarrativeViewProps {
  storyboard: StoryboardScene[];
  clips: MediaClip[];
  onUpdateStoryboard: (newStoryboard: StoryboardScene[]) => void;
  onNavigateToNLE?: () => void;
  onNavigateToAudio?: () => void;
}

export const NarrativeView: React.FC<NarrativeViewProps> = ({
  storyboard,
  clips,
  onUpdateStoryboard,
  onNavigateToNLE,
  onNavigateToAudio,
}) => {
  const [selectedTemplate, setSelectedTemplate] = useState<'vlog' | 'reels' | 'hero' | 'doc'>('vlog');
  const [targetDurationTotal, setTargetDurationTotal] = useState<number>(60);
  const [selectedSceneId, setSelectedSceneId] = useState<string>(storyboard[0]?.id || 'scene-1');
  const [aiPromptInput, setAiPromptInput] = useState('');
  const [aiMessage, setAiMessage] = useState<string | null>(null);

  // Calculate actual total duration
  const actualTotalSeconds = storyboard.reduce((acc, scene) => acc + scene.actualDurationSec, 0);

  const selectedScene = storyboard.find((s) => s.id === selectedSceneId) || storyboard[0];

  const handleApplyAiOptimization = (type: 'retention' | 'music' | 'reorder') => {
    if (type === 'retention') {
      setAiMessage('Otimização aplicada: Cena 1 reduzida em 2s para corte acelerado de gancho viral. Duração equilibrada!');
    } else if (type === 'music') {
      setAiMessage('Batidas sincronizadas: Todas as transições foram alinhadas para os compassos de 118 BPM da trilha!');
    } else {
      setAiMessage('Ordem cronológica reorganizada com base nos metadados GPS das câmeras Sony FX3!');
    }
    setTimeout(() => setAiMessage(null), 4000);
  };

  const handleAddScene = () => {
    const newScene: StoryboardScene = {
      id: `scene-${Date.now()}`,
      order: storyboard.length + 1,
      title: 'Nova Cena de Destaque',
      location: 'Kyoto, Japão',
      vibe: 'Dinâmica & Cultural',
      targetDurationSec: 10,
      actualDurationSec: 10,
      assignedClipIds: [clips[0]?.id || 'clip-1'],
      musicMood: 'Harmonia suave e contemplativa',
      soundEffect: 'Ambiente natural e passos',
      narrativeGoal: 'Expandir a experiência imersiva e a conexão com a cultura local.'
    };
    onUpdateStoryboard([...storyboard, newScene]);
    setSelectedSceneId(newScene.id);
  };

  const handleDeleteScene = (sceneId: string, e: React.MouseEvent) => {
    e.stopPropagation();
    if (storyboard.length <= 1) return;
    const filtered = storyboard.filter((s) => s.id !== sceneId);
    onUpdateStoryboard(filtered);
    if (selectedSceneId === sceneId) {
      setSelectedSceneId(filtered[0].id);
    }
  };

  return (
    <div className="flex-1 flex flex-col overflow-hidden bg-studio-bg select-none">
      {/* Top Banner: Story Template & Duration Targeter */}
      <div className="h-16 border-b border-studio-border bg-studio-panel px-6 flex items-center justify-between shrink-0">
        <div className="flex items-center space-x-4">
          <div className="flex items-center space-x-2">
            <Layers className="w-5 h-5 text-indigo-400" />
            <h2 className="text-sm font-bold text-studio-text tracking-wide uppercase">
              Planejamento Narrativo & Estrutura
            </h2>
          </div>

          {/* Template pills */}
          <div className="flex items-center space-x-1.5 bg-studio-card p-1 rounded-xl border border-studio-border text-xs">
            {[
              { id: 'vlog', label: 'Vlog Cinemático' },
              { id: 'reels', label: 'Reels / TikTok (60s)' },
              { id: 'hero', label: 'Jornada do Herói' },
              { id: 'doc', label: 'Documentário Imersivo' },
            ].map((tmpl) => (
              <button
                key={tmpl.id}
                onClick={() => setSelectedTemplate(tmpl.id as any)}
                className={`px-3 py-1 rounded-lg font-medium transition-colors ${
                  selectedTemplate === tmpl.id
                    ? 'bg-indigo-600 text-white font-semibold shadow-glow-indigo'
                    : 'text-studio-muted hover:text-white'
                }`}
              >
                {tmpl.label}
              </button>
            ))}
          </div>
        </div>

        {/* Duration Progress Gauge */}
        <div className="flex items-center space-x-6">
          <div className="flex items-center space-x-3 bg-studio-card border border-studio-border px-3.5 py-1.5 rounded-xl">
            <Clock className="w-4 h-4 text-indigo-400" />
            <div className="text-xs font-mono">
              <span className="text-studio-subtle mr-1.5">Meta:</span>
              <span className="text-studio-text font-bold">{targetDurationTotal}s</span>
              <span className="text-studio-subtle mx-2">|</span>
              <span className="text-studio-subtle mr-1.5">Atual:</span>
              <span className={`font-bold ${actualTotalSeconds > targetDurationTotal ? 'text-amber-400' : 'text-emerald-400'}`}>
                {actualTotalSeconds.toFixed(1)}s
              </span>
            </div>
          </div>

          {/* Target Slider */}
          <div className="hidden xl:flex items-center space-x-2">
            <span className="text-xs text-studio-muted font-medium">Ajustar Meta:</span>
            <input
              type="range"
              min="30"
              max="180"
              step="5"
              value={targetDurationTotal}
              onChange={(e) => setTargetDurationTotal(parseInt(e.target.value))}
              className="w-24 accent-indigo-500 h-1.5 bg-studio-card rounded cursor-pointer"
            />
          </div>

          <button
            onClick={handleAddScene}
            className="flex items-center space-x-1.5 bg-indigo-600 hover:bg-indigo-500 text-white text-xs px-3 py-1.5 rounded-lg font-bold shadow-glow-indigo transition-all"
          >
            <Plus className="w-3.5 h-3.5" />
            <span>Adicionar Cena</span>
          </button>
        </div>
      </div>

      {/* Main Content Area */}
      <div className="flex-1 flex overflow-hidden">
        {/* Left / Center: Storyboard Sequence Grid */}
        <div className="flex-1 flex flex-col overflow-y-auto p-6 space-y-4">
          {/* AI Banner Message */}
          {aiMessage && (
            <div className="bg-indigo-600/20 border border-indigo-500/50 rounded-xl p-3 flex items-center space-x-3 text-xs text-indigo-200">
              <Sparkles className="w-4 h-4 text-amber-400 shrink-0" />
              <span>{aiMessage}</span>
            </div>
          )}

          <div className="flex items-center justify-between">
            <h3 className="text-xs font-bold uppercase tracking-wider text-studio-muted flex items-center gap-2">
              <MoveHorizontal className="w-4 h-4 text-indigo-400" />
              Sequência de Cenas do Vídeo ({storyboard.length} Cenas Ordenadas)
            </h3>
            <span className="text-[11px] text-studio-subtle">
              Arraste para reordenar ou clique para inspecionar os clipes atribuídos
            </span>
          </div>

          {/* Cards Grid */}
          <div className="grid grid-cols-1 md:grid-cols-2 2xl:grid-cols-3 gap-4">
            {storyboard.map((scene) => {
              const isSelected = scene.id === selectedScene.id;
              const assignedClip = clips.find((c) => scene.assignedClipIds.includes(c.id)) || clips[0];

              return (
                <div
                  key={scene.id}
                  onClick={() => setSelectedSceneId(scene.id)}
                  className={`bg-studio-panel border rounded-2xl p-4 cursor-pointer transition-all duration-200 relative group flex flex-col justify-between ${
                    isSelected
                      ? 'border-indigo-500 shadow-glow-indigo bg-indigo-950/20'
                      : 'border-studio-border hover:border-studio-borderLight hover:bg-studio-card/80'
                  }`}
                >
                  {/* Top Bar: Scene # & Duration */}
                  <div>
                    <div className="flex items-center justify-between mb-2">
                      <div className="flex items-center space-x-2">
                        <span className="w-6 h-6 rounded-full bg-indigo-600 text-white font-mono font-bold text-xs flex items-center justify-center">
                          {scene.order}
                        </span>
                        <span className="bg-studio-card border border-studio-border text-studio-muted text-[10px] font-mono px-2 py-0.5 rounded">
                          {scene.location}
                        </span>
                      </div>

                      <div className="flex items-center space-x-1.5">
                        <span className="font-mono text-xs font-bold text-indigo-300">
                          {scene.actualDurationSec.toFixed(1)}s
                        </span>
                        {storyboard.length > 1 && (
                          <button
                            onClick={(e) => handleDeleteScene(scene.id, e)}
                            className="p-1 hover:bg-rose-500/20 text-studio-subtle hover:text-rose-400 rounded transition-colors opacity-0 group-hover:opacity-100"
                            title="Remover cena"
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </button>
                        )}
                      </div>
                    </div>

                    <h4 className="text-sm font-bold text-studio-text group-hover:text-white mb-1">
                      {scene.title}
                    </h4>

                    {/* Thumbnail preview */}
                    <div className="relative h-32 rounded-xl overflow-hidden my-2.5 bg-black/60 border border-white/5">
                      <img
                        src={assignedClip?.thumbnail}
                        alt={scene.title}
                        className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                      />
                      <div className="absolute inset-0 bg-gradient-to-t from-black/80 via-transparent to-transparent"></div>
                      <div className="absolute bottom-2 left-2 right-2 flex items-center justify-between text-[11px] font-mono text-white">
                        <span className="bg-black/80 px-2 py-0.5 rounded text-[10px]">
                          {assignedClip?.title || 'Clipe Vinculado'}
                        </span>
                        <span className="text-emerald-400 font-bold">
                          {assignedClip?.resolution || '4K 60fps'}
                        </span>
                      </div>
                    </div>

                    <p className="text-xs text-studio-muted line-clamp-2 leading-relaxed">
                      {scene.narrativeGoal}
                    </p>
                  </div>

                  {/* Scene Metadata Footer */}
                  <div className="pt-3 mt-3 border-t border-studio-border/70 flex items-center justify-between text-[11px] text-studio-subtle">
                    <span className="flex items-center gap-1 text-amber-400/90 font-medium truncate max-w-[150px]">
                      <Flame className="w-3 h-3 text-amber-400 shrink-0" />
                      {scene.vibe}
                    </span>
                    <span className="flex items-center gap-1 text-indigo-300 font-mono">
                      <Music className="w-3 h-3 text-indigo-400 shrink-0" />
                      {scene.musicMood.split(' ')[0]}...
                    </span>
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        {/* Right Sidebar: AI Narrative Assistant & Scene Inspector */}
        <div className="w-80 md:w-96 border-l border-studio-border bg-studio-panel flex flex-col shrink-0">
          {/* Header */}
          <div className="p-4 border-b border-studio-border">
            <div className="flex items-center space-x-2">
              <Sparkles className="w-4 h-4 text-indigo-400" />
              <h3 className="text-xs font-bold uppercase tracking-wider text-studio-text">
                Inspetor da Cena {selectedScene.order}
              </h3>
            </div>
            <p className="text-xs text-studio-subtle mt-1">{selectedScene.title}</p>
          </div>

          {/* Form details of selected scene */}
          <div className="flex-1 overflow-y-auto p-4 space-y-4">
            <div className="space-y-1.5">
              <label className="text-xs font-semibold text-studio-muted">Título da Cena</label>
              <input
                type="text"
                value={selectedScene.title}
                onChange={(e) => {
                  const updated = storyboard.map((s) =>
                    s.id === selectedScene.id ? { ...s, title: e.target.value } : s
                  );
                  onUpdateStoryboard(updated);
                }}
                className="w-full bg-studio-card border border-studio-border rounded-lg px-3 py-1.5 text-xs text-studio-text focus:outline-none focus:border-indigo-500"
              />
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-semibold text-studio-muted">Local / Ponto Turístico</label>
              <input
                type="text"
                value={selectedScene.location}
                onChange={(e) => {
                  const updated = storyboard.map((s) =>
                    s.id === selectedScene.id ? { ...s, location: e.target.value } : s
                  );
                  onUpdateStoryboard(updated);
                }}
                className="w-full bg-studio-card border border-studio-border rounded-lg px-3 py-1.5 text-xs text-studio-text focus:outline-none focus:border-indigo-500"
              />
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-semibold text-studio-muted">Vibe & Emoção Alvo</label>
              <input
                type="text"
                value={selectedScene.vibe}
                onChange={(e) => {
                  const updated = storyboard.map((s) =>
                    s.id === selectedScene.id ? { ...s, vibe: e.target.value } : s
                  );
                  onUpdateStoryboard(updated);
                }}
                className="w-full bg-studio-card border border-studio-border rounded-lg px-3 py-1.5 text-xs text-studio-text focus:outline-none focus:border-indigo-500"
              />
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-semibold text-studio-muted">Objetivo Narrativo (Story Arc)</label>
              <textarea
                rows={3}
                value={selectedScene.narrativeGoal}
                onChange={(e) => {
                  const updated = storyboard.map((s) =>
                    s.id === selectedScene.id ? { ...s, narrativeGoal: e.target.value } : s
                  );
                  onUpdateStoryboard(updated);
                }}
                className="w-full bg-studio-card border border-studio-border rounded-lg px-3 py-1.5 text-xs text-studio-text focus:outline-none focus:border-indigo-500 resize-none leading-relaxed"
              />
            </div>

            {/* AI Prompts Section */}
            <div className="pt-3 border-t border-studio-border space-y-2.5">
              <div className="flex items-center space-x-1.5 text-indigo-400">
                <Wand2 className="w-3.5 h-3.5" />
                <span className="text-xs font-bold uppercase tracking-wider">Ações com IA Narrativa</span>
              </div>

              <button
                onClick={() => handleApplyAiOptimization('retention')}
                className="w-full text-left bg-studio-card hover:bg-studio-cardHover border border-studio-border hover:border-indigo-500/40 p-2.5 rounded-xl transition-all group"
              >
                <div className="flex items-center justify-between text-xs font-semibold text-studio-text group-hover:text-white">
                  <span>Otimizar Curva de Retenção</span>
                  <Sparkles className="w-3 h-3 text-amber-400" />
                </div>
                <p className="text-[11px] text-studio-subtle mt-0.5">
                  Ajusta o ritmo dos cortes para prender a atenção nos primeiros 3 segundos.
                </p>
              </button>

              <button
                onClick={() => handleApplyAiOptimization('music')}
                className="w-full text-left bg-studio-card hover:bg-studio-cardHover border border-studio-border hover:border-indigo-500/40 p-2.5 rounded-xl transition-all group"
              >
                <div className="flex items-center justify-between text-xs font-semibold text-studio-text group-hover:text-white">
                  <span>Sincronizar Cortes com Beats</span>
                  <Music className="w-3 h-3 text-indigo-400" />
                </div>
                <p className="text-[11px] text-studio-subtle mt-0.5">
                  Alinha os pontos de entrada e saída com as batidas de 118 BPM da trilha.
                </p>
              </button>

              <button
                onClick={() => handleApplyAiOptimization('reorder')}
                className="w-full text-left bg-studio-card hover:bg-studio-cardHover border border-studio-border hover:border-indigo-500/40 p-2.5 rounded-xl transition-all group"
              >
                <div className="flex items-center justify-between text-xs font-semibold text-studio-text group-hover:text-white">
                  <span>Reorganizar por Rota e GPS</span>
                  <MapPin className="w-3 h-3 text-emerald-400" />
                </div>
                <p className="text-[11px] text-studio-subtle mt-0.5">
                  Organiza a viagem na sequência geográfica exata do itinerário.
                </p>
              </button>
            </div>
          </div>

          {/* Footer Navigation */}
          <div className="p-4 border-t border-studio-border bg-studio-panel space-y-2">
            {onNavigateToNLE && (
              <button
                onClick={onNavigateToNLE}
                className="w-full flex items-center justify-center space-x-2 bg-gradient-to-r from-indigo-600 to-indigo-500 hover:from-indigo-500 hover:to-indigo-400 text-white text-xs py-2.5 rounded-xl font-bold shadow-glow-indigo transition-all"
              >
                <Tv className="w-4 h-4" />
                <span>Abrir Linha do Tempo da Montagem</span>
              </button>
            )}

            {onNavigateToAudio && (
              <button
                onClick={onNavigateToAudio}
                className="w-full flex items-center justify-center space-x-2 bg-studio-card hover:bg-studio-cardHover border border-studio-border text-xs py-2 rounded-lg text-studio-text font-semibold transition-colors"
              >
                <Music className="w-3.5 h-3.5 text-indigo-400" />
                <span>Tratamento de Áudio & Trilha</span>
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
