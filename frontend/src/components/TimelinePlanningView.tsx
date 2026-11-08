import React, { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, Layers, Send } from 'lucide-react';
import { ProjectSummary } from '../types/ipc';
import { ClipInspector } from './plan/ClipInspector';
import { PlanTimelineCanvas } from './plan/PlanTimelineCanvas';
import { RevisionControls } from './plan/RevisionControls';
import { ValidationPanel } from './plan/ValidationPanel';
import { PlanPreviewPlayer } from './plan/PlanPreviewPlayer';
import {
  ActionError,
  DemoBadge,
  StateCard,
  StatTile
} from './plan/PlanPrimitives';
import { clipsWithMissingSource, findClip, hasPlanOutdated } from './plan/planModel';
import { formatDuration, formatTimecode, framesToSeconds, usToSeconds } from './plan/planTime';
import { useEditPlan } from './plan/useEditPlan';
import { MultitrackPlanningPanel } from './audio/MultitrackPlanningPanel';
import { DEFAULT_TRACK_STATES, type TrackAudioState } from './audio/multitrackModel';

interface TimelinePlanningViewProps {
  project: ProjectSummary | null;
  onNavigateToTab?: (tab: any) => void;
  /** Abre a tela Enviar ao DaVinci (simulação seca e envio). Só aparece quando há plano. */
  onNavigateToExport?: () => void;
}

/**
 * Tela 04 — Planejamento da Timeline (F06). Planejamento e revisão apenas: não é o NLE, não há
 * player real e nada é enviado ao DaVinci. Todos os valores vêm de plan.get/plan.list.
 */
export const TimelinePlanningView: React.FC<TimelinePlanningViewProps> = ({ project, onNavigateToExport }) => {
  const planState = useEditPlan(project?.projectId ?? null, project?.lastModifiedUtc ?? null);
  const { plan, validation, status } = planState;
  const mediaCount = project?.mediaCount ?? 0;
  const [selectedClipId, setSelectedClipId] = useState<string | null>(null);
  const [zoom, setZoom] = useState(1);
  const [currentFrame, setCurrentFrame] = useState<number>(0);
  const planKey = plan ? `${plan.planId}:${plan.revision}` : null;

  // A seleção não sobrevive a troca de revisão ou de projeto.
  useEffect(() => {
    setSelectedClipId(null);
    setCurrentFrame(0);
  }, [planKey, project?.projectId]);

  const selectedClip = plan ? findClip(plan, selectedClipId) : null;
  const summary = plan?.summary ?? null;
  const tb = plan?.timeBase ?? null;
  const outdated = hasPlanOutdated(validation);
  const [showMultitrack, setShowMultitrack] = useState(false);
  const [trackStates, setTrackStates] = useState<Record<string, TrackAudioState>>(DEFAULT_TRACK_STATES);
  const missingClipIds = useMemo(
    () => (plan ? clipsWithMissingSource(plan, validation) : new Set<string>()),
    [plan, validation]
  );

  const renderBody = () => {
    switch (status) {
      case 'no_project':
        return (
          <StateCard
            tone="blocked"
            title="Nenhum projeto ativo"
            description="Abra ou crie um projeto para visualizar ou gerar o plano editorial da timeline."
          />
        );
      case 'loading':
        return (
          <StateCard
            tone="loading"
            title="Carregando plano editorial..."
            description="Lendo metadados, trilhas e clipes do plano no projeto ativo."
          />
        );
      case 'error':
        return (
          <StateCard
            tone="error"
            title="Não foi possível ler o plano editorial"
            description={planState.loadError ?? 'Falha desconhecida ao ler o plano.'}
            action={
              <button
                type="button"
                onClick={planState.reload}
                className="px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-semibold text-slate-200"
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
            description="O plano usa a cronologia das mídias e os cortes aprovados na Revisão. Nada é enviado ao DaVinci."
            action={
              <button
                type="button"
                onClick={planState.generate}
                disabled={planState.busy === 'generate' || mediaCount === 0}
                className="px-4 py-2 rounded-xl bg-gradient-to-r from-cyan-500 to-blue-600 text-xs font-bold text-white disabled:opacity-50"
              >
                {planState.busy === 'generate' ? 'Gerando plano…' : 'Gerar plano'}
              </button>
            }
          />
        );
      default:
        return null;
    }
  };

  return (
    <div className="flex-1 flex flex-col h-full min-h-0 bg-[#070d18] text-slate-100 font-sans overflow-hidden">
      {/* Cabeçalho */}
      <div className="px-6 py-3 border-b border-slate-800 bg-[#0a1222] flex flex-wrap items-center justify-between gap-3 shrink-0">
        <div className="flex items-center gap-3 min-w-0">
          <div className="flex items-center gap-2">
            <Layers className="w-5 h-5 text-cyan-400" aria-hidden="true" />
            <h2 className="text-sm font-bold text-white">Planejamento da Timeline</h2>
          </div>
          <DemoBadge />
          {plan && (
            <span className="text-xs text-slate-400 font-mono">
              Revisão r{plan.revision} · {plan.clips.length} {plan.clips.length === 1 ? 'clipe' : 'clipes'} · {plan.tracks.length} trilhas
            </span>
          )}
        </div>
        {plan && onNavigateToExport && (
          <button
            type="button"
            onClick={onNavigateToExport}
            className="px-3 py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-semibold text-slate-200 flex items-center gap-1.5 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
          >
            <Send className="w-3.5 h-3.5" aria-hidden="true" />
            <span>Enviar ao DaVinci</span>
          </button>
        )}
      </div>

      {status === 'ready' && plan && summary && tb && (
        <>
          {/* Métricas do plano */}
          <div className="grid grid-cols-2 md:grid-cols-6 gap-3 p-4 border-b border-slate-800 bg-[#080d1a] shrink-0">
            <StatTile
              label="Duração da timeline"
              value={formatDuration(framesToSeconds(summary.timelineDurationFrames, tb))}
              hint={formatTimecode(summary.timelineDurationFrames, tb)}
            />
            <StatTile
              label="Removido da fonte"
              value={formatDuration(usToSeconds(summary.removedSourceUs))}
              hint={`de ${formatDuration(usToSeconds(summary.sourceDurationUs))} na fonte`}
            />
            <StatTile label="Cortes aprovados aplicados" value={String(summary.approvedCutsApplied)} />
            <StatTile label="Cortes automáticos" value={String(summary.autoCutsApplied)} />
            <StatTile label="Pendentes" value={String(summary.pendingSuggestions)} hint="sugestões sem decisão" />
            <StatTile label="Mídias excluídas" value={String(summary.excludedMedia)} />
          </div>

          {planState.actionError && <ActionError message={planState.actionError} onDismiss={planState.dismissActionError} />}

          {outdated && (
            <div
              role="status"
              data-testid="plan-outdated-banner"
              className="mx-4 mt-3 flex flex-wrap items-center gap-2 px-3 py-2 rounded-xl bg-amber-950/50 border border-amber-500/30 text-xs text-amber-200"
            >
              <AlertTriangle className="w-4 h-4 shrink-0" aria-hidden="true" />
              <span className="flex-1">Plano desatualizado em relação às decisões — gere nova revisão.</span>
              <button
                type="button"
                onClick={planState.generate}
                disabled={planState.busy !== null || mediaCount === 0}
                className="px-3 py-1 rounded-lg bg-amber-500/20 hover:bg-amber-500/30 border border-amber-500/40 text-[11px] font-semibold text-amber-100 disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
              >
                {planState.busy === 'generate' ? 'Gerando plano…' : 'Gerar nova revisão'}
              </button>
            </div>
          )}

          <div className="flex-1 min-h-0 overflow-y-auto xl:overflow-hidden flex flex-col xl:flex-row gap-3 p-4">
            {/* Coluna de revisões e validação */}
            <div className="xl:w-72 shrink-0 space-y-5 xl:overflow-y-auto rounded-xl border border-slate-800 bg-[#080e1b] p-4">
              <RevisionControls
                revisions={planState.revisions}
                viewedRevision={plan.revision}
                latestRevision={planState.latestRevision ?? plan.revision}
                busy={planState.busy}
                mediaCount={mediaCount}
                onSelect={planState.selectRevision}
                onGenerate={planState.generate}
                onRevert={planState.revert}
              />
              {validation && (
                <ValidationPanel
                  plan={plan}
                  validation={validation}
                  onSelectClip={setSelectedClipId}
                  onRevalidate={planState.validate}
                  revalidating={planState.busy === 'validate'}
                  disabled={planState.busy !== null && planState.busy !== 'validate'}
                />
              )}
            </div>

            {/* Prévia interativa e timeline */}
            <div className="flex-1 min-w-0 flex flex-col gap-3 min-h-[420px]">
              <PlanPreviewPlayer
                plan={plan}
                selectedClip={selectedClip}
                currentFrame={currentFrame}
                onSeekFrame={setCurrentFrame}
              />
              <PlanTimelineCanvas
                plan={plan}
                selectedClipId={selectedClipId}
                onSelectClip={setSelectedClipId}
                zoom={zoom}
                onZoomChange={setZoom}
                currentFrame={currentFrame}
                onSeekFrame={setCurrentFrame}
                missingClipIds={missingClipIds}
              />
            </div>

            {/* Inspetor do clipe */}
            <div className="xl:w-80 shrink-0 rounded-xl border border-slate-800 bg-[#080e1b] xl:overflow-hidden min-h-[240px]">
              <ClipInspector plan={plan} clip={selectedClip} onSelectClip={setSelectedClipId} />
            </div>
          </div>
        </>
      )}

      {status !== 'ready' && (
        <div className="flex-1 flex flex-col min-h-0">
          {planState.actionError && <ActionError message={planState.actionError} onDismiss={planState.dismissActionError} />}
          {renderBody()}
        </div>
      )}
    </div>
  );
};
