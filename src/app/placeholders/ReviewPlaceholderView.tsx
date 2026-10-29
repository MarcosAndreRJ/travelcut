import React, { useState } from 'react';
import {
  CheckSquare,
  Sparkles,
  Check,
  X,
  Play,
  Film,
  Clock,
  ThumbsUp,
  Share2,
  Tv,
  Eye,
  Sliders
} from 'lucide-react';
import { PageHeader } from '../../components/ui/PageHeader';
import { Card, CardHeader, CardTitle, CardContent, CardDescription, CardFooter } from '../../components/ui/Card';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';

export interface ReviewPlaceholderViewProps {
  onNavigateToTab?: (tab: string) => void;
}

interface ReviewCutItem {
  id: string;
  clipName: string;
  camera: string;
  timecode: string;
  duration: string;
  score: number;
  reason: string;
  tags: string[];
  status: 'pending' | 'accepted' | 'rejected';
}

const INITIAL_CUTS: ReviewCutItem[] = [
  {
    id: 'cut-1',
    clipName: 'C0012_Kyoto_FushimiInari_Entrance.mp4',
    camera: 'Sony FX3',
    timecode: '00:00:14:10 - 00:00:19:00',
    duration: '4.6s',
    score: 96,
    reason: 'Passagem pelo Torii com movimento suave de gimbal e boa iluminação natural',
    tags: ['Gimbal', 'Estável', 'Ponto Chave'],
    status: 'accepted'
  },
  {
    id: 'cut-2',
    clipName: 'C0028_Tokyo_ShibuyaCrossing_NightWalk.mp4',
    camera: 'Sony FX3',
    timecode: '00:01:05:00 - 00:01:09:12',
    duration: '4.2s',
    score: 92,
    reason: 'Multidão em Shibuya com luzes de neon e áudio ambiente nítido',
    tags: ['Ação', 'Luz de Neon', 'B-Roll'],
    status: 'accepted'
  },
  {
    id: 'cut-3',
    clipName: 'C0045_Kyoto_BambooForest_Morning.mp4',
    camera: 'DJI Mini 4 Pro',
    timecode: '00:00:03:00 - 00:00:08:15',
    duration: '5.2s',
    score: 89,
    reason: 'Subida vertical cinematográfica revelando o topo do bosque de bambu',
    tags: ['Drone', '4K 60fps', 'Transição'],
    status: 'pending'
  },
  {
    id: 'cut-4',
    clipName: 'C0062_Tokyo_Ramen_CloseUp.mp4',
    camera: 'iPhone 15 Pro',
    timecode: '00:00:08:20 - 00:00:12:00',
    duration: '3.3s',
    score: 85,
    reason: 'Close-up no preparo do caldo quente, vapor e reação positiva',
    tags: ['Gastronomia', 'Vapor', 'Expressão'],
    status: 'pending'
  }
];

export const ReviewPlaceholderView: React.FC<ReviewPlaceholderViewProps> = ({
  onNavigateToTab
}) => {
  const [cuts, setCuts] = useState<ReviewCutItem[]>(INITIAL_CUTS);

  const handleStatusChange = (id: string, newStatus: 'accepted' | 'rejected') => {
    setCuts((prev) =>
      prev.map((c) => (c.id === id ? { ...c, status: newStatus } : c))
    );
  };

  const acceptedCount = cuts.filter((c) => c.status === 'accepted').length;
  const pendingCount = cuts.filter((c) => c.status === 'pending').length;

  return (
    <div className="flex-1 flex flex-col overflow-y-auto">
      <PageHeader
        title="Revisão de Cortes & Refinamento"
        subtitle="Aprovação rápida de trechos selecionados pela IA antes da montagem na linha do tempo"
        badge={
          <Badge variant="warning" size="sm" withDot>
            {pendingCount} Pendentes
          </Badge>
        }
        actions={
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              leftIcon={<Eye className="w-3.5 h-3.5 text-blue-400" />}
              onClick={() => onNavigateToTab?.('edit')}
            >
              Ver na Linha do Tempo
            </Button>
            <Button
              variant="primary"
              size="sm"
              leftIcon={<Share2 className="w-3.5 h-3.5" />}
              onClick={() => onNavigateToTab?.('export')}
            >
              Exportar p/ DaVinci ({acceptedCount} Cortes)
            </Button>
          </div>
        }
      />

      <div className="p-6 max-w-7xl mx-auto w-full space-y-6">
        {/* Overview Banner */}
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div className="bg-studio-card border border-studio-border p-4 rounded-xl flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-emerald-500/15 border border-emerald-500/30 flex items-center justify-center text-emerald-400 shrink-0">
              <Check className="w-5 h-5" />
            </div>
            <div>
              <span className="text-xs text-studio-muted block">Cortes Aprovados</span>
              <span className="text-lg font-bold text-white font-mono">{acceptedCount} trechos</span>
            </div>
          </div>

          <div className="bg-studio-card border border-studio-border p-4 rounded-xl flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-amber-500/15 border border-amber-500/30 flex items-center justify-center text-amber-400 shrink-0">
              <Clock className="w-5 h-5" />
            </div>
            <div>
              <span className="text-xs text-studio-muted block">Aguardando Avaliação</span>
              <span className="text-lg font-bold text-amber-300 font-mono">{pendingCount} trechos</span>
            </div>
          </div>

          <div className="bg-studio-card border border-studio-border p-4 rounded-xl flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-blue-500/15 border border-blue-500/30 flex items-center justify-center text-blue-400 shrink-0">
              <Sparkles className="w-5 h-5" />
            </div>
            <div>
              <span className="text-xs text-studio-muted block">Precisão Estimada</span>
              <span className="text-lg font-bold text-white font-mono">92.4%</span>
            </div>
          </div>
        </div>

        {/* Cuts Review Table/Cards */}
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <h3 className="text-sm font-bold text-white flex items-center gap-2">
              <CheckSquare className="w-4 h-4 text-blue-400" />
              <span>Lista de Sugestões de Corte IA</span>
            </h3>
            <span className="text-xs text-studio-subtle font-mono">
              Classificado por relevância narrativa
            </span>
          </div>

          <div className="grid grid-cols-1 gap-3">
            {cuts.map((cut) => (
              <div
                key={cut.id}
                className={`p-4 rounded-xl border transition-all flex flex-col md:flex-row md:items-center justify-between gap-4 ${
                  cut.status === 'accepted'
                    ? 'bg-studio-card/80 border-emerald-500/30'
                    : cut.status === 'rejected'
                    ? 'bg-studio-card/40 border-studio-border opacity-50'
                    : 'bg-studio-card border-studio-border hover:border-studio-borderLight'
                }`}
              >
                <div className="flex items-start gap-3.5 min-w-0">
                  <div className="w-10 h-10 rounded-lg bg-studio-panel border border-studio-border flex items-center justify-center text-studio-primary shrink-0 mt-0.5">
                    <Film className="w-5 h-5 text-blue-400" />
                  </div>

                  <div className="min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="text-xs font-bold text-white truncate max-w-sm">
                        {cut.clipName}
                      </span>
                      <Badge variant="neutral" size="xs">
                        {cut.camera}
                      </Badge>
                      <Badge
                        variant={cut.score >= 90 ? 'success' : 'primary'}
                        size="xs"
                        className="font-mono font-bold"
                      >
                        Score: {cut.score}
                      </Badge>
                    </div>

                    <p className="text-xs text-studio-muted mt-1 leading-normal">
                      {cut.reason}
                    </p>

                    <div className="flex items-center gap-3 mt-2 text-[11px] font-mono text-studio-subtle">
                      <span>TC: {cut.timecode}</span>
                      <span>•</span>
                      <span>Duração: {cut.duration}</span>
                      <span>•</span>
                      <div className="flex items-center gap-1">
                        {cut.tags.map((t) => (
                          <span
                            key={t}
                            className="bg-studio-panel px-1.5 py-0.2 rounded text-[10px] text-studio-muted border border-studio-border/60"
                          >
                            #{t}
                          </span>
                        ))}
                      </div>
                    </div>
                  </div>
                </div>

                {/* Actions: Accept or Reject */}
                <div className="flex items-center gap-2 shrink-0 self-end md:self-center">
                  <Button
                    variant={cut.status === 'rejected' ? 'danger' : 'outline'}
                    size="sm"
                    leftIcon={<X className="w-3.5 h-3.5" />}
                    onClick={() => handleStatusChange(cut.id, 'rejected')}
                    title="Descartar este trecho"
                  >
                    Descartar
                  </Button>
                  <Button
                    variant={cut.status === 'accepted' ? 'success' : 'primary'}
                    size="sm"
                    leftIcon={<Check className="w-3.5 h-3.5" />}
                    onClick={() => handleStatusChange(cut.id, 'accepted')}
                    title="Aprovar e incluir na linha do tempo"
                  >
                    {cut.status === 'accepted' ? 'Aprovado' : 'Aprovar'}
                  </Button>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
};
