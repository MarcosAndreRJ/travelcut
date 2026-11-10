import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { ImportJobStatusDto } from '../../../types/ipc';
import { CPU_SLOW_NOTICE_TEXT, ImportProgressOverlay, shouldShowCpuSlowNotice } from '../ImportProgressOverlay';

const job: ImportJobStatusDto = {
  jobId: 'job-1',
  projectId: 'proj-1',
  state: 'completed',
  stage: 'done',
  totalFiles: 1,
  processedFiles: 1,
  bytesTotal: 0,
  bytesCopied: 0,
  currentFile: null,
  speechAnalysis: 'not_requested',
  message: null,
  items: [],
  startedAtUtc: '2026-11-09T10:00:00Z',
  finishedAtUtc: '2026-11-09T10:05:00Z'
};

const render = (cpuFallback: boolean, speechEnabled: boolean): string =>
  renderToStaticMarkup(
    <ImportProgressOverlay
      job={job}
      overlayError={null}
      cancelRequested={false}
      onCancel={() => undefined}
      onClose={() => undefined}
      onOpenLibrary={() => undefined}
      cpuFallback={cpuFallback}
      speechEnabled={speechEnabled}
    />
  );

describe('shouldShowCpuSlowNotice', () => {
  it('só aparece com fala habilitada e Whisper em CPU', () => {
    expect(shouldShowCpuSlowNotice(true, true)).toBe(true);
  });

  it('não aparece sem fala habilitada, sem cpuFallback ou com valores ausentes', () => {
    expect(shouldShowCpuSlowNotice(true, false)).toBe(false);
    expect(shouldShowCpuSlowNotice(false, true)).toBe(false);
    expect(shouldShowCpuSlowNotice(undefined, true)).toBe(false);
    expect(shouldShowCpuSlowNotice(true, undefined)).toBe(false);
  });
});

describe('ImportProgressOverlay aviso de CPU lenta', () => {
  it('mostra o aviso quando a fala está habilitada em CPU', () => {
    expect(render(true, true)).toContain(CPU_SLOW_NOTICE_TEXT);
  });

  it('oculta o aviso quando a fala está desabilitada ou a GPU está disponível', () => {
    expect(render(true, false)).not.toContain(CPU_SLOW_NOTICE_TEXT);
    expect(render(false, true)).not.toContain(CPU_SLOW_NOTICE_TEXT);
  });
});
