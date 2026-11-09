import { describe, expect, it } from 'vitest';
import { isPipelineInProgressError, PIPELINE_IN_PROGRESS_NOTICE } from '../pipelineStartOutcome';

describe('isPipelineInProgressError', () => {
  it('reconhece o código estável PIPELINE_IN_PROGRESS', () => {
    const err = Object.assign(new Error('busy'), { code: 'PIPELINE_IN_PROGRESS' });
    expect(isPipelineInProgressError(err)).toBe(true);
    expect(isPipelineInProgressError({ code: 'PIPELINE_IN_PROGRESS' })).toBe(true);
  });

  it('outros erros e valores não são tratados como em andamento', () => {
    expect(isPipelineInProgressError(new Error('falha de rede'))).toBe(false);
    expect(isPipelineInProgressError(Object.assign(new Error('x'), { code: 'OTHER' }))).toBe(false);
    expect(isPipelineInProgressError(null)).toBe(false);
    expect(isPipelineInProgressError('PIPELINE_IN_PROGRESS')).toBe(false);
  });

  it('aviso amigável cita "Análise já em andamento"', () => {
    expect(PIPELINE_IN_PROGRESS_NOTICE).toContain('Análise já em andamento');
  });
});
