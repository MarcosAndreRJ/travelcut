import { describe, expect, it, vi } from 'vitest';
import { ipc } from '../ipcBridge';
import type { MediaAssetDto, EditSuggestion, PlanRevisionInfoDto } from '../../types/ipc';

describe('ipcBridge normalização de respostas de mídia, sugestões e planos', () => {
  const projectId = 'proj-test-123';

  it('listMedia desempacota resposta no formato { media: [...] }', async () => {
    const fakeAsset: MediaAssetDto = {
      mediaId: 'm1',
      projectId,
      relativePath: 'video.mp4',
      byteLength: 1024,
      kind: 'video',
      fps: 30,
      width: 1920,
      height: 1080,
      durationSeconds: 12.5,
      codec: 'h264',
      hasTranscript: false,
      hasVAD: false
    };

    const spy = vi.spyOn(ipc, 'send').mockResolvedValueOnce({ media: [fakeAsset] });

    const result = await ipc.listMedia(projectId);
    expect(spy).toHaveBeenCalledWith('media.list', { projectId });
    expect(Array.isArray(result)).toBe(true);
    expect(result.length).toBe(1);
    expect(result[0].mediaId).toBe('m1');

    spy.mockRestore();
  });

  it('listMedia aceita resposta direta como array MediaAssetDto[]', async () => {
    const fakeAsset: MediaAssetDto = {
      mediaId: 'm2',
      projectId,
      relativePath: 'audio.wav',
      byteLength: 2048,
      kind: 'audio',
      fps: 0,
      width: 0,
      height: 0,
      durationSeconds: 30,
      codec: 'pcm',
      hasTranscript: false,
      hasVAD: false
    };

    const spy = vi.spyOn(ipc, 'send').mockResolvedValueOnce([fakeAsset]);

    const result = await ipc.listMedia(projectId);
    expect(Array.isArray(result)).toBe(true);
    expect(result.length).toBe(1);
    expect(result[0].mediaId).toBe('m2');

    spy.mockRestore();
  });

  it('listMedia retorna array vazio quando o projeto está vazio ([] recebido do host)', async () => {
    const spy = vi.spyOn(ipc, 'send').mockResolvedValueOnce([]);

    const result = await ipc.listMedia(projectId);
    expect(Array.isArray(result)).toBe(true);
    expect(result.length).toBe(0);

    spy.mockRestore();
  });

  it('listMedia lança erro de contrato se payload for nulo (não converte silenciosamente em array vazio)', async () => {
    const spy = vi.spyOn(ipc, 'send').mockResolvedValueOnce(null);

    await expect(ipc.listMedia(projectId)).rejects.toThrow(/Falha de contrato IPC em 'media.list'/);

    spy.mockRestore();
  });

  it('listMedia lança erro de contrato se payload for inválido (não-array e sem chave media válida)', async () => {
    const spy = vi.spyOn(ipc, 'send').mockResolvedValueOnce({ unexpected: 123 });

    await expect(ipc.listMedia(projectId)).rejects.toThrow(/Falha de contrato IPC em 'media.list'/);

    spy.mockRestore();
  });

  it('listMedia propaga falhas de IPC rejeitadas', async () => {
    const spy = vi.spyOn(ipc, 'send').mockRejectedValueOnce(new Error('IPC network error'));

    await expect(ipc.listMedia(projectId)).rejects.toThrow('IPC network error');

    spy.mockRestore();
  });

  it('listSuggestions desempacota resposta no formato { suggestions: [...] }', async () => {
    const fakeSuggestion: EditSuggestion = {
      suggestionId: 's1',
      mediaId: 'm1',
      startSeconds: 0,
      endSeconds: 2.5,
      kind: 'silence',
      rationale: 'Pausa prolongada',
      confidenceBand: 'high',
      needsHumanReview: false,
      suggestedAction: 'cut',
      status: 'Proposed',
      author: 'vad'
    };

    const spy = vi.spyOn(ipc, 'send').mockResolvedValueOnce({ suggestions: [fakeSuggestion] });

    const result = await ipc.listSuggestions(projectId);
    expect(Array.isArray(result)).toBe(true);
    expect(result.length).toBe(1);
    expect(result[0].suggestionId).toBe('s1');

    spy.mockRestore();
  });

  it('listSuggestions aceita resposta direta como array EditSuggestion[]', async () => {
    const fakeSuggestion: EditSuggestion = {
      suggestionId: 's2',
      mediaId: 'm1',
      startSeconds: 5,
      endSeconds: 6.2,
      kind: 'filler',
      rationale: 'Palavra de preenchimento',
      confidenceBand: 'medium',
      needsHumanReview: true,
      suggestedAction: 'trim',
      status: 'Approved',
      author: 'ai'
    };

    const spy = vi.spyOn(ipc, 'send').mockResolvedValueOnce([fakeSuggestion]);

    const result = await ipc.listSuggestions(projectId);
    expect(Array.isArray(result)).toBe(true);
    expect(result.length).toBe(1);
    expect(result[0].suggestionId).toBe('s2');

    spy.mockRestore();
  });

  it('listPlanRevisions desempacota resposta no formato { revisions: [...] } ou array', async () => {
    const fakeRevision: PlanRevisionInfoDto = {
      planId: 'plan-1',
      revision: 1,
      baseRevision: null,
      createdAtUtc: new Date().toISOString(),
      author: 'planner',
      planFingerprint: 'fp-123',
      isValid: true,
      clipCount: 4,
      timelineDurationFrames: 1200
    };

    const spyObj = vi.spyOn(ipc, 'send').mockResolvedValueOnce({ revisions: [fakeRevision] });
    const resultObj = await ipc.listPlanRevisions(projectId);
    expect(Array.isArray(resultObj)).toBe(true);
    expect(resultObj.length).toBe(1);
    expect(resultObj[0].revision).toBe(1);
    spyObj.mockRestore();

    const spyArr = vi.spyOn(ipc, 'send').mockResolvedValueOnce([fakeRevision]);
    const resultArr = await ipc.listPlanRevisions(projectId);
    expect(Array.isArray(resultArr)).toBe(true);
    expect(resultArr.length).toBe(1);
    expect(resultArr[0].revision).toBe(1);
    spyArr.mockRestore();
  });
});
