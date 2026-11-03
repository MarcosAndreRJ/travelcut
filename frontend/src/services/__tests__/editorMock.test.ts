import { describe, expect, it } from 'vitest';
import { ipc } from '../ipcBridge';
import type {
  ApplyEditorPlanRequest,
  EditorApplicationsResponse,
  EditorApplyPreview,
  EditorApplyResult,
  EditorCapabilities,
  EditPlanDto
} from '../../types/ipc';

// DEMONSTRAÇÃO: o mock do navegador segue o contrato editor/1 (sem nada enviado ao DaVinci).
// Um único fluxo sequencial: o mock guarda estado em memória por instância.

const PROJECT_ID = 'e5b7a192-3c82-4f15-8f6a-93a0b5f190c1';

describe('mock do editor (editor/1) — DEMONSTRAÇÃO', () => {
  it('capacidades do simulador: MOCK, simulado e sem versão inventada', async () => {
    const caps = await ipc.send<EditorCapabilities>('editor.capabilities');
    expect(caps.schemaVersion).toBe('editor/1');
    expect(caps.adapterMode).toBe('mock');
    expect(caps.environmentState).toBe('MOCK');
    expect(caps.isSimulated).toBe(true);
    expect(caps.version).toBeNull();
    expect(caps.productName).toBeNull();
    expect(caps.operations.length).toBe(9);
    expect(caps.canApplyEditPlan).toBe(true);
    expect(caps.operations.every((o) => o.verification !== 'host_pass')).toBe(true);
  });

  it('sem plano, a preparação é recusada com NO_PLAN (sem dado inventado)', async () => {
    await expect(ipc.send('editor.prepareApply', { projectId: PROJECT_ID, revision: null })).rejects.toMatchObject({
      code: 'NO_PLAN'
    });
  });

  it('dry-run derivado do plano mock: clipes V1 em frames e taxa da timeline', async () => {
    await ipc.send<EditPlanDto>('plan.generate', { projectId: PROJECT_ID });
    const preview = await ipc.send<EditorApplyPreview>('editor.prepareApply', { projectId: PROJECT_ID, revision: null });

    expect(preview.status).toBe('DRY_RUN');
    expect(preview.canApply).toBe(true);
    expect(preview.blockingReasons).toEqual([]);
    expect(preview.requiresProjectSwitchConfirmation).toBe(false);
    expect(preview.timelineFrameRate).toBe('29.97');
    expect(preview.manifestRelativePath).toMatch(/^3-Trabalho\/editor\/.+\/r\d{4}\.assembly\.json$/);
    expect(preview.editorProjectName).toMatch(/^TravelCut_.+_r\d{4}_\d{8}T\d{6}$/);
    expect(preview.clips.length).toBeGreaterThan(0);

    let previous = -1;
    for (const clip of preview.clips) {
      expect(clip.sourceEndFrameExclusive).toBeGreaterThan(clip.sourceStartFrame);
      expect(clip.durationFrames).toBeGreaterThan(0);
      expect(clip.recordFrame).toBeGreaterThan(previous);
      previous = clip.recordFrame;
      expect(Number.isInteger(clip.sourceStartFrame)).toBe(true);
      expect(Number.isInteger(clip.sourceEndFrameExclusive)).toBe(true);
    }
  });

  it('envio com fingerprint divergente é recusado com PROJECT_VERSION_CONFLICT', async () => {
    const preview = await ipc.send<EditorApplyPreview>('editor.prepareApply', { projectId: PROJECT_ID, revision: null });
    const body: ApplyEditorPlanRequest = {
      projectId: PROJECT_ID,
      revision: preview.revision,
      assemblyFingerprint: 'fingerprint-antigo',
      confirmProjectSwitch: false,
      idempotencyKey: 'chave_conflito_01'
    };
    await expect(ipc.send('editor.apply', body)).rejects.toMatchObject({ code: 'PROJECT_VERSION_CONFLICT' });
  });

  it('envio simulado: SIMULATED, passos simulados, mapeamentos não conferidos e sem snapshot', async () => {
    const preview = await ipc.send<EditorApplyPreview>('editor.prepareApply', { projectId: PROJECT_ID, revision: null });
    const body: ApplyEditorPlanRequest = {
      projectId: PROJECT_ID,
      revision: preview.revision,
      assemblyFingerprint: preview.assemblyFingerprint,
      confirmProjectSwitch: false,
      idempotencyKey: 'chave_demo_envio_01'
    };
    const result = await ipc.send<EditorApplyResult>('editor.apply', body);

    expect(result.status).toBe('SIMULATED');
    expect(result.isSimulated).toBe(true);
    expect(result.adapterMode).toBe('mock');
    expect(result.reusedPrevious).toBe(false);
    expect(result.snapshotRelativePath).toBeNull();
    expect(result.editorVersion).toBeNull();
    expect(result.steps.length).toBeGreaterThan(0);
    expect(result.steps.every((s) => s.status === 'simulated')).toBe(true);
    expect(result.clipMappings.length).toBe(preview.clips.length);
    expect(result.clipMappings.every((m) => m.matches === null)).toBe(true);
    expect(result.idempotencyKey).toBe('chave_demo_envio_01');
  });

  it('mesma chave e mesma montagem: reaproveita o registro anterior', async () => {
    const preview = await ipc.send<EditorApplyPreview>('editor.prepareApply', { projectId: PROJECT_ID, revision: null });
    const body: ApplyEditorPlanRequest = {
      projectId: PROJECT_ID,
      revision: preview.revision,
      assemblyFingerprint: preview.assemblyFingerprint,
      confirmProjectSwitch: false,
      idempotencyKey: 'chave_demo_envio_01'
    };
    const first = await ipc.send<EditorApplyResult>('editor.apply', body);
    const again = await ipc.send<EditorApplyResult>('editor.apply', body);
    expect(again.reusedPrevious).toBe(true);
    expect(again.applicationId).toBe(first.applicationId);
  });

  it('chave com caracteres inválidos é recusada', async () => {
    const preview = await ipc.send<EditorApplyPreview>('editor.prepareApply', { projectId: PROJECT_ID, revision: null });
    await expect(
      ipc.send('editor.apply', {
        projectId: PROJECT_ID,
        revision: preview.revision,
        assemblyFingerprint: preview.assemblyFingerprint,
        confirmProjectSwitch: false,
        idempotencyKey: 'chave com espaço'
      })
    ).rejects.toMatchObject({ code: 'INVALID_IDEMPOTENCY_KEY' });
  });

  it('histórico: mais recente primeiro e com o modo simulador', async () => {
    const res = await ipc.send<EditorApplicationsResponse>('editor.applications', { projectId: PROJECT_ID });
    expect(res.applications.length).toBeGreaterThanOrEqual(1);
    expect(res.applications[0].adapterMode).toBe('mock');
    expect(res.applications[0].isSimulated).toBe(true);
    const sameKeyCount = res.applications.filter((a) => a.idempotencyKey === 'chave_demo_envio_01').length;
    expect(sameKeyCount).toBe(1);
  });
});
