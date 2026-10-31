import React, { useState } from 'react';
import {
  Search,
  LayoutGrid,
  List,
  MoreVertical,
  Play,
  Sparkles,
  Camera,
  MapPin,
  Clock,
  ChevronDown,
  Check
} from 'lucide-react';
import { LibraryClip, MediaFilterCategory, ViewMode } from '../types';

interface MediaGridProps {
  clips: LibraryClip[];
  selectedClipId: string;
  onSelectClip: (clip: LibraryClip) => void;
  onOpenClipMenu?: (clip: LibraryClip, e: React.MouseEvent) => void;
  onUpdateClipCategory?: (clipId: string, newCategory: LibraryClip['category']) => void;
}

const CATEGORY_OPTIONS: LibraryClip['category'][] = [
  'Conversa',
  'Drone',
  'Paisagem',
  'Deslocamento',
  'B-roll',
  'Outros'
];

export const MediaGrid: React.FC<MediaGridProps> = ({
  clips,
  selectedClipId,
  onSelectClip,
  onOpenClipMenu,
  onUpdateClipCategory
}) => {
  const [selectedCategory, setSelectedCategory] = useState<MediaFilterCategory>('Todos');
  const [searchQuery, setSearchQuery] = useState('');
  const [viewMode, setViewMode] = useState<ViewMode>('grid');
  const [activeCategoryMenuClipId, setActiveCategoryMenuClipId] = useState<string | null>(null);

  // Filter counts
  const counts = {
    Todos: clips.length,
    Conversas: clips.filter((c) => c.category === 'Conversa').length,
    Paisagens: clips.filter((c) => c.category === 'Paisagem').length,
    Deslocamentos: clips.filter((c) => c.category === 'Deslocamento').length,
    Drone: clips.filter((c) => c.category === 'Drone').length,
    Outros: clips.filter((c) => c.category === 'B-roll' || c.category === 'Outros').length,
  };

  const categories: { label: MediaFilterCategory; count: number }[] = [
    { label: 'Todos', count: counts.Todos },
    { label: 'Conversas', count: counts.Conversas },
    { label: 'Paisagens', count: counts.Paisagens },
    { label: 'Deslocamentos', count: counts.Deslocamentos },
    { label: 'Drone', count: counts.Drone },
    { label: 'Outros', count: counts.Outros },
  ];

  const filteredClips = clips.filter((clip) => {
    const matchesCategory =
      selectedCategory === 'Todos' ||
      (selectedCategory === 'Conversas' && clip.category === 'Conversa') ||
      (selectedCategory === 'Paisagens' && clip.category === 'Paisagem') ||
      (selectedCategory === 'Deslocamentos' && clip.category === 'Deslocamento') ||
      (selectedCategory === 'Drone' && clip.category === 'Drone') ||
      (selectedCategory === 'Outros' && (clip.category === 'B-roll' || clip.category === 'Outros'));

    const query = searchQuery.toLowerCase();
    const matchesSearch =
      clip.filename.toLowerCase().includes(query) ||
      clip.title.toLowerCase().includes(query) ||
      clip.location.toLowerCase().includes(query);

    return matchesCategory && matchesSearch;
  });

  const getCategoryBadgeClass = (category: string) => {
    switch (category) {
      case 'Conversa':
        return 'bg-emerald-500/20 text-emerald-300 border-emerald-500/40';
      case 'Deslocamento':
        return 'bg-sky-500/20 text-sky-300 border-sky-500/40';
      case 'Drone':
        return 'bg-purple-500/20 text-purple-300 border-purple-500/40';
      case 'Paisagem':
        return 'bg-amber-500/20 text-amber-300 border-amber-500/40';
      case 'B-roll':
      default:
        return 'bg-yellow-500/20 text-yellow-300 border-yellow-500/40';
    }
  };

  const getSegmentColorClass = (category: string) => {
    switch (category) {
      case 'speech':
      case 'Conversa':
        return 'bg-emerald-500';
      case 'drone':
      case 'Drone':
        return 'bg-purple-500';
      case 'road_trip':
      case 'Deslocamento':
        return 'bg-sky-500';
      case 'landscape':
      case 'Paisagem':
        return 'bg-amber-500';
      case 'broll':
      case 'B-roll':
      default:
        return 'bg-yellow-500';
    }
  };

  return (
    <div className="flex flex-col space-y-3.5">
      {/* Top Toolbar */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <h2 className="text-base font-bold text-white tracking-tight flex items-center space-x-2">
          <span>Clipes e análise</span>
          <span className="text-xs font-normal text-slate-500">
            ({filteredClips.length} de {clips.length} carregados)
          </span>
        </h2>

        <div className="flex items-center space-x-2">
          {/* Search Input */}
          <div className="relative w-48 sm:w-64">
            <Search className="w-3.5 h-3.5 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Buscar clipes..."
              className="w-full bg-slate-900 border border-slate-800 rounded-lg pl-8 pr-3 py-1.5 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-blue-500 transition-colors"
            />
            {searchQuery && (
              <button
                onClick={() => setSearchQuery('')}
                className="absolute right-2.5 top-1/2 -translate-y-1/2 text-slate-500 hover:text-white text-xs"
              >
                ✕
              </button>
            )}
          </div>

          {/* View Mode Toggle */}
          <div className="flex items-center bg-slate-900 border border-slate-800 rounded-lg p-0.5">
            <button
              onClick={() => setViewMode('grid')}
              title="Visualização em Grade"
              className={`p-1.5 rounded transition-all ${
                viewMode === 'grid'
                  ? 'bg-blue-600 text-white shadow-sm'
                  : 'text-slate-400 hover:text-white hover:bg-slate-800'
              }`}
            >
              <LayoutGrid className="w-3.5 h-3.5" />
            </button>
            <button
              onClick={() => setViewMode('list')}
              title="Visualização em Lista"
              className={`p-1.5 rounded transition-all ${
                viewMode === 'list'
                  ? 'bg-blue-600 text-white shadow-sm'
                  : 'text-slate-400 hover:text-white hover:bg-slate-800'
              }`}
            >
              <List className="w-3.5 h-3.5" />
            </button>
          </div>
        </div>
      </div>

      {/* Category Filter Pills */}
      <div className="flex items-center space-x-1.5 overflow-x-auto pb-1 scrollbar-none">
        {categories.map((cat) => {
          const isActive = selectedCategory === cat.label;
          return (
            <button
              key={cat.label}
              onClick={() => setSelectedCategory(cat.label)}
              className={`px-3 py-1.5 rounded-full text-xs font-medium transition-all whitespace-nowrap shrink-0 flex items-center space-x-1.5 ${
                isActive
                  ? 'bg-blue-600 text-white font-semibold shadow-sm'
                  : 'bg-slate-900/80 hover:bg-slate-800 border border-slate-800/80 text-slate-400 hover:text-slate-200'
              }`}
            >
              <span>{cat.label}</span>
              <span
                className={`text-[10px] px-1.5 py-0.2 rounded-full ${
                  isActive ? 'bg-blue-700/60 text-white' : 'bg-slate-800 text-slate-400'
                }`}
              >
                {cat.count}
              </span>
            </button>
          );
        })}
      </div>

      {/* Media Grid / List / Empty State */}
      {filteredClips.length === 0 ? (
        <div className="py-12 px-4 text-center rounded-xl bg-slate-900/40 border border-dashed border-slate-800 text-slate-400 flex flex-col items-center justify-center space-y-3">
          <div className="p-3 rounded-full bg-slate-800/80 text-slate-400">
            <Camera className="w-8 h-8" />
          </div>
          <div>
            <h3 className="text-base font-semibold text-slate-200">Nenhum clipe de vídeo encontrado</h3>
            <p className="text-xs text-slate-400 mt-1 max-w-md">
              {clips.length === 0
                ? 'Este projeto ainda não possui mídias importadas. Selecione uma pasta de vídeos ou importe arquivos para iniciar o inventário e a análise com IA.'
                : 'Nenhum clipe corresponde aos filtros ou busca selecionados.'}
            </p>
          </div>
        </div>
      ) : viewMode === 'grid' ? (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
          {filteredClips.map((clip) => {
            const isSelected = clip.id === selectedClipId;

            return (
              <div
                key={clip.id}
                onClick={() => onSelectClip(clip)}
                className={`group cursor-pointer bg-slate-900/90 rounded-xl overflow-hidden border transition-all relative flex flex-col ${
                  isSelected
                    ? 'border-blue-500 ring-2 ring-blue-500/40 shadow-lg shadow-blue-500/10'
                    : 'border-slate-800/90 hover:border-slate-700 hover:bg-slate-850'
                }`}
              >
                {/* Thumbnail Area */}
                <div className="relative aspect-video w-full bg-slate-950 overflow-hidden">
                  <img
                    src={clip.thumbnail}
                    alt={clip.title}
                    className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                    loading="lazy"
                  />

                  {/* Gradient Overlay */}
                  <div className="absolute inset-0 bg-gradient-to-t from-slate-950/80 via-transparent to-slate-950/30 pointer-events-none" />

                  {/* Top Category Badge & Quick Menu */}
                  <div className="absolute top-2 left-2 flex items-center space-x-1 z-20">
                    <button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation();
                        setActiveCategoryMenuClipId(
                          activeCategoryMenuClipId === clip.id ? null : clip.id
                        );
                      }}
                      className={`text-[10px] font-semibold px-2 py-0.5 rounded-full border shadow-sm flex items-center space-x-1 hover:brightness-110 transition-all ${getCategoryBadgeClass(
                        clip.category
                      )}`}
                      title="Clique para reclassificar a tomada"
                    >
                      <span>{clip.category}</span>
                      {clip.isUserOverridden && (
                        <span className="w-1.5 h-1.5 rounded-full bg-amber-400" title="Classificação personalizada pelo usuário" />
                      )}
                      <ChevronDown className="w-2.5 h-2.5 opacity-70" />
                    </button>

                    {/* Dropdown Menu */}
                    {activeCategoryMenuClipId === clip.id && (
                      <div
                        className="absolute top-6 left-0 z-30 bg-slate-900 border border-slate-700 rounded-lg shadow-2xl py-1 w-36 text-xs backdrop-blur-md"
                        onClick={(e) => e.stopPropagation()}
                      >
                        <div className="px-2 py-1 text-[9px] uppercase tracking-wider text-slate-400 font-bold border-b border-slate-800">
                          Reclassificar
                        </div>
                        {CATEGORY_OPTIONS.map((cat) => (
                          <button
                            key={cat}
                            type="button"
                            onClick={() => {
                              onUpdateClipCategory?.(clip.id, cat);
                              setActiveCategoryMenuClipId(null);
                            }}
                            className={`w-full px-2 py-1 text-left flex items-center justify-between hover:bg-slate-800 transition-colors ${
                              clip.category === cat ? 'text-blue-400 font-semibold' : 'text-slate-300'
                            }`}
                          >
                            <span className="flex items-center space-x-1.5">
                              <span className={`w-2 h-2 rounded-full ${getSegmentColorClass(cat)}`} />
                              <span>{cat}</span>
                            </span>
                            {clip.category === cat && <Check className="w-3 h-3 text-blue-400" />}
                          </button>
                        ))}
                      </div>
                    )}
                  </div>

                  {/* Duration Pill Bottom Right */}
                  <div className="absolute bottom-2 right-2 bg-slate-950/85 backdrop-blur-sm text-slate-200 text-[10px] font-mono px-1.5 py-0.5 rounded border border-slate-700/60 shadow">
                    {clip.duration}
                  </div>

                  {/* Play icon overlay on hover */}
                  <div className="absolute inset-0 flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity bg-slate-950/40">
                    <div className="w-9 h-9 rounded-full bg-blue-600/90 text-white flex items-center justify-center shadow-lg transform group-hover:scale-110 transition-transform">
                      <Play className="w-4 h-4 ml-0.5 fill-current" />
                    </div>
                  </div>
                </div>

                {/* Mini Temporal Segment Bar */}
                <div
                  className="w-full h-1.5 bg-slate-950 flex overflow-hidden border-b border-slate-800/60"
                  title="Distribuição temporal dos segmentos classificados"
                >
                  {clip.segments && clip.segments.length > 0 ? (
                    clip.segments.map((seg, idx) => {
                      const segDuration = Math.max(0.1, seg.endSec - seg.startSec);
                      const totalDuration = clip.durationSec || 30;
                      const segWidth = (segDuration / totalDuration) * 100;
                      return (
                        <div
                          key={seg.id || idx}
                          className={`${getSegmentColorClass(seg.category)} h-full transition-opacity hover:opacity-80`}
                          style={{ width: `${Math.max(1.5, segWidth)}%` }}
                          title={`${seg.category}: ${seg.startSec.toFixed(1)}s - ${seg.endSec.toFixed(1)}s (${Math.round(seg.confidence * 100)}%)`}
                        />
                      );
                    })
                  ) : (
                    <div
                      className={`w-full h-full ${getSegmentColorClass(clip.category)}`}
                      title={clip.category}
                    />
                  )}
                </div>

                {/* Waveform Strip */}
                <div className="px-2.5 pt-2 pb-1 bg-slate-950/50 flex items-center space-x-0.5 h-6">
                  {clip.waveform.slice(0, 24).map((val, idx) => (
                    <div
                      key={idx}
                      className={`flex-1 rounded-full transition-all ${
                        isSelected
                          ? 'bg-blue-400 group-hover:bg-blue-300'
                          : 'bg-slate-600 group-hover:bg-slate-500'
                      }`}
                      style={{ height: `${Math.max(15, (val / 100) * 100)}%` }}
                    />
                  ))}
                </div>

                {/* Card Bottom Meta */}
                <div className="p-2.5 flex items-center justify-between text-xs border-t border-slate-800/60 bg-slate-900/60">
                  <div className="flex flex-col min-w-0 pr-1">
                    <span
                      className={`font-mono text-xs truncate ${
                        isSelected ? 'text-blue-300 font-bold' : 'text-slate-200 font-medium'
                      }`}
                      title={clip.filename}
                    >
                      {clip.filename}
                    </span>
                    <span className="text-[10px] text-slate-400 truncate mt-0.5">
                      {clip.title}
                    </span>
                  </div>

                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      onOpenClipMenu?.(clip, e);
                    }}
                    className="text-slate-400 hover:text-white p-1 rounded hover:bg-slate-800/80 transition-colors shrink-0"
                    title="Mais opções"
                  >
                    <MoreVertical className="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      ) : (
        /* List View */
        <div className="bg-slate-900/80 border border-slate-800/90 rounded-xl overflow-hidden divide-y divide-slate-800/60">
          {filteredClips.map((clip) => {
            const isSelected = clip.id === selectedClipId;
            return (
              <div
                key={clip.id}
                onClick={() => onSelectClip(clip)}
                className={`flex items-center justify-between p-2.5 hover:bg-slate-800/50 cursor-pointer transition-colors ${
                  isSelected ? 'bg-blue-600/10 border-l-2 border-l-blue-500' : ''
                }`}
              >
                <div className="flex items-center space-x-3 min-w-0">
                  <div className="w-16 h-10 rounded bg-slate-950 overflow-hidden relative shrink-0 flex flex-col">
                    <img
                      src={clip.thumbnail}
                      alt={clip.title}
                      className="w-full h-full object-cover"
                    />
                    <span className="absolute bottom-0.5 right-0.5 text-[9px] font-mono bg-slate-950/80 px-1 rounded text-slate-300">
                      {clip.duration}
                    </span>
                  </div>

                  <div className="flex flex-col min-w-0">
                    <span
                      className={`font-mono text-xs truncate ${
                        isSelected ? 'text-blue-300 font-bold' : 'text-slate-200 font-medium'
                      }`}
                    >
                      {clip.filename}
                    </span>
                    <span className="text-[11px] text-slate-400 truncate">{clip.title}</span>
                  </div>
                </div>

                <div className="flex items-center space-x-4 shrink-0 text-xs text-slate-400">
                  <div className="relative">
                    <button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation();
                        setActiveCategoryMenuClipId(
                          activeCategoryMenuClipId === `list_${clip.id}` ? null : `list_${clip.id}`
                        );
                      }}
                      className={`text-[10px] font-semibold px-2 py-0.5 rounded-full border flex items-center space-x-1 hover:brightness-110 ${getCategoryBadgeClass(
                        clip.category
                      )}`}
                    >
                      <span>{clip.category}</span>
                      {clip.isUserOverridden && (
                        <span className="w-1.5 h-1.5 rounded-full bg-amber-400" />
                      )}
                      <ChevronDown className="w-2.5 h-2.5 opacity-70" />
                    </button>

                    {activeCategoryMenuClipId === `list_${clip.id}` && (
                      <div
                        className="absolute right-0 top-6 z-30 bg-slate-900 border border-slate-700 rounded-lg shadow-2xl py-1 w-36 text-xs backdrop-blur-md"
                        onClick={(e) => e.stopPropagation()}
                      >
                        <div className="px-2 py-1 text-[9px] uppercase tracking-wider text-slate-400 font-bold border-b border-slate-800">
                          Reclassificar
                        </div>
                        {CATEGORY_OPTIONS.map((cat) => (
                          <button
                            key={cat}
                            type="button"
                            onClick={() => {
                              onUpdateClipCategory?.(clip.id, cat);
                              setActiveCategoryMenuClipId(null);
                            }}
                            className={`w-full px-2 py-1 text-left flex items-center justify-between hover:bg-slate-800 transition-colors ${
                              clip.category === cat ? 'text-blue-400 font-semibold' : 'text-slate-300'
                            }`}
                          >
                            <span className="flex items-center space-x-1.5">
                              <span className={`w-2 h-2 rounded-full ${getSegmentColorClass(cat)}`} />
                              <span>{cat}</span>
                            </span>
                            {clip.category === cat && <Check className="w-3 h-3 text-blue-400" />}
                          </button>
                        ))}
                      </div>
                    )}
                  </div>

                  <span className="hidden md:inline font-mono text-[11px] text-slate-400">
                    {clip.resolution}
                  </span>

                  <span className="hidden lg:inline text-[11px] text-slate-400">
                    {clip.camera}
                  </span>

                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      onOpenClipMenu?.(clip, e);
                    }}
                    className="p-1 hover:text-white rounded"
                  >
                    <MoreVertical className="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
