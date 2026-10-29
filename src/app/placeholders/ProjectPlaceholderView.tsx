import React from 'react';
import {
  FolderOpen,
  FolderPlus,
  HardDrive,
  Camera,
  Film,
  Calendar,
  Layers,
  FileCheck,
  CheckCircle2,
  Tv
} from 'lucide-react';
import { PageHeader } from '../../components/ui/PageHeader';
import { Card, CardHeader, CardTitle, CardContent, CardDescription } from '../../components/ui/Card';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { ProgressBar } from '../../components/ui/ProgressBar';

export interface ProjectPlaceholderViewProps {
  projectName?: string;
  onNavigateToTab?: (tab: string) => void;
}

export const ProjectPlaceholderView: React.FC<ProjectPlaceholderViewProps> = ({
  projectName = 'Japão 2026 - Tóquio & Kyoto 4K',
  onNavigateToTab
}) => {
  return (
    <div className="flex-1 flex flex-col overflow-y-auto">
      <PageHeader
        title="Configuração do Projeto & Mídias"
        subtitle="Gerenciamento de repositório bruto, perfis de câmera e estrutura de pastas de viagem"
        badge={
          <Badge variant="primary" size="sm" withDot>
            Projeto Ativo
          </Badge>
        }
        actions={
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              leftIcon={<FolderPlus className="w-3.5 h-3.5 text-blue-400" />}
            >
              Vincular Pasta
            </Button>
            <Button
              variant="primary"
              size="sm"
              leftIcon={<Film className="w-3.5 h-3.5" />}
              onClick={() => onNavigateToTab?.('analysis')}
            >
              Explorar Mídias
            </Button>
          </div>
        }
      />

      <div className="p-6 max-w-7xl mx-auto w-full space-y-6">
        {/* Project Header Info Card */}
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">
          <Card variant="elevated" className="lg:col-span-2 border-studio-border">
            <CardHeader>
              <CardTitle>
                <FolderOpen className="w-4 h-4 text-blue-400" />
                <span>Estrutura do Projeto</span>
              </CardTitle>
              <span className="text-xs text-studio-subtle font-mono">ID: PROJ-JP-2026</span>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="bg-studio-panel/70 p-4 rounded-xl border border-studio-border/70 space-y-2">
                <div className="flex items-center justify-between">
                  <span className="text-xs text-studio-muted font-medium">Nome do Projeto:</span>
                  <span className="text-sm font-bold text-white">{projectName}</span>
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-xs text-studio-muted font-medium">Diretório Raiz:</span>
                  <span className="text-xs font-mono text-studio-subtle bg-studio-bg px-2 py-0.5 rounded border border-studio-border/60">
                    D:/Videos/Viagens/2026_Japao_4K_SonyFX3
                  </span>
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-xs text-studio-muted font-medium">Destino de Exportação DaVinci:</span>
                  <span className="text-xs font-mono text-studio-subtle bg-studio-bg px-2 py-0.5 rounded border border-studio-border/60">
                    D:/Videos/Viagens/2026_Japao_4K_SonyFX3/DaVinci_Exports/
                  </span>
                </div>
              </div>

              {/* Cameras & Equipment Ingested */}
              <div className="pt-2">
                <span className="text-xs font-bold text-white uppercase tracking-wider block mb-2.5">
                  Câmeras e Fontes Detectadas no Projeto
                </span>
                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                  <div className="bg-studio-panel/60 p-3 rounded-xl border border-studio-border/60 flex items-center gap-3">
                    <div className="w-8 h-8 rounded-lg bg-blue-600/15 border border-blue-500/30 flex items-center justify-center text-blue-400 shrink-0">
                      <Camera className="w-4 h-4" />
                    </div>
                    <div className="min-w-0">
                      <span className="text-xs font-bold text-white block truncate">Sony FX3</span>
                      <span className="text-[10px] text-studio-muted font-mono">S-Log3 • 4K 60p</span>
                    </div>
                  </div>

                  <div className="bg-studio-panel/60 p-3 rounded-xl border border-studio-border/60 flex items-center gap-3">
                    <div className="w-8 h-8 rounded-lg bg-cyan-600/15 border border-cyan-500/30 flex items-center justify-center text-cyan-400 shrink-0">
                      <Film className="w-4 h-4" />
                    </div>
                    <div className="min-w-0">
                      <span className="text-xs font-bold text-white block truncate">DJI Mini 4 Pro</span>
                      <span className="text-[10px] text-studio-muted font-mono">D-Log M • 4K 60p</span>
                    </div>
                  </div>

                  <div className="bg-studio-panel/60 p-3 rounded-xl border border-studio-border/60 flex items-center gap-3">
                    <div className="w-8 h-8 rounded-lg bg-emerald-600/15 border border-emerald-500/30 flex items-center justify-center text-emerald-400 shrink-0">
                      <Layers className="w-4 h-4" />
                    </div>
                    <div className="min-w-0">
                      <span className="text-xs font-bold text-white block truncate">iPhone 15 Pro</span>
                      <span className="text-[10px] text-studio-muted font-mono">Apple ProRes Log</span>
                    </div>
                  </div>
                </div>
              </div>
            </CardContent>
          </Card>

          {/* Storage & Proxies */}
          <Card variant="elevated" className="border-studio-border flex flex-col justify-between">
            <CardHeader>
              <CardTitle>Armazenamento & Proxies</CardTitle>
              <CardDescription>Uso de disco no SSD NVMe rápido</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div>
                <div className="flex items-center justify-between text-xs mb-1.5">
                  <span className="text-studio-muted flex items-center gap-1.5">
                    <HardDrive className="w-3.5 h-3.5 text-blue-400" />
                    Volume D: (SSD NVMe)
                  </span>
                  <span className="font-mono text-white font-bold">142 GB / 2 TB</span>
                </div>
                <ProgressBar value={28} variant="primary" size="sm" />
              </div>

              <div className="bg-studio-panel/50 p-3 rounded-xl border border-studio-border/60 space-y-2 text-xs">
                <div className="flex items-center justify-between text-studio-muted">
                  <span>Mídia Bruta (Original):</span>
                  <span className="text-white font-mono font-semibold">128.4 GB</span>
                </div>
                <div className="flex items-center justify-between text-studio-muted">
                  <span>Proxies ProRes Proxy:</span>
                  <span className="text-white font-mono font-semibold">13.6 GB</span>
                </div>
                <div className="flex items-center justify-between text-studio-muted">
                  <span>Cache de Formas de Onda:</span>
                  <span className="text-white font-mono font-semibold">120 MB</span>
                </div>
              </div>

              <div className="flex items-center gap-2 text-xs text-emerald-400 font-medium">
                <CheckCircle2 className="w-4 h-4 shrink-0" />
                <span>Proxies em 1080p gerados para todos os clipes</span>
              </div>
            </CardContent>
          </Card>
        </div>

        {/* DaVinci Resolve Handshake Card */}
        <div className="bg-studio-card border border-studio-border p-5 rounded-2xl flex items-center justify-between gap-4">
          <div className="flex items-center gap-3.5">
            <div className="w-10 h-10 rounded-xl bg-purple-600/15 border border-purple-500/30 flex items-center justify-center text-purple-400 shrink-0">
              <Tv className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-sm font-bold text-white">
                Espaço de Cor DaVinci YRGB Color Managed
              </h3>
              <p className="text-xs text-studio-muted mt-0.5">
                Os metadados de cor de cada clipe (Log, Gama e ISO) estão configurados para transferência automática para a página Color do DaVinci.
              </p>
            </div>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => onNavigateToTab?.('export')}
          >
            Ver Parâmetros DaVinci
          </Button>
        </div>
      </div>
    </div>
  );
};
