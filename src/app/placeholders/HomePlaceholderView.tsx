import React from 'react';
import {
  FolderPlus,
  PlaySquare,
  Sparkles,
  Share2,
  Clock,
  HardDrive,
  Film,
  ArrowRight,
  ShieldCheck,
  CheckCircle2,
  Tv
} from 'lucide-react';
import { HomeProject, HomeAction } from '../../types/homeContract';
import { Button } from '../../components/ui/Button';
import { Card, CardHeader, CardTitle, CardContent, CardDescription, CardFooter } from '../../components/ui/Card';
import { Badge } from '../../components/ui/Badge';
import { PageHeader } from '../../components/ui/PageHeader';

export interface HomePlaceholderViewProps {
  project?: HomeProject | null;
  onAction?: (action: HomeAction) => void;
  onNavigateToTab?: (tab: string) => void;
}

export const HomePlaceholderView: React.FC<HomePlaceholderViewProps> = ({
  project,
  onAction,
  onNavigateToTab
}) => {
  const currentProject: HomeProject = project || {
    id: 'proj-japan-2026',
    name: 'Japão 2026 - Tóquio & Kyoto 4K',
    path: 'D:/Videos/Viagens/2026_Japao_4K_SonyFX3',
    lastModified: 'Hoje às 15:42',
    clipCount: 14,
    totalDurationFormatted: '00:01:01:15'
  };

  const handleAction = (action: HomeAction) => {
    if (onAction) {
      onAction(action);
    }
  };

  return (
    <div className="flex-1 flex flex-col overflow-y-auto">
      {/* Contextual Page Header */}
      <PageHeader
        title="Painel Inicial"
        subtitle="Centro de controle de pré-edição de viagens e preparação para o DaVinci Resolve"
        badge={
          <Badge variant="turquoise" size="sm" withDot>
            Fase 1 Foundation
          </Badge>
        }
        actions={
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              leftIcon={<FolderPlus className="w-3.5 h-3.5 text-blue-400" />}
              onClick={() => handleAction({ type: 'import_media' })}
            >
              Adicionar Mídias
            </Button>
            <Button
              variant="primary"
              size="sm"
              leftIcon={<Sparkles className="w-3.5 h-3.5" />}
              onClick={() => onNavigateToTab?.('analysis')}
            >
              Iniciar Análise IA
            </Button>
          </div>
        }
      />

      <div className="p-6 max-w-7xl mx-auto w-full space-y-6">
        {/* Phase 2 Integration Contract Notice */}
        <div className="bg-gradient-to-r from-blue-950/40 via-indigo-950/30 to-studio-card border border-blue-500/30 p-4 rounded-2xl flex flex-col md:flex-row items-start md:items-center justify-between gap-4 shadow-panel">
          <div className="flex items-start gap-3.5">
            <div className="w-9 h-9 rounded-xl bg-blue-600/20 border border-blue-500/40 flex items-center justify-center text-blue-400 shrink-0 mt-0.5">
              <ShieldCheck className="w-5 h-5" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xs font-bold uppercase tracking-wider text-blue-300 font-mono">
                  Contrato de Integração • Fase 2
                </span>
                <Badge variant="primary" size="xs">
                  HomeView.tsx Placeholder
                </Badge>
              </div>
              <p className="text-xs text-studio-muted mt-1 leading-relaxed max-w-2xl">
                Este placeholder implementa a interface contratual para a Fase 2 (<code>HomeViewProps</code>).
                Após o merge da branch <code>feature/phase-02-home</code>, o módulo oficial assumirá esta visão automaticamente sem alterar a estrutura da aplicação.
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2 shrink-0">
            <Button
              variant="outline"
              size="xs"
              onClick={() => handleAction({ type: 'quick_tour' })}
            >
              Guia de Integração
            </Button>
          </div>
        </div>

        {/* Hero Section: Active Project Card & Quick Actions */}
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">
          {/* Main Active Project Overview */}
          <Card variant="elevated" className="lg:col-span-2 border-studio-border">
            <CardHeader>
              <div className="flex items-center justify-between w-full">
                <CardTitle>
                  <Film className="w-4 h-4 text-blue-400" />
                  <span>Projeto Atual em Andamento</span>
                </CardTitle>
                <Badge variant="success" size="xs" withDot pulse>
                  Pronto para DaVinci
                </Badge>
              </div>
            </CardHeader>

            <CardContent className="space-y-4">
              <div className="bg-studio-panel/70 border border-studio-border/80 p-4 rounded-xl flex flex-col sm:flex-row sm:items-center justify-between gap-4">
                <div>
                  <h2 className="text-base font-bold text-white tracking-tight">
                    {currentProject.name}
                  </h2>
                  <p className="text-xs font-mono text-studio-subtle mt-0.5 truncate max-w-md">
                    {currentProject.path}
                  </p>
                </div>
                <Button
                  variant="primary"
                  size="sm"
                  rightIcon={<ArrowRight className="w-3.5 h-3.5" />}
                  onClick={() => onNavigateToTab?.('analysis')}
                >
                  Abrir Análise
                </Button>
              </div>

              {/* Project Quick Stats */}
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 pt-1">
                <div className="bg-studio-panel/50 border border-studio-border/60 p-3 rounded-xl">
                  <div className="flex items-center gap-1.5 text-studio-subtle text-[11px] mb-1">
                    <Film className="w-3.5 h-3.5 text-blue-400" />
                    <span>Clipes Importados</span>
                  </div>
                  <span className="text-sm font-bold text-white font-mono">
                    {currentProject.clipCount} vídeos
                  </span>
                </div>

                <div className="bg-studio-panel/50 border border-studio-border/60 p-3 rounded-xl">
                  <div className="flex items-center gap-1.5 text-studio-subtle text-[11px] mb-1">
                    <Clock className="w-3.5 h-3.5 text-cyan-400" />
                    <span>Duração Prevista</span>
                  </div>
                  <span className="text-sm font-bold text-white font-mono">
                    {currentProject.totalDurationFormatted || '00:01:01:15'}
                  </span>
                </div>

                <div className="bg-studio-panel/50 border border-studio-border/60 p-3 rounded-xl">
                  <div className="flex items-center gap-1.5 text-studio-subtle text-[11px] mb-1">
                    <Sparkles className="w-3.5 h-3.5 text-amber-400" />
                    <span>Cortes Sugeridos</span>
                  </div>
                  <span className="text-sm font-bold text-emerald-400 font-mono">
                    12 aceitos
                  </span>
                </div>

                <div className="bg-studio-panel/50 border border-studio-border/60 p-3 rounded-xl">
                  <div className="flex items-center gap-1.5 text-studio-subtle text-[11px] mb-1">
                    <HardDrive className="w-3.5 h-3.5 text-indigo-400" />
                    <span>Resolução Master</span>
                  </div>
                  <span className="text-sm font-bold text-white font-mono">
                    4K 60fps ProRes
                  </span>
                </div>
              </div>
            </CardContent>

            <CardFooter>
              <div className="flex items-center gap-4 text-xs text-studio-muted">
                <span className="flex items-center gap-1.5">
                  <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400" />
                  Áudio sincronizado com beats
                </span>
                <span className="flex items-center gap-1.5">
                  <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400" />
                  FCPXML pronto para Resolve
                </span>
              </div>
              <span className="text-[11px] text-studio-subtle font-mono">
                Modificado: {currentProject.lastModified}
              </span>
            </CardFooter>
          </Card>

          {/* Quick Project Actions */}
          <Card variant="elevated" className="border-studio-border flex flex-col justify-between">
            <CardHeader>
              <CardTitle>Ações Rápidas de Fluxo</CardTitle>
              <CardDescription>Atalhos rápidos para o pipeline de corte</CardDescription>
            </CardHeader>

            <CardContent className="space-y-2.5">
              <button
                onClick={() => onNavigateToTab?.('analysis')}
                className="w-full flex items-center justify-between p-3 rounded-xl bg-studio-panel hover:bg-studio-cardHover border border-studio-border hover:border-studio-borderLight transition-all group text-left cursor-pointer"
              >
                <div className="flex items-center gap-3">
                  <div className="w-8 h-8 rounded-lg bg-blue-600/15 border border-blue-500/30 flex items-center justify-center text-blue-400">
                    <Sparkles className="w-4 h-4" />
                  </div>
                  <div>
                    <span className="text-xs font-bold text-white block group-hover:text-blue-300 transition-colors">
                      1. Mídia & Transcrição
                    </span>
                    <span className="text-[11px] text-studio-muted">
                      Whisper v3 e detecção de cenas IA
                    </span>
                  </div>
                </div>
                <ArrowRight className="w-4 h-4 text-studio-subtle group-hover:text-white transition-transform group-hover:translate-x-0.5" />
              </button>

              <button
                onClick={() => onNavigateToTab?.('edit')}
                className="w-full flex items-center justify-between p-3 rounded-xl bg-studio-panel hover:bg-studio-cardHover border border-studio-border hover:border-studio-borderLight transition-all group text-left cursor-pointer"
              >
                <div className="flex items-center gap-3">
                  <div className="w-8 h-8 rounded-lg bg-cyan-600/15 border border-cyan-500/30 flex items-center justify-center text-cyan-400">
                    <PlaySquare className="w-4 h-4" />
                  </div>
                  <div>
                    <span className="text-xs font-bold text-white block group-hover:text-cyan-300 transition-colors">
                      2. Roteiro & Storyboard
                    </span>
                    <span className="text-[11px] text-studio-muted">
                      Organize os momentos da viagem
                    </span>
                  </div>
                </div>
                <ArrowRight className="w-4 h-4 text-studio-subtle group-hover:text-white transition-transform group-hover:translate-x-0.5" />
              </button>

              <button
                onClick={() => onNavigateToTab?.('export')}
                className="w-full flex items-center justify-between p-3 rounded-xl bg-studio-panel hover:bg-studio-cardHover border border-studio-border hover:border-studio-borderLight transition-all group text-left cursor-pointer"
              >
                <div className="flex items-center gap-3">
                  <div className="w-8 h-8 rounded-lg bg-emerald-600/15 border border-emerald-500/30 flex items-center justify-center text-emerald-400">
                    <Share2 className="w-4 h-4" />
                  </div>
                  <div>
                    <span className="text-xs font-bold text-white block group-hover:text-emerald-300 transition-colors">
                      3. Exportar para DaVinci
                    </span>
                    <span className="text-[11px] text-studio-muted">
                      Timeline nativa XML/EDL sem perdas
                    </span>
                  </div>
                </div>
                <ArrowRight className="w-4 h-4 text-studio-subtle group-hover:text-white transition-transform group-hover:translate-x-0.5" />
              </button>
            </CardContent>

            <CardFooter>
              <div className="w-full flex items-center justify-between text-[11px] text-studio-subtle">
                <span>Versão do Motor: v2.4 PRO</span>
                <span className="text-emerald-400 font-mono">CUDA 12.4 • RTX 4090</span>
              </div>
            </CardFooter>
          </Card>
        </div>

        {/* Pipeline Architecture Explanation */}
        <div className="border border-studio-border bg-studio-card/60 p-5 rounded-2xl space-y-3">
          <div className="flex items-center gap-2">
            <Tv className="w-4 h-4 text-blue-400" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-white">
              Arquitetura de Pré-Edição TravelCut Studio
            </h3>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 text-xs text-studio-muted">
            <div className="bg-studio-panel/60 p-3.5 rounded-xl border border-studio-border/60">
              <span className="font-bold text-white block mb-1">
                1. Organização & Triagem IA
              </span>
              Processa horas de material bruto de viagens, detecta rostos, paisagens e estabiliza trechos tremidos antes de cortar.
            </div>

            <div className="bg-studio-panel/60 p-3.5 rounded-xl border border-studio-border/60">
              <span className="font-bold text-white block mb-1">
                2. Roteiro & Sincronia Musical
              </span>
              Alinha a fala transcrita com a batida musical (BPM) criando um corte preliminar dinâmico e sem fadiga criativa.
            </div>

            <div className="bg-studio-panel/60 p-3.5 rounded-xl border border-studio-border/60">
              <span className="font-bold text-white block mb-1">
                3. Finalização no DaVinci Resolve
              </span>
              Gera arquivos FCPXML/EDL limpos com referências diretas à mídia original em 4K/10-bit para grading e mixagem final.
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
