import React, { useEffect, useState } from 'react';
import { CheckCircle2, AlertCircle, Clock, Film, Sliders, Sparkles } from 'lucide-react';
import { ipc } from '../services/ipcBridge';
import { EditorialProfileDto, PipelineState, ProjectSummary } from '../types/ipc';
import { Badge, DemoBadge, HypothesisBadge, StateCard, StatTile } from './plan/PlanPrimitives';
import { PendingList } from './plan/PendingList';
import { buildFlowSteps, FlowStep } from './plan/flowProgress';
import { clipsOfSection, sortedSections } from './plan/planModel';
import { PROFILE_FIELD_LABEL, profileValueLabel, roleLabel } from './plan/planFormat';
import { formatDuration, formatTimecode, framesToSeconds } from './plan/planTime';
import { useEditPlan } from './plan/useEditPlan';

interface MontageOverviewViewProps {
  project: ProjectSummary | null;
  /** Leva à Tela 03 (Planejamento). Sem a prop, o botão fica desabilitado. */
  onNavigateToTimeline?: () => void;
  onNavigateToTab?: (tab: any) => void;
}

// Só campos que o planejador realmente aplica. silencePolicy, pace e jokePreservation ainda não
// têm efeito (a preservação de humor está fixa como estrita na análise) — não exibir como ativos.
const PROFILE_FIELDS: (keyof EditorialProfileDto)[] = [
  'zoomStrength',
  'speedBroll',
  'musicPolicy',
  'reviewAmbiguous',
  'autoApplyTrivialCuts'
];

const STEP_ICON: Record<FlowStep['status'], React.ReactNode> = {
  done: <CheckCircle2 className="w-4 h-4" aria-hidden="true" />,
  active: <Clock className="w-4 h-4" aria-hidden="true" />,
  pending: null,
  error: <AlertCircle className="w-4 h-4" aria-hidden="true" />
};

const STEP_CLASS: Record<FlowStep['status'], string> = {
  done: 'bg-emerald-500/20 text-emerald-300 border-emerald-500/40',
  active: 'bg-cyan-500 text-white border-cyan-300 shadow-md shadow-cyan-950',
  pending: 'bg-slate-800 text-slate-400 border-slate-700',
  error: 'bg-rose-500/20 text-rose-300 border-rose-500/40'
};

/** Estado do pipeline consultado por projeto; null quando não há como saber. */
const usePipelineState = (projectId: string | null): PipelineState | null => {
  const [snapshot, setSnapshot] = useState<{ projectId: string; state: PipelineState | null } | null>(null);
  useEffect(() => {
    if (!projectId) return;
    let cancelled = false;
    ipc
      .getPipelineStatus(projectId)
      .then((status) => {
        if (!cancelled) setSnapshot({ projectId, state: status.state });
      })
      .catch(() => {
        if (!cancelled) setSnapshot({ projectId, state: null });
      });
    return () => {
      cancelled = true;
    };
  }, [projectId]);
  return snapshot && snapshot.projectId === projectId ? snapshot.state : null;
};

export const MontageOverviewView: React.FC<MontageOverviewViewProps> = ({ project, onNavigateToTimeline }) => {
  const projectId = project?.projectId ?? null;
  const planState = useEditPlan(projectId, project?.lastModifiedUtc ?? null);
  const pipelineState = usePipelineState(projectId);
  const { plan, validation, status } = planState;

  const goToTimeline = () => onNavigateToTimeline?.();
  const timelineButton = (
    <button
      type="button"
      onClick={goToTimeline}
      disabled={!onNavigateToTimeline}
      className="px-3.5 py-1.5 rounded-xl bg-cyan-600 hover:bg-cyan-500 text-white text-xs font-semibold flex items-center gap-1.5 disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-300"
    >
      <Film className="w-3.5 h-3.5" aria-hidden="true" /> Ir para o planejamento
    </button>
  );

  const renderBody = () => {
    switch (status) {
      case 'no_project':
        return (
          <StateCard tone="blocked" title="Nenhum projeto aberto" description="Abra ou crie um projeto para ver a estrutura da montagem." />
        );
      case 'loading':
        return <StateCard tone="loading" title="Carregando estrutura" description="Lendo o plano mais recente do projeto…" />;
      case 'error':
        return (
          <StateCard
            tone="error"
            title="Não foi possível carregar a montagem"
            description={planState.loadError ?? 'Erro desconhecido ao consultar o host.'}
            action={
              <button
                type="button"
                onClick={planState.reload}
                className="px-3 py-1.5 rounded-xl border border-slate-700 text-xs font-semibold text-slate-200 hover:bg-slate-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
              >
                Tentar novamente
              </button>
            }
          />
        );
      case 'empty':
        return (
          <StateCard
            tone="empty"
            title="Nenhum plano gerado"
            description="A estrutura narrativa aparece depois que o plano for gerado na tela de Planejamento."
            action={timelineButton}
          />
        );
      case 'ready':
        return null;
    }
  };

  const tb = plan?.timeBase ?? null;
  const sections = plan ? sortedSections(plan) : [];
  const steps = project
    ? buildFlowSteps({
        project,
        pipelineState,
        planExists: plan !== null,
        planRevision: plan?.revision ?? null,
        validation
      })
    : [];

  return (
    <div className="flex-1 flex flex-col h-full min-h-0 bg-[#070d18] text-slate-100 font-sans overflow-hidden">
      {/* Cabeçalho */}
      <div className="px-6 py-3 border-b border-slate-800 bg-[#0a1222] flex flex-wrap items-center justify-between gap-3 shrink-0">
        <div className="flex items-center gap-3 min-w-0">
          <Film className="w-5 h-5 text-cyan-400 shrink-0" aria-hidden="true" />
          <h2 className="text-sm font-bold text-white">Visão Geral da Montagem</h2>
          <DemoBadge />
          <span className="text-xs text-slate-400 truncate">{project?.name ?? 'Nenhum projeto aberto'}</span>
        </div>
        {status === 'ready' && timelineButton}
      </div>

      {status === 'ready' && plan && tb && (
        <>
          <div className="px-6 py-3 border-b border-slate-800 bg-[#080e1b] flex flex-wrap gap-2 shrink-0" aria-label="Resumo da montagem">
            <StatTile
              label="Duração da timeline"
              value={formatDuration(framesToSeconds(plan.summary.timelineDurationFrames, tb))}
              hint={formatTimecode(plan.summary.timelineDurationFrames, tb)}
              tone="cyan"
            />
            <StatTile label="Seções" value={String(plan.sections.length)} hint="hipóteses até revisão" />
            <StatTile label="Clipes" value={String(plan.clips.length)} />
            <StatTile
              label="Revisão exibida"
              value={`R${plan.revision}`}
              hint={validation ? (validation.isValid ? 'plano válido' : 'plano com erros') : undefined}
            />
          </div>

          {/* Progresso do fluxo (somente etapas com dados reais) */}
          <div className="px-6 py-3 border-b border-slate-800 bg-[#080e1b] shrink-0 overflow-x-auto" aria-label="Progresso do fluxo">
            <ol className="flex items-center gap-4 min-w-max">
              {steps.map((step, index) => (
                <li key={step.key} className="flex items-center gap-2">
                  <span
                    className={`w-7 h-7 rounded-full border flex items-center justify-center text-xs font-bold ${STEP_CLASS[step.status]}`}
                  >
                    {STEP_ICON[step.status] ?? index + 1}
                  </span>
                  <span className="flex flex-col">
                    <span className="text-xs font-semibold text-slate-200">{step.label}</span>
                    <span className="text-[10px] text-slate-400">{step.detail}</span>
                  </span>
                </li>
              ))}
            </ol>
          </div>

          <div className="flex-1 min-h-0 overflow-y-auto xl:overflow-hidden flex flex-col xl:flex-row gap-4 p-5">
            {/* Estrutura narrativa */}
            <div className="flex-1 min-w-0 xl:overflow-y-auto space-y-3">
              <div>
                <h3 className="text-xs font-bold text-white uppercase tracking-wider">Estrutura narrativa</h3>
                <p className="text-[11px] text-slate-400 mt-0.5">
                  Seções em ordem. Cada seção é uma hipótese do plano até ser revisada.
                </p>
              </div>
              {sections.length === 0 ? (
                <p className="text-xs text-slate-400">Este plano não tem seções.</p>
              ) : (
                <ol className="space-y-3" aria-label="Seções da montagem">
                  {sections.map((section) => {
                    const duration = section.endFrame - section.startFrame;
                    const clipCount = clipsOfSection(plan, section.sectionId).length;
                    return (
                      <li key={section.sectionId} className="p-4 rounded-2xl bg-[#0a1424] border border-slate-800 space-y-2">
                        <div className="flex items-center justify-between gap-2 flex-wrap">
                          <span className="text-[10px] font-mono text-cyan-400">Seção {section.order}</span>
                          <div className="flex items-center gap-1.5">
                            <Badge tone="cyan">{roleLabel(section.role)}</Badge>
                            {section.isHypothesis && <HypothesisBadge />}
                          </div>
                        </div>
                        <h4 className="text-sm font-bold text-white">{section.title}</h4>
                        <p className="text-xs text-slate-300 leading-relaxed">{section.rationale || 'Sem justificativa registrada.'}</p>
                        <div className="flex flex-wrap gap-x-4 gap-y-1 pt-2 border-t border-slate-800/80 text-[11px] text-slate-400">
                          <span>
                            Início <span className="font-mono text-slate-200">{formatTimecode(section.startFrame, tb)}</span>
                          </span>
                          <span>
                            Fim <span className="font-mono text-slate-200">{formatTimecode(section.endFrame, tb)}</span>
                          </span>
                          <span>
                            Duração{' '}
                            <span className="font-mono text-white font-bold">{formatTimecode(duration, tb)}</span>{' '}
                            <span className="text-slate-500">({formatDuration(framesToSeconds(duration, tb))})</span>
                          </span>
                          <span>
                            <span className="font-mono text-slate-200">{clipCount}</span> clipes
                          </span>
                        </div>
                      </li>
                    );
                  })}
                </ol>
              )}
            </div>

            {/* Pendências e perfil */}
            <div className="xl:w-96 shrink-0 space-y-4 xl:overflow-y-auto">
              <PendingList pending={plan.pending} />

              {planState.profile && (
                <section aria-labelledby="montage-profile-title" className="rounded-xl bg-[#0a1222] border border-slate-800 p-4 space-y-2">
                  <div className="flex items-center justify-between">
                    <h3 id="montage-profile-title" className="text-xs font-bold text-white flex items-center gap-1.5">
                      <Sliders className="w-3.5 h-3.5 text-cyan-400" aria-hidden="true" /> Perfil editorial
                    </h3>
                    <span className="text-[10px] font-mono text-slate-500">v{planState.profile.version}</span>
                  </div>
                  <dl className="space-y-1">
                    {PROFILE_FIELDS.map((field) => {
                      const value = planState.profile![field];
                      return (
                        <div key={field} className="flex justify-between gap-3 text-xs">
                          <dt className="text-slate-400">{PROFILE_FIELD_LABEL[field] ?? field}</dt>
                          <dd className="text-slate-100 text-right">
                            {profileValueLabel(field, value as string | boolean)}
                          </dd>
                        </div>
                      );
                    })}
                  </dl>
                  <p className="mt-2 text-[10px] text-slate-500 leading-relaxed">
                    Política de silêncio, ritmo e preservação de piadas ainda não são aplicadas pelo planejador;
                    a preservação de humor segue sempre estrita.
                  </p>
                </section>
              )}

              <p className="text-[10px] text-slate-500 flex items-center gap-1.5">
                <Sparkles className="w-3 h-3" aria-hidden="true" />
                Esta tela não envia nada ao DaVinci Resolve.
              </p>
            </div>
          </div>
        </>
      )}

      {status !== 'ready' && <div className="flex-1 flex flex-col min-h-0">{renderBody()}</div>}
    </div>
  );
};
