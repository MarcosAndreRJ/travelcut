import type { PipelineState, PlanValidationDto, ProjectSummary } from '../../types/ipc';

/**
 * Progresso do fluxo derivado de dados reais. Uma etapa só aparece quando há como saber o estado:
 * sem informação (ex.: pipeline ainda não consultado) ela é omitida, nunca inventada.
 */
export type FlowStepStatus = 'done' | 'active' | 'pending' | 'error';

export interface FlowStep {
  key: 'media' | 'analysis' | 'review' | 'plan';
  label: string;
  status: FlowStepStatus;
  detail: string;
}

export interface FlowInput {
  project: ProjectSummary;
  /** null = estado do pipeline desconhecido (não consultado ou falhou). */
  pipelineState: PipelineState | null;
  planExists: boolean;
  planRevision: number | null;
  validation: PlanValidationDto | null;
}

const plural = (n: number, singular: string, pluralForm: string): string => `${n} ${n === 1 ? singular : pluralForm}`;

export const buildFlowSteps = (input: FlowInput): FlowStep[] => {
  const { project } = input;
  const steps: FlowStep[] = [];

  steps.push({
    key: 'media',
    label: 'Mídia importada',
    status: project.mediaCount > 0 ? 'done' : 'pending',
    detail: project.mediaCount > 0 ? plural(project.mediaCount, 'mídia no projeto', 'mídias no projeto') : 'Nenhuma mídia no projeto'
  });

  const pipeline = input.pipelineState;
  if (pipeline !== null && pipeline !== 'idle') {
    const analysis: Omit<FlowStep, 'key' | 'label'> =
      pipeline === 'running' || pipeline === 'queued'
        ? { status: 'active', detail: 'Em andamento' }
        : pipeline === 'completed'
          ? { status: 'done', detail: 'Concluída' }
          : pipeline === 'completed_with_issues'
            ? { status: 'done', detail: 'Concluída com pendências' }
            : pipeline === 'failed'
              ? { status: 'error', detail: 'Falhou' }
              : { status: 'pending', detail: 'Cancelada' };
    steps.push({ key: 'analysis', label: 'Análise de mídia', ...analysis });
  }

  // Revisão de sugestões só entra quando o projeto informa o contador de pendências.
  if (project.pendingReviewsCount !== undefined) {
    const pendingReviews = project.pendingReviewsCount;
    const suggestions = project.suggestionsCount;
    const review: Omit<FlowStep, 'key' | 'label'> =
      suggestions === 0
        ? { status: 'pending', detail: 'Nenhuma sugestão gerada' }
        : pendingReviews > 0
          ? { status: 'active', detail: `${pendingReviews} de ${plural(suggestions, 'sugestão', 'sugestões')} pendente(s)` }
          : { status: 'done', detail: `${plural(suggestions, 'sugestão revisada', 'sugestões revisadas')}` };
    steps.push({ key: 'review', label: 'Sugestões revisadas', ...review });
  }

  const plan: Omit<FlowStep, 'key' | 'label'> = !input.planExists
    ? { status: 'pending', detail: 'Plano não gerado' }
    : input.validation === null
      ? { status: 'pending', detail: `Revisão R${input.planRevision ?? '?'} sem validação` }
      : input.validation.isValid
        ? { status: 'done', detail: `Válido (R${input.planRevision ?? '?'})` }
        : {
            status: 'error',
            detail: `${input.validation.issues.filter((i) => i.severity === 'error').length} erro(s) em R${input.planRevision ?? '?'}`
          };
  steps.push({ key: 'plan', label: 'Plano válido', ...plan });

  return steps;
};
