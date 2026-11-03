import React, { useEffect, useState } from 'react';
import {
  Plus,
  FolderOpen,
  Cpu,
  Video,
  Scissors,
  Sparkles,
  AlertCircle,
  ArrowRight,
  Plane
} from 'lucide-react';
import { ProjectSummary, EditorCapabilities, PipelineStatusDto, StageState } from '../types/ipc';
import { ipc } from '../services/ipcBridge';
import { EditorBadge, TONE_DOT, TONE_TEXT } from './editor/EditorPrimitives';
import type { EditorTone } from './editor/editorModel';
import { PIPELINE_STATE_LABEL, stageStateLabel } from './analysis/analysisFormat';
import { buildIntegrationRows, type IntegrationRow } from './environment/integrationStatus';
import { useIntegrationStatus } from './environment/useIntegrationStatus';

interface DashboardOverviewViewProps {
  project: ProjectSummary | null;
  editorCaps: EditorCapabilities | null;
  onCreateProject: () => void;
  onOpenProject: () => void;
  onNavigateToCheck: () => void;
  onNavigateToTab: (tab: any) => void;
}

/** Pastas canônicas do projeto (docs/CLAUDE.md). Nomes fixos são a estrutura, não dados. */
const CANONICAL_FOLDERS: { name: string; role: string; badge: string }[] = [
  { name: '1-Bruto', role: 'Mídias originais (somente leitura)', badge: 'Origem' },
  { name: '2-Assets', role: 'Recursos de apoio (somente leitura)', badge: 'Apoio' },
  { name: '3-Trabalho', role: 'Temporários e análises', badge: 'Trabalho' },
  { name: '4-Final', role: 'Somente materiais aprovados', badge: 'Final' }
];

/** Integrações mostradas no painel (ids de buildIntegrationRows). */
const PANEL_INTEGRATION_IDS = ['davinci', 'ffmpeg', 'whisper', 'hyperframes', 'claude'] as const;

const STAGE_TONE: Record<StageState, EditorTone> = {
  pending: 'slate',
  running: 'cyan',
  completed: 'emerald',
  completed_with_issues: 'amber',
  unavailable: 'amber',
  failed: 'rose',
  canceled: 'slate',
  skipped: 'slate'
};

const DemoTag: React.FC = () => (
  <span className="px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-300 border border-amber-500/30 text-[10px] font-bold uppercase tracking-wider">
    DEMONSTRAÇÃO
  </span>
);

export const DashboardOverviewView: React.FC<DashboardOverviewViewProps> = ({
  project,
  editorCaps,
  onCreateProject,
  onOpenProject,
  onNavigateToCheck,
  onNavigateToTab
}) => {
  const isMock = ipc.isMock;
  const status = useIntegrationStatus();
  const rows = buildIntegrationRows({
    isMock,
    analysisEnv: status.analysisEnv,
    analysisError: status.analysisError,
    editorCaps,
    motionCaps: status.motionCaps,
    motionError: status.motionError
  });
  const panelRows = PANEL_INTEGRATION_IDS.map((id) => rows.find((r) => r.id === id)).filter(
    (r): r is IntegrationRow => r !== undefined
  );

  // Pipeline real do projeto aberto (pipeline.status). Sem projeto, nada é consultado.
  const projectId = project?.projectId ?? null;
  const [pipeline, setPipeline] = useState<PipelineStatusDto | null>(null);
  const [pipelineError, setPipelineError] = useState<string | null>(null);
  const [pipelineLoading, setPipelineLoading] = useState<boolean>(false);

  useEffect(() => {
    setPipeline(null);
    setPipelineError(null);
    if (!projectId) {
      setPipelineLoading(false);
      return;
    }
    let cancelled = false;
    setPipelineLoading(true);
    ipc
      .getPipelineStatus(projectId)
      .then((s) => {
        if (!cancelled) setPipeline(s);
      })
      .catch((err: unknown) => {
        console.error('Falha ao consultar pipeline:', err);
        if (!cancelled) setPipelineError(err instanceof Error ? err.message : 'Falha ao consultar o pipeline.');
      })
      .finally(() => {
        if (!cancelled) setPipelineLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [projectId]);

  const pipelineHeaderText = !project
    ? 'Sem projeto aberto'
    : pipelineLoading
      ? 'Consultando…'
      : pipelineError
        ? 'Falha na consulta'
        : pipeline
          ? PIPELINE_STATE_LABEL[pipeline.state] ?? pipeline.state
          : '—';

  return (
    <div className="flex-1 overflow-y-auto bg-[#070d18] text-slate-100 p-8 space-y-8 font-sans">
      {/* Hero Banner Card */}
      <div className="relative rounded-3xl overflow-hidden bg-gradient-to-r from-[#0a1628] via-[#0d223f] to-[#0a1424] border border-cyan-500/30 shadow-2xl p-8 flex flex-col justify-between min-h-[220px]">
        <div
          className="absolute right-0 top-0 bottom-0 w-1/2 opacity-25 bg-cover bg-right mix-blend-screen pointer-events-none rounded-r-3xl"
          style={{ backgroundImage: `url('/assets/8.webp')`, backgroundPosition: 'right 15% center' }}
        />
        <div className="absolute -right-10 -bottom-10 w-96 h-96 rounded-full bg-cyan-500/10 blur-[100px] pointer-events-none" />

        <div className="relative z-10 max-w-2xl">
          <div className="text-[11px] font-bold tracking-widest text-cyan-400 uppercase mb-2">
            TRAVELCUT STUDIO
          </div>
          <h1 className="text-3xl lg:text-4xl font-extrabold text-white tracking-tight mb-3">
            Bem-vindo ao TravelCut <span className="text-cyan-400">Studio</span>
          </h1>
          <p className="text-sm text-slate-300 leading-relaxed max-w-xl">
            O seu estúdio inteligente de edição de vídeos de viagem. Organize suas mídias,
            transcreva automaticamente, identifique os melhores momentos e edite com apoio de IA,
            da ideia ao vídeo final.
          </p>
        </div>

        {project && (
          <div className="relative z-10 mt-6 pt-4 border-t border-slate-700/60 flex items-center justify-between text-xs text-slate-300">
            <div className="flex items-center gap-2">
              Projeto ativo: <span className="font-semibold text-white">{project.name}</span>
              {isMock && <DemoTag />}
            </div>
            <button
              onClick={() => onNavigateToTab('review')}
              className="text-cyan-400 hover:text-cyan-300 font-semibold flex items-center gap-1"
            >
              Ir para Revisão de Cortes
              {typeof project.pendingReviewsCount === 'number' ? ` (${project.pendingReviewsCount} pendentes)` : ''}
              <ArrowRight className="w-3.5 h-3.5" />
            </button>
          </div>
        )}
      </div>

      {/* Main Grid: 3 Quick Action Cards + Integration Status */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-2 space-y-6">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            {/* Card 1: Criar novo projeto (Highlighted) */}
            <div className="rounded-2xl bg-gradient-to-b from-[#0c182c] to-[#091322] border-2 border-cyan-500/70 p-5 flex flex-col justify-between shadow-xl shadow-cyan-950/40 relative overflow-hidden group">
              <div className="absolute top-0 right-0 w-24 h-24 bg-cyan-400/10 rounded-full blur-2xl group-hover:bg-cyan-400/20 transition-all" />
              <div>
                <div className="w-12 h-12 rounded-2xl bg-gradient-to-tr from-cyan-500 to-blue-600 flex items-center justify-center text-white mb-4 shadow-md shadow-cyan-500/30">
                  <Plus className="w-6 h-6 stroke-[2.5]" />
                </div>
                <h3 className="text-base font-bold text-white mb-1.5">
                  Criar novo projeto
                </h3>
                <p className="text-xs text-slate-400 leading-relaxed mb-6">
                  Inicie um projeto do zero, organize suas mídias e comece a edição.
                </p>
              </div>

              <button
                onClick={onCreateProject}
                className="w-full py-2.5 px-4 rounded-xl bg-gradient-to-r from-cyan-500 to-blue-600 hover:from-cyan-400 hover:to-blue-500 text-white font-semibold text-xs transition-all shadow-md shadow-cyan-950 flex items-center justify-center gap-1.5"
              >
                <Plus className="w-4 h-4" /> Novo projeto
              </button>
            </div>

            {/* Card 2: Abrir projeto existente */}
            <div className="rounded-2xl bg-[#0a1222] border border-slate-800 p-5 flex flex-col justify-between hover:border-slate-700 transition-colors">
              <div>
                <div className="w-12 h-12 rounded-2xl bg-emerald-950/60 border border-emerald-500/30 flex items-center justify-center text-emerald-400 mb-4">
                  <FolderOpen className="w-6 h-6" />
                </div>
                <h3 className="text-base font-bold text-white mb-1.5">
                  Abrir projeto existente
                </h3>
                <p className="text-xs text-slate-400 leading-relaxed mb-6">
                  Continue a edição de um projeto já criado no TravelCut Studio.
                </p>
              </div>

              <button
                onClick={onOpenProject}
                className="w-full py-2.5 px-4 rounded-xl bg-slate-800/80 hover:bg-slate-700/80 border border-slate-700 text-slate-200 font-semibold text-xs transition-all flex items-center justify-center gap-1.5"
              >
                Abrir projeto
              </button>
            </div>

            {/* Card 3: Verificar integrações */}
            <div className="rounded-2xl bg-[#0a1222] border border-slate-800 p-5 flex flex-col justify-between hover:border-slate-700 transition-colors">
              <div>
                <div className="w-12 h-12 rounded-2xl bg-indigo-950/60 border border-indigo-500/30 flex items-center justify-center text-indigo-400 mb-4">
                  <Cpu className="w-6 h-6" />
                </div>
                <h3 className="text-base font-bold text-white mb-1.5">
                  Verificar integrações
                </h3>
                <p className="text-xs text-slate-400 leading-relaxed mb-6">
                  Confira e configure as ferramentas essenciais (DaVinci, Whisper, FFmpeg e Claude).
                </p>
              </div>

              <button
                onClick={onNavigateToCheck}
                className="w-full py-2.5 px-4 rounded-xl bg-slate-800/80 hover:bg-slate-700/80 border border-slate-700 text-slate-200 font-semibold text-xs transition-all flex items-center justify-center gap-1.5"
              >
                Verificar integrações
              </button>
            </div>
          </div>

          {/* Section: O que você pode fazer aqui? */}
          <div className="pt-2">
            <h3 className="text-sm font-bold text-slate-200 mb-4">
              O que você pode fazer aqui?
            </h3>
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <div
                onClick={() => onNavigateToTab('transcription')}
                className="p-4 rounded-2xl bg-[#0a1222]/80 border border-slate-800/80 hover:border-cyan-500/40 cursor-pointer transition-all flex items-start gap-3.5"
              >
                <div className="w-9 h-9 rounded-xl bg-cyan-950/80 border border-cyan-500/30 text-cyan-400 flex items-center justify-center shrink-0">
                  <Video className="w-4 h-4" />
                </div>
                <div>
                  <h4 className="text-xs font-bold text-white">Analisar e transcrever vídeos</h4>
                  <p className="text-[11px] text-slate-400 mt-1">Com IA (Whisper) e detecção de destaques.</p>
                </div>
              </div>

              <div
                onClick={() => onNavigateToTab('review')}
                className="p-4 rounded-2xl bg-[#0a1222]/80 border border-slate-800/80 hover:border-cyan-500/40 cursor-pointer transition-all flex items-start gap-3.5"
              >
                <div className="w-9 h-9 rounded-xl bg-blue-950/80 border border-blue-500/30 text-blue-400 flex items-center justify-center shrink-0">
                  <Scissors className="w-4 h-4" />
                </div>
                <div>
                  <h4 className="text-xs font-bold text-white">Editar com mais agilidade</h4>
                  <p className="text-[11px] text-slate-400 mt-1">Sugestões de cortes, narrativa e timeline.</p>
                </div>
              </div>

              <div
                onClick={() => onNavigateToTab('export')}
                className="p-4 rounded-2xl bg-[#0a1222]/80 border border-slate-800/80 hover:border-cyan-500/40 cursor-pointer transition-all flex items-start gap-3.5"
              >
                <div className="w-9 h-9 rounded-xl bg-indigo-950/80 border border-indigo-500/30 text-indigo-400 flex items-center justify-center shrink-0">
                  <Sparkles className="w-4 h-4" />
                </div>
                <div>
                  <h4 className="text-xs font-bold text-white">Finalizar para publicação</h4>
                  <p className="text-[11px] text-slate-400 mt-1">Com áudio, animações e exportação otimizada.</p>
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Coluna direita: status real das integrações */}
        <div className="rounded-2xl bg-[#0a1222] border border-slate-800 p-6 flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between mb-1.5 gap-2">
              <h3 className="text-sm font-bold text-white">
                Status das integrações
              </h3>
              {isMock && <DemoTag />}
            </div>
            <p className="text-xs text-slate-400 mb-6">
              Leitura do host. &quot;Não verificado&quot; significa que não houve resposta real nesta tela.
            </p>

            <div className="space-y-3.5">
              {panelRows.map((item) => (
                <div
                  key={item.id}
                  className="flex items-center justify-between gap-2 p-3 rounded-xl bg-[#080d19] border border-slate-800/80"
                >
                  <div className="flex items-center gap-2.5 min-w-0">
                    <div className={`w-2 h-2 rounded-full shrink-0 ${TONE_DOT[item.tone]}`} />
                    <span className="text-xs font-medium text-slate-200 truncate" title={item.name}>
                      {item.name}
                    </span>
                  </div>
                  <EditorBadge badge={{ label: item.label, tone: item.tone }} className={TONE_TEXT[item.tone]} />
                </div>
              ))}
            </div>
          </div>

          <button
            onClick={onNavigateToCheck}
            className="w-full mt-6 py-2.5 px-4 rounded-xl bg-slate-800 hover:bg-slate-700 text-cyan-300 font-semibold text-xs transition-all flex items-center justify-center gap-1.5"
          >
            Verificar todas as ferramentas <ArrowRight className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* Pastas canônicas do projeto (estado real) */}
      <div className="space-y-4">
        <div className="flex items-center justify-between gap-4">
          <div>
            <h3 className="text-sm font-bold text-white flex items-center gap-2">
              Estrutura de Pastas do Projeto
              <span className="text-[10px] uppercase font-bold tracking-wider px-2 py-0.5 rounded bg-cyan-950 text-cyan-400 border border-cyan-500/30">
                Padrão TravelCut
              </span>
              {isMock && <DemoTag />}
            </h3>
            <p className="text-xs text-slate-400">Pastas canônicas do projeto aberto</p>
          </div>
          <span className="text-xs text-slate-400 font-mono truncate" title={project?.rootPath ?? undefined}>
            {project?.rootPath ?? 'Nenhum projeto aberto'}
          </span>
        </div>

        {!project ? (
          <div className="p-5 rounded-2xl bg-[#0a1222] border border-dashed border-slate-700 text-xs text-slate-400">
            Nenhum projeto aberto. Crie ou abra um projeto para ver as pastas e os contadores reais.
          </div>
        ) : (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            {CANONICAL_FOLDERS.map((folder) => {
              const present = project.fourFoldersValid;
              const missing = !present && project.missingFolder === folder.name;
              const label = present ? 'Presente' : missing ? 'Ausente' : 'Não verificado';
              const tone: EditorTone = present ? 'emerald' : missing ? 'rose' : 'slate';
              return (
                <div
                  key={folder.name}
                  className="p-4 rounded-2xl bg-[#0a1222] border border-slate-800 hover:border-cyan-500/40 transition-all flex flex-col justify-between"
                >
                  <div className="flex items-center justify-between mb-3 gap-2">
                    <span className="text-xs font-mono font-bold text-cyan-400">{folder.name}</span>
                    <span className="text-[10px] px-2 py-0.5 rounded bg-slate-800 text-slate-300 font-semibold">
                      {folder.badge}
                    </span>
                  </div>
                  <p className="text-xs text-slate-300 font-medium mb-4">{folder.role}</p>
                  <div className="flex items-center justify-between text-[11px] text-slate-400 pt-2 border-t border-slate-800/80 gap-2">
                    <EditorBadge badge={{ label, tone }} />
                    {folder.name === '1-Bruto' && <span>{project.mediaCount} mídias registradas</span>}
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Pipeline de análise: etapas reais do host */}
      <div className="p-6 rounded-2xl bg-[#0a1222] border border-slate-800 space-y-4">
        <div className="flex items-center justify-between gap-4">
          <h3 className="text-sm font-bold text-white flex items-center gap-2">
            Pipeline de análise
            {isMock && <DemoTag />}
          </h3>
          <span className="text-xs text-cyan-400 font-medium">{pipelineHeaderText}</span>
        </div>

        {!project ? (
          <p className="text-xs text-slate-400">Abra ou crie um projeto para ver o estado real do pipeline.</p>
        ) : pipelineLoading ? (
          <p className="text-xs text-slate-400">Consultando o pipeline no host…</p>
        ) : pipelineError ? (
          <p className="text-xs text-rose-300 flex items-center gap-1.5">
            <AlertCircle className="w-3.5 h-3.5" /> Não foi possível consultar o pipeline: {pipelineError}
          </p>
        ) : pipeline && pipeline.stages.length > 0 ? (
          <div className="grid grid-cols-1 md:grid-cols-5 gap-3">
            {pipeline.stages.map((stage) => {
              const tone = STAGE_TONE[stage.state] ?? 'slate';
              return (
                <div key={stage.stage} className="p-3 rounded-xl bg-[#080d19] border border-slate-800 text-xs space-y-1.5">
                  <div className="text-slate-200 font-bold flex items-center gap-1.5">
                    <span className={`w-1.5 h-1.5 rounded-full ${TONE_DOT[tone]}`} />
                    {stage.label}
                  </div>
                  <EditorBadge badge={{ label: stageStateLabel(stage.state), tone }} />
                  {stage.totalItems > 0 && (
                    <p className="text-[11px] text-slate-400">
                      {stage.processedItems}/{stage.totalItems} itens
                    </p>
                  )}
                  {stage.reason && <p className="text-[11px] text-slate-500">{stage.reason}</p>}
                </div>
              );
            })}
          </div>
        ) : (
          <p className="text-xs text-slate-400">O host não retornou etapas para este projeto.</p>
        )}
      </div>

      {/* Decorative Bottom Travel Route Illustration */}
      <div className="relative pt-4 flex items-center justify-center opacity-40">
        <div className="flex items-center gap-4 text-xs font-mono text-cyan-400">
          <span>Origem (Bruto)</span>
          <div className="w-24 border-b border-dashed border-cyan-500/60" />
          <Plane className="w-4 h-4 text-cyan-400" />
          <div className="w-24 border-b border-dashed border-cyan-500/60" />
          <span>Destino (DaVinci Timeline)</span>
        </div>
      </div>
    </div>
  );
};
