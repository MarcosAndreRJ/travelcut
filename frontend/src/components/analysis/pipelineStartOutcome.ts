// Classificação pura do resultado de pipeline.start (sem IPC e sem React).

/** Aviso amigável quando um start encontra análise já em andamento (ex.: reanálise automática após scan). */
export const PIPELINE_IN_PROGRESS_NOTICE = 'Análise já em andamento. Acompanhando a execução atual.';

export const PIPELINE_IN_PROGRESS_CODE = 'PIPELINE_IN_PROGRESS';

/** Duck typing do código estável do host (IpcError.code), sem depender da classe. */
export const isPipelineInProgressError = (err: unknown): boolean =>
  typeof err === 'object' && err !== null && (err as { code?: unknown }).code === PIPELINE_IN_PROGRESS_CODE;
