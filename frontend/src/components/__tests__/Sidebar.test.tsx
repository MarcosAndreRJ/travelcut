import { renderToString } from 'react-dom/server';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Sidebar } from '../Sidebar';
import { ipc } from '../../services/ipcBridge';
import { integrationStatusStore } from '../../services/integrationStatusStore';
import type { AnalysisEnvironmentDto } from '../../types/ipc';

const env = (over: Partial<AnalysisEnvironmentDto> = {}): AnalysisEnvironmentDto =>
  ({ pythonAvailable: true, whisperAvailable: true, whisperModel: 'large-v3', ffmpegAvailable: true, cudaAvailable: false, ...over }) as AnalysisEnvironmentDto;

const render = (): string => renderToString(<Sidebar currentTab="overview" onSelectTab={() => undefined} editorCaps={null} />);

describe('Sidebar com o estado autoritativo', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.spyOn(ipc, 'getMotionCapabilities').mockRejectedValue(new Error('sem motion'));
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('mostra o mesmo resultado da consulta feita por outra tela e o horário da verificação', async () => {
    vi.spyOn(ipc, 'getAnalysisEnvironment').mockResolvedValue(env());
    await integrationStatusStore.refresh();

    const html = render();

    expect(html).toContain('Whisper large-v3');
    expect(html).toContain('Disponível');
    expect(html).toContain('Verificado às');
    expect(html).not.toContain('Falha na consulta');
  });

  it('falha posterior mantém a leitura válida como desatualizada', async () => {
    vi.spyOn(ipc, 'getAnalysisEnvironment').mockResolvedValue(env());
    await integrationStatusStore.refresh();
    vi.spyOn(ipc, 'getAnalysisEnvironment').mockRejectedValue(new Error('Timeout IPC'));
    await integrationStatusStore.refresh();

    const html = render();

    expect(html).toContain('desatualizado');
    expect(html).not.toContain('Falha na consulta');
  });

  it('ferramenta ausente informada pelo host não vira falha de consulta', async () => {
    vi.spyOn(ipc, 'getAnalysisEnvironment').mockResolvedValue(env({ ffmpegAvailable: false }));
    await integrationStatusStore.refresh();

    const html = render();

    expect(html).toContain('Não encontrado');
    expect(html).not.toContain('Falha na consulta');
  });
});
