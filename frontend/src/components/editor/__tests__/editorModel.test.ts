import { describe, expect, it } from 'vitest';
import type {
  EditorApplyPreview,
  EditorApplyResult,
  EditorCapabilities,
  EditorEnvironmentState
} from '../../../types/ipc';
import {
  blockingReasonLabel,
  buildApplyRequest,
  canSendApply,
  describeEditorError,
  editorModeLabel,
  editorStatusBadge,
  editorVersionLabel,
  hasRecordNotSaved,
  hasSnapshot,
  isEditorConnected,
  isEditorSimulated,
  mappingBadge,
  newIdempotencyKey,
  resultErrorInfo,
  resultStatusBadge,
  revisionLabel,
  sendBlockedReason,
  sortApplicationsNewestFirst,
  timelineFrameRateLabel,
  verificationBadge,
  operationLabel,
  formatUtcDateTime
} from '../editorModel';

const caps = (state: EditorEnvironmentState, overrides: Partial<EditorCapabilities> = {}): EditorCapabilities => ({
  schemaVersion: 'editor/1',
  adapterMode: state === 'MOCK' ? 'mock' : 'davinci',
  environmentState: state,
  isAvailable: state === 'READY' || state === 'READY_PARTIAL' || state === 'MOCK',
  isSimulated: state === 'MOCK',
  productName: null,
  version: null,
  statusMessage: 'mensagem',
  reasonCode: null,
  operations: [],
  checkedAtUtc: '2026-11-02T10:00:00Z',
  canCreateProject: false,
  canImportMedia: false,
  canCreateTimeline: false,
  canApplyEditPlan: false,
  canExportPreview: false,
  ...overrides
});

const preview = (overrides: Partial<EditorApplyPreview> = {}): EditorApplyPreview => ({
  status: 'DRY_RUN',
  planId: 'plan-1',
  revision: 3,
  planFingerprint: 'pf',
  assemblyFingerprint: 'af-3',
  editorProjectName: 'TravelCut_Teste_r0003_20261102T100000',
  timelineName: 'Timeline Teste r0003',
  timelineFrameRate: '29.97',
  clips: [],
  rejected: [],
  warnings: [],
  manifestRelativePath: '3-Trabalho/editor/plan-1/r0003.assembly.json',
  capabilities: caps('MOCK'),
  canApply: true,
  blockingReasons: [],
  requiresProjectSwitchConfirmation: false,
  ...overrides
});

const result = (overrides: Partial<EditorApplyResult> = {}): EditorApplyResult => ({
  schemaVersion: 'editor-apply/1',
  applicationId: 'app-1',
  status: 'SIMULATED',
  adapterMode: 'mock',
  isSimulated: true,
  projectId: 'proj-1',
  planId: 'plan-1',
  revision: 3,
  assemblyFingerprint: 'af-3',
  idempotencyKey: 'key-1',
  editorProjectName: null,
  timelineName: null,
  editorVersion: null,
  steps: [],
  clipMappings: [],
  rejected: [],
  warnings: [],
  snapshotRelativePath: null,
  recordRelativePath: '3-Trabalho/editor/plan-1/applications/app-1.json',
  startedAtUtc: '2026-11-02T10:00:00Z',
  finishedAtUtc: '2026-11-02T10:00:01Z',
  errorCode: null,
  errorMessage: null,
  reusedPrevious: false,
  ...overrides
});

describe('estado do ambiente', () => {
  it('conectado só com READY ou READY_PARTIAL', () => {
    expect(isEditorConnected(caps('READY'))).toBe(true);
    expect(isEditorConnected(caps('READY_PARTIAL'))).toBe(true);
    for (const state of ['MOCK', 'NOT_INSTALLED', 'NOT_RUNNING', 'SCRIPTING_DISABLED', 'PYTHON_UNAVAILABLE', 'ERROR'] as const) {
      expect(isEditorConnected(caps(state))).toBe(false);
    }
    expect(isEditorConnected(null)).toBe(false);
  });

  it('simulador nunca aparece como conectado', () => {
    expect(editorStatusBadge(caps('MOCK')).label).toBe('Simulador');
    expect(editorStatusBadge(caps('MOCK')).label).not.toMatch(/conectado/i);
    expect(isEditorSimulated(caps('MOCK'))).toBe(true);
    expect(isEditorSimulated(caps('READY'))).toBe(false);
    expect(isEditorSimulated(null)).toBe(false);
  });

  it('selo de verificando quando ainda não há capacidades', () => {
    expect(editorStatusBadge(null)).toEqual({ label: 'Verificando…', tone: 'slate' });
  });

  it('selo de conexão real e limitada', () => {
    expect(editorStatusBadge(caps('READY')).tone).toBe('emerald');
    expect(editorStatusBadge(caps('READY_PARTIAL')).label).toBe('Conectado (limitado)');
    expect(editorStatusBadge(caps('NOT_RUNNING')).tone).toBe('rose');
  });

  it('frases de estado do ambiente traduzidas', () => {
    expect(editorModeLabel(caps('MOCK'))).toBe('Simulador');
    expect(editorModeLabel(caps('READY'))).toBe('DaVinci real');
    expect(editorModeLabel(null)).toBe('Verificando…');
  });

  it('versão nunca inventada', () => {
    expect(editorVersionLabel(caps('MOCK'))).toBe('versão não lida');
    expect(editorVersionLabel(caps('READY', { version: '19.1' }))).toBe('19.1');
  });
});

describe('capacidades', () => {
  it('traduz nome de operação e selo de verificação', () => {
    expect(operationLabel('apply_edit_plan')).toBe('Aplicar plano de edição');
    expect(operationLabel('algo_novo')).toBe('algo_novo');
    expect(verificationBadge('mock_pass')).toEqual({ label: 'só simulador', tone: 'amber' });
    expect(verificationBadge('host_pass').label).toBe('homologada');
    expect(verificationBadge('host_pending').label).toBe('a homologar no host');
    expect(verificationBadge('documented').label).toBe('documentada');
    expect(verificationBadge('unsupported').label).toBe('não suportada');
  });
});

describe('bloqueios e erros', () => {
  it('traduz os códigos de bloqueio do contrato', () => {
    expect(blockingReasonLabel('PLAN_OUTDATED')).toBe('plano desatualizado — gere de novo na Tela 03');
    expect(blockingReasonLabel('SOURCE_FILE_MISSING')).toContain('arquivo de mídia ausente');
    expect(blockingReasonLabel('PLAN_TAMPERED')).toContain('fingerprint');
    expect(blockingReasonLabel('NO_CLIPS')).toContain('clipes');
    expect(blockingReasonLabel('NO_PLAN')).toContain('Tela 03');
    expect(blockingReasonLabel('EDITOR_UNAVAILABLE')).toContain('indisponível');
  });

  it('código desconhecido fica visível, sem mensagem inventada', () => {
    expect(blockingReasonLabel('XPTO')).toContain('XPTO');
  });

  it('descreve erro de IPC com código e mensagem', () => {
    const err = Object.assign(new Error('mensagem do host'), { code: 'PROJECT_VERSION_CONFLICT' });
    const info = describeEditorError(err);
    expect(info.code).toBe('PROJECT_VERSION_CONFLICT');
    expect(info.message).toContain('mudou desde a preparação');
  });

  it('erro sem código usa a mensagem recebida ou um texto padrão', () => {
    expect(describeEditorError(new Error('falhou aqui'))).toEqual({ code: null, message: 'falhou aqui' });
    expect(describeEditorError('texto puro').message).toBe('texto puro');
    expect(describeEditorError(undefined)).toEqual({ code: null, message: 'Falha desconhecida ao enviar ao DaVinci.' });
    expect(describeEditorError({ code: 'X_CODE', message: '   ' }).message).toBe('Falha desconhecida ao enviar ao DaVinci.');
  });

  it('erro do resultado só existe quando o host envia código ou mensagem', () => {
    expect(resultErrorInfo(result())).toBeNull();
    expect(resultErrorInfo(result({ errorCode: 'EDITOR_FAILED', errorMessage: null }))).toEqual({
      code: 'EDITOR_FAILED',
      message: 'O editor não informou detalhes do erro.'
    });
  });
});

describe('resultado', () => {
  it('status do resultado com selo', () => {
    expect(resultStatusBadge('SIMULATED').label).toBe('Simulado — nada foi criado no DaVinci');
    expect(resultStatusBadge('READY_REAL').tone).toBe('emerald');
    expect(resultStatusBadge('PARTIAL_REAL').tone).toBe('amber');
    expect(resultStatusBadge('ERROR').tone).toBe('rose');
  });

  it('mapeamento: true, false e null (não conferido)', () => {
    expect(mappingBadge(true).label).toBe('confere');
    expect(mappingBadge(false).label).toBe('diverge');
    expect(mappingBadge(null).label).toBe('não conferido');
  });

  it('snapshot só com caminho relativo; RECORD_NOT_SAVED detectado', () => {
    expect(hasSnapshot(result())).toBe(false);
    expect(hasSnapshot(result({ snapshotRelativePath: '3-Trabalho/editor/plan-1/x.drp' }))).toBe(true);
    expect(hasRecordNotSaved([{ code: 'RECORD_NOT_SAVED', message: 'x', clipId: null }])).toBe(true);
    expect(hasRecordNotSaved([])).toBe(false);
  });

  it('ordena histórico do mais recente para o mais antigo sem mutar a lista', () => {
    const list = [
      result({ applicationId: 'a', startedAtUtc: '2026-11-01T08:00:00Z' }),
      result({ applicationId: 'b', startedAtUtc: '2026-11-02T08:00:00Z' }),
      result({ applicationId: 'c', startedAtUtc: '2026-10-30T08:00:00Z' })
    ];
    const sorted = sortApplicationsNewestFirst(list);
    expect(sorted.map((r) => r.applicationId)).toEqual(['b', 'a', 'c']);
    expect(list.map((r) => r.applicationId)).toEqual(['a', 'b', 'c']);
  });

  it('data inválida vira traço', () => {
    expect(formatUtcDateTime(null)).toBe('—');
    expect(formatUtcDateTime('não é data')).toBe('—');
    expect(formatUtcDateTime('2026-11-02T10:00:00Z')).not.toBe('—');
  });
});

describe('preparação e envio', () => {
  it('taxa da timeline a partir do timeBase', () => {
    expect(timelineFrameRateLabel({ frameDurationNum: 1001, frameDurationDen: 30000, label: '' })).toBe('29.97');
    expect(timelineFrameRateLabel({ frameDurationNum: 1, frameDurationDen: 25, label: '' })).toBe('25');
    expect(timelineFrameRateLabel({ frameDurationNum: 1001, frameDurationDen: 24000, label: '' })).toBe('23.976');
    expect(timelineFrameRateLabel({ frameDurationNum: 0, frameDurationDen: 25, label: '' })).toBe('—');
  });

  it('botão só habilita com canApply e confirmação quando exigida', () => {
    expect(canSendApply(preview(), false, false)).toBe(true);
    expect(canSendApply(null, false, false)).toBe(false);
    expect(canSendApply(preview({ canApply: false, blockingReasons: ['NO_CLIPS'] }), false, false)).toBe(false);
    const needsConfirm = preview({ requiresProjectSwitchConfirmation: true });
    expect(canSendApply(needsConfirm, false, false)).toBe(false);
    expect(canSendApply(needsConfirm, true, false)).toBe(true);
    expect(canSendApply(preview(), false, true)).toBe(false);
  });

  it('motivo do bloqueio aparece para o usuário', () => {
    expect(sendBlockedReason(null, false, false)).toBe('Prepare o envio antes de enviar.');
    expect(sendBlockedReason(preview({ canApply: false, blockingReasons: ['PLAN_OUTDATED'] }), false, false)).toContain(
      'Tela 03'
    );
    expect(sendBlockedReason(preview({ requiresProjectSwitchConfirmation: true }), false, false)).toContain('confirmação');
    expect(sendBlockedReason(preview(), false, false)).toBeNull();
  });

  it('corpo de editor.apply usa revisão e fingerprint do dry-run', () => {
    const req = buildApplyRequest('proj-1', preview(), false, 'chave-1');
    expect(req).toEqual({
      projectId: 'proj-1',
      revision: 3,
      assemblyFingerprint: 'af-3',
      confirmProjectSwitch: false,
      idempotencyKey: 'chave-1'
    });
    const confirmed = buildApplyRequest('proj-1', preview({ requiresProjectSwitchConfirmation: true }), true, 'k');
    expect(confirmed.confirmProjectSwitch).toBe(true);
  });

  it('chave de idempotência: remove hífens do UUID', () => {
    expect(newIdempotencyKey(() => '3f2a1b0c-1111-4222-8333-444455556666')).toBe('3f2a1b0c111142228333444455556666');
  });

  it('chave de idempotência: fallback quando o gerador falha ou produz caracteres inválidos', () => {
    const fromNull = newIdempotencyKey(() => null);
    expect(fromNull).toMatch(/^[A-Za-z0-9_-]{1,100}$/);
    const fromInvalid = newIdempotencyKey(() => 'com espaço e ponto.');
    expect(fromInvalid).toMatch(/^[A-Za-z0-9_-]{1,100}$/);
    expect(newIdempotencyKey(() => '')).toMatch(/^[A-Za-z0-9_-]{1,100}$/);
  });

  it('chave padrão gerada sem gerador injetado é válida', () => {
    expect(newIdempotencyKey()).toMatch(/^[A-Za-z0-9_-]{1,100}$/);
  });

  it('formatação de revisão no formato do manifesto', () => {
    expect(revisionLabel(3)).toBe('r0003');
    expect(revisionLabel(12)).toBe('r0012');
  });

  it('selo de verificação desconhecido permanece visível', () => {
    expect(verificationBadge('novo_nivel').label).toBe('novo_nivel');
  });
});
