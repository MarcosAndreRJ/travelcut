import { describe, expect, it } from 'vitest';
import type { PipelineCompletionDto, PipelineStatusDto, PostAnalysisStatusDto } from '../../types/ipc';
import {
  completionNotice,
  postAnalysisSummary,
  shouldNotifyCompletion,
  shouldPollCompletion
} from '../pipelineCompletion';

const completion = (over: Partial<PipelineCompletionDto> = {}): PipelineCompletionDto => ({
  settled: true,
  pendingBatches: 0,
  token: 'tok-2',
  outcome: 'completed',
  ...over
});

const post = (over: Partial<PostAnalysisStatusDto> = {}): PostAnalysisStatusDto => ({
  state: 'completed',
  stage: null,
  reason: null,
  autoCutsApplied: 3,
  pendingReview: 2,
  proxiesQueued: 4,
  failures: 0,
  issues: [],
  planRevision: 1,
  startedAtUtc: null,
  finishedAtUtc: null,
  ...over
});

const status = (over: Partial<PipelineStatusDto> = {}): PipelineStatusDto =>
  ({
    jobId: null,
    projectId: 'p1',
    state: 'completed',
    stages: [],
    media: [],
    currentMedia: null,
    environment: null,
    startedAtUtc: null,
    finishedAtUtc: null,
    ...over
  }) as PipelineStatusDto;

describe('shouldNotifyCompletion', () => {
  it('primeira leitura ao abrir o app nunca notifica, mesmo com token novo e assentado', () => {
    expect(shouldNotifyCompletion(undefined, completion())).toBe(false);
  });

  it('token nulo nunca notifica', () => {
    expect(shouldNotifyCompletion('tok-1', completion({ token: null }))).toBe(false);
    expect(shouldNotifyCompletion(null, completion({ token: null }))).toBe(false);
  });

  it('notifica quando o token muda para um valor novo e não nulo com ciclo assentado', () => {
    expect(shouldNotifyCompletion('tok-1', completion({ token: 'tok-2' }))).toBe(true);
    expect(shouldNotifyCompletion(null, completion({ token: 'tok-2' }))).toBe(true);
  });

  it('não repete notificação quando o token é o mesmo', () => {
    expect(shouldNotifyCompletion('tok-2', completion({ token: 'tok-2' }))).toBe(false);
  });

  it('não notifica enquanto o ciclo não assentou', () => {
    expect(shouldNotifyCompletion('tok-1', completion({ settled: false }))).toBe(false);
  });

  it('ausência de completion (host antigo) não notifica', () => {
    expect(shouldNotifyCompletion('tok-1', null)).toBe(false);
    expect(shouldNotifyCompletion('tok-1', undefined)).toBe(false);
  });
});

describe('shouldPollCompletion', () => {
  it('sem status não consulta', () => {
    expect(shouldPollCompletion(null)).toBe(false);
  });

  it('job ativo mantém o polling', () => {
    expect(shouldPollCompletion(status({ state: 'running' }))).toBe(true);
    expect(shouldPollCompletion(status({ state: 'queued' }))).toBe(true);
  });

  it('lote pendente ou ciclo não assentado mantém o polling mesmo com job parado', () => {
    expect(shouldPollCompletion(status({ completion: completion({ pendingBatches: 2 }) }))).toBe(true);
    expect(shouldPollCompletion(status({ completion: completion({ settled: false }) }))).toBe(true);
  });

  it('ocioso e assentado encerra o polling', () => {
    expect(shouldPollCompletion(status({ state: 'idle', completion: completion() }))).toBe(false);
    expect(shouldPollCompletion(status({ state: 'completed' }))).toBe(false);
  });
});

describe('postAnalysisSummary e completionNotice', () => {
  it('resume cortes, pendências e proxies quando a pós-análise existe', () => {
    expect(postAnalysisSummary(post())).toBe('3 cortes aplicados, 2 pendentes de revisão, 4 proxies na fila.');
  });

  it('sem pós-análise ou ignorada/ociosa não há resumo', () => {
    expect(postAnalysisSummary(null)).toBeNull();
    expect(postAnalysisSummary(undefined)).toBeNull();
    expect(postAnalysisSummary(post({ state: 'skipped', reason: 'auto_process_disabled' }))).toBeNull();
    expect(postAnalysisSummary(post({ state: 'idle' }))).toBeNull();
  });

  it('concluído com resumo usa sucesso e inclui o resumo', () => {
    const notice = completionNotice('completed', post());
    expect(notice.type).toBe('success');
    expect(notice.message).toContain('3 cortes aplicados');
  });

  it('falha usa erro e mantém o resumo quando existe', () => {
    expect(completionNotice('failed', null)).toMatchObject({ type: 'error' });
    expect(completionNotice('failed', post({ state: 'failed' })).message).toContain('Ocorreu uma falha.');
  });

  it('cancelado e pendências usam tipo informativo (sem som de sucesso)', () => {
    expect(completionNotice('canceled', null).type).toBe('info');
    expect(completionNotice('completed_with_issues', post({ state: 'completed_with_issues' })).type).toBe('info');
  });

  it('resultado desconhecido ainda gera aviso informativo', () => {
    expect(completionNotice(null, null)).toMatchObject({ type: 'info', title: 'Processamento finalizado' });
  });
});
