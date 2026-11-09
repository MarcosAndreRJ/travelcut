import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { ImportJobStatusDto, PostAnalysisStatusDto } from '../../../types/ipc';
import { ImportProgressOverlay, speechAnalysisNotice } from '../ImportProgressOverlay';

const job = (overrides: Partial<ImportJobStatusDto> = {}): ImportJobStatusDto => ({
  jobId: 'job-1',
  projectId: 'proj-1',
  state: 'completed',
  stage: 'done',
  totalFiles: 2,
  processedFiles: 2,
  bytesTotal: 0,
  bytesCopied: 0,
  currentFile: null,
  speechAnalysis: 'not_requested',
  message: null,
  items: [],
  startedAtUtc: '2026-11-09T10:00:00Z',
  finishedAtUtc: '2026-11-09T10:05:00Z',
  ...overrides
});

const post = (overrides: Partial<PostAnalysisStatusDto> = {}): PostAnalysisStatusDto => ({
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
  ...overrides
});

const render = (jobValue: ImportJobStatusDto, postAnalysis: PostAnalysisStatusDto | null = null): string =>
  renderToStaticMarkup(
    <ImportProgressOverlay
      job={jobValue}
      overlayError={null}
      cancelRequested={false}
      onCancel={() => undefined}
      onClose={() => undefined}
      onOpenLibrary={() => undefined}
      postAnalysis={postAnalysis}
    />
  );

describe('ImportProgressOverlay: estados de fala', () => {
  it('mostra fala mesclada como sucesso', () => {
    const html = render(job({ speechAnalysis: 'merged' }));
    expect(html).toContain('mescladas ao projeto');
    expect(html).not.toContain('role="alert"');
  });

  it('mostra falha de fala como alerta sem afirmar transcrição', () => {
    const html = render(job({ speechAnalysis: 'failed' }));
    expect(html).toContain('A análise de fala falhou');
    expect(html).toContain('role="alert"');
  });

  it('mostra fala ignorada e indisponível de forma honesta', () => {
    expect(render(job({ speechAnalysis: 'skipped' }))).toContain('Análise de fala ignorada');
    expect(render(job({ speechAnalysis: 'unavailable' }))).toContain('Nenhuma análise de fala foi feita');
  });

  it('não exibe aviso de fala quando não foi solicitada', () => {
    expect(speechAnalysisNotice('not_requested')).toBeNull();
    expect(render(job({ speechAnalysis: 'not_requested' }))).not.toContain('análise de fala');
  });
});

describe('ImportProgressOverlay: resumo pós-análise', () => {
  it('mostra cortes aplicados, pendências, proxies e falhas quando postAnalysis existe', () => {
    const html = render(job({ speechAnalysis: 'merged' }), post());
    expect(html).toContain('Pós-análise automática');
    expect(html).toContain('Cortes aplicados');
    expect(html).toContain('>3<');
    expect(html).toContain('Pendentes de revisão');
    expect(html).toContain('>2<');
    expect(html).toContain('Proxies na fila');
    expect(html).toContain('>4<');
  });

  it('informa o motivo quando a pós-análise foi ignorada', () => {
    const html = render(job(), post({ state: 'skipped', reason: 'auto_process_disabled' }));
    expect(html).toContain('processamento automático desligado');
    expect(html).not.toContain('Cortes aplicados');
  });

  it('lista códigos de pendência e não exibe bloco sem postAnalysis', () => {
    const html = render(job(), post({ state: 'completed_with_issues', issues: ['PROXY_FAILED:2'] }));
    expect(html).toContain('Concluída com pendências');
    expect(html).toContain('PROXY_FAILED:2');
    expect(render(job())).not.toContain('Pós-análise automática');
  });
});
