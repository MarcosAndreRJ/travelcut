import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { AnalysisEnvironmentDto } from '../../../types/ipc';
import { OptionsCard } from '../ImportSidePanels';
import {
  DEFAULT_IMPORT_OPTIONS,
  effectiveImportOptions,
  importToggleLocks,
  resolveAnalysisCapabilities
} from '../importUtils';

const env = (overrides: Partial<AnalysisEnvironmentDto> = {}): AnalysisEnvironmentDto => ({
  ffmpegAvailable: true,
  ffmpegVersion: '7.0',
  pythonAvailable: true,
  whisperAvailable: true,
  whisperEngine: 'faster-whisper',
  whisperModel: 'small',
  cudaAvailable: true,
  reasons: [],
  device: 'cuda',
  modelCached: true,
  cpuFallback: false,
  whisperUnavailableReason: null,
  ...overrides
});

const renderOptionsCard = (environment: AnalysisEnvironmentDto | null, environmentError = false): string => {
  const caps = resolveAnalysisCapabilities(environment, environmentError);
  const effective = effectiveImportOptions(DEFAULT_IMPORT_OPTIONS, caps);
  return renderToStaticMarkup(
    <OptionsCard
      options={effective}
      onChange={() => undefined}
      locks={importToggleLocks(effective, caps)}
      cpuFallback={caps.cpuFallback}
    />
  );
};

/** Tag <button> do toggle identificado por id (atributos não contêm '>'). */
const buttonFor = (html: string, id: string): string => html.match(new RegExp(`<button[^>]*id="${id}"[^>]*>`))?.[0] ?? '';

describe('toggles de análise dependentes do ambiente', () => {
  it('desabilita o toggle de transcrição e explica o motivo sem FFmpeg', () => {
    const html = renderOptionsCard(env({ ffmpegAvailable: false }));
    expect(buttonFor(html, 'import-opt-detectSpeech')).toContain('disabled=""');
    expect(buttonFor(html, 'import-opt-generateThumbnails')).toContain('disabled=""');
    expect(buttonFor(html, 'import-opt-analyzeMetadata')).toContain('disabled=""');
    expect(html).toContain('FFmpeg não encontrado');
  });

  it('mantém miniaturas e metadados disponíveis quando só o Whisper falta', () => {
    const html = renderOptionsCard(env({ whisperAvailable: false, whisperUnavailableReason: 'faster-whisper ausente' }));
    expect(buttonFor(html, 'import-opt-detectSpeech')).toContain('disabled=""');
    expect(html).toContain('faster-whisper ausente');
    expect(buttonFor(html, 'import-opt-generateThumbnails')).not.toContain('disabled=""');
  });

  it('não bloqueia nenhum toggle quando o ambiente está completo', () => {
    const html = renderOptionsCard(env());
    expect(buttonFor(html, 'import-opt-detectSpeech')).not.toContain('disabled=""');
    expect(buttonFor(html, 'import-opt-generateThumbnails')).not.toContain('disabled=""');
  });

  it('bloqueia a análise enquanto o ambiente não foi lido', () => {
    const caps = resolveAnalysisCapabilities(null, false);
    expect(caps.ready).toBe(false);
    expect(caps.blocked.detectSpeech).toContain('Verificando');
    const failed = resolveAnalysisCapabilities(null, true);
    expect(failed.blocked.generateThumbnails).toContain('Não foi possível');
  });

  it('exibe aviso discreto de CPU somente quando Whisper está ativo em cpuFallback', () => {
    expect(renderOptionsCard(env({ cpuFallback: true, device: 'cpu' }))).toContain('sem GPU CUDA');
    expect(renderOptionsCard(env({ cpuFallback: true, whisperAvailable: false }))).not.toContain('sem GPU CUDA');
  });
});

describe('rotulo e dependencia de metadados', () => {
  it('usa o rótulo "Transcrever e analisar automaticamente" no lugar do antigo', () => {
    const html = renderOptionsCard(env());
    expect(html).toContain('Transcrever e analisar automaticamente');
    expect(html).not.toContain('Detectar cenas com fala');
  });

  it('força metadados quando fala ou miniaturas estão ligadas', () => {
    const caps = resolveAnalysisCapabilities(env(), false);
    const forced = effectiveImportOptions({ ...DEFAULT_IMPORT_OPTIONS, analyzeMetadata: false }, caps);
    expect(forced.analyzeMetadata).toBe(true);
    expect(importToggleLocks(forced, caps).analyzeMetadata).toContain('Obrigatória');

    const offOnly = effectiveImportOptions(
      { ...DEFAULT_IMPORT_OPTIONS, analyzeMetadata: false, generateThumbnails: false, detectSpeech: false },
      caps
    );
    expect(offOnly.analyzeMetadata).toBe(false);
    expect(importToggleLocks(offOnly, caps).analyzeMetadata).toBeUndefined();
  });

  it('envia false para opções bloqueadas mesmo se o usuário as tiver marcado', () => {
    const caps = resolveAnalysisCapabilities(env({ ffmpegAvailable: false }), false);
    const effective = effectiveImportOptions(DEFAULT_IMPORT_OPTIONS, caps);
    expect(effective.detectSpeech).toBe(false);
    expect(effective.generateThumbnails).toBe(false);
    expect(effective.analyzeMetadata).toBe(false);
    expect(effective.copyFiles).toBe(true);
  });
});
