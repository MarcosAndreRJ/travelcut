import { renderToString } from 'react-dom/server';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { LibraryTranscriptionView } from '../LibraryTranscriptionView';
import { ipc } from '../../services/ipcBridge';
import type { MediaAssetDto, ProjectSummary } from '../../types/ipc';

describe('LibraryTranscriptionView unit & integration tests', () => {
  const dummyProject: ProjectSummary = {
    projectId: 'test-proj-uuid',
    name: 'Projeto de Teste',
    rootPath: 'K:\\Videos\\Projeto',
    fourFoldersValid: true,
    mediaCount: 0,
    suggestionsCount: 0,
    approvedCutsCount: 0,
    totalOriginalDurationSeconds: 0,
    estimatedCutDurationSeconds: null,
    lastModifiedUtc: new Date().toISOString()
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renderiza mensagem de projeto não selecionado quando project é null', () => {
    const html = renderToString(<LibraryTranscriptionView project={null} />);
    expect(html).toContain('Nenhum projeto aberto');
    expect(html).toContain('Crie ou abra um projeto para ver a biblioteca de mídias');
  });

  it('renderiza casca da visualização de clipes e importação quando projeto é fornecido', () => {
    const html = renderToString(<LibraryTranscriptionView project={dummyProject} />);
    expect(html).toContain('Importar mídia');
    expect(html).toContain('Biblioteca vazia');
  });

  it('lida com listMedia retornando array direto com sucesso', async () => {
    const fakeAsset: MediaAssetDto = {
      mediaId: 'asset-1',
      projectId: dummyProject.projectId,
      relativePath: '1-Bruto/DJI_0001.MP4',
      byteLength: 1048576,
      kind: 'video',
      fps: 30,
      width: 1920,
      height: 1080,
      durationSeconds: 15.0,
      codec: 'h264',
      hasTranscript: false,
      hasVAD: false
    };

    const spy = vi.spyOn(ipc, 'send').mockResolvedValueOnce([fakeAsset]);
    const assets = await ipc.listMedia(dummyProject.projectId);

    expect(spy).toHaveBeenCalledWith('media.list', { projectId: dummyProject.projectId });
    expect(Array.isArray(assets)).toBe(true);
    expect(assets).toHaveLength(1);
    expect(assets[0].relativePath).toBe('1-Bruto/DJI_0001.MP4');
  });

  it('lida com listMedia retornando envelope legado { media: [...] } com retrocompatibilidade', async () => {
    const fakeAsset: MediaAssetDto = {
      mediaId: 'asset-legacy',
      projectId: dummyProject.projectId,
      relativePath: '1-Bruto/LEGACY.MP4',
      byteLength: 524288,
      kind: 'video',
      fps: 24,
      width: 1280,
      height: 720,
      durationSeconds: 10.0,
      codec: 'h264',
      hasTranscript: true,
      hasVAD: true
    };

    vi.spyOn(ipc, 'send').mockResolvedValueOnce({ media: [fakeAsset] });
    const assets = await ipc.listMedia(dummyProject.projectId);

    expect(Array.isArray(assets)).toBe(true);
    expect(assets).toHaveLength(1);
    expect(assets[0].mediaId).toBe('asset-legacy');
  });

  it('trata projeto vazio retornando array vazio sem erro', async () => {
    vi.spyOn(ipc, 'send').mockResolvedValueOnce([]);
    const assets = await ipc.listMedia(dummyProject.projectId);

    expect(Array.isArray(assets)).toBe(true);
    expect(assets).toHaveLength(0);
  });

  it('falha explicitamente em caso de erro de contrato (payload null)', async () => {
    vi.spyOn(ipc, 'send').mockResolvedValueOnce(null);

    await expect(ipc.listMedia(dummyProject.projectId)).rejects.toThrow(
      /Falha de contrato IPC em 'media\.list'/
    );
  });

  it('falha explicitamente em caso de payload arbitrário inválido', async () => {
    vi.spyOn(ipc, 'send').mockResolvedValueOnce({ invalidField: true, count: 0 });

    await expect(ipc.listMedia(dummyProject.projectId)).rejects.toThrow(
      /Falha de contrato IPC em 'media\.list'/
    );
  });

  it('propaga erro de comunicação do canal IPC', async () => {
    vi.spyOn(ipc, 'send').mockRejectedValueOnce(new Error('Processo host desconectado'));

    await expect(ipc.listMedia(dummyProject.projectId)).rejects.toThrow(
      'Processo host desconectado'
    );
  });
});
