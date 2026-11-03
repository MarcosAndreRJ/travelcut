import type {
  ApplyEditorPlanRequest,
  CapabilityVerification,
  EditorApplyPreview,
  EditorApplyResult,
  EditorCapabilities,
  EditorEnvironmentState,
  EditorOperation,
  EditorResultStatus,
  EditorWarning,
  TimelineTimeBaseDto
} from '../../types/ipc';

// Funções puras do frontend para a tela "Enviar ao DaVinci" (contrato editor/1).
// Nenhuma regra de negócio aqui: só tradução, derivação de exibição e montagem de requisições.

export type EditorTone = 'emerald' | 'amber' | 'rose' | 'cyan' | 'slate';

export interface EditorBadgeInfo {
  label: string;
  tone: EditorTone;
}

export interface EditorErrorInfo {
  code: string | null;
  message: string;
}

// ---- Estado do ambiente ----

export const ENVIRONMENT_STATE_LABEL: Record<EditorEnvironmentState, string> = {
  NOT_INSTALLED: 'DaVinci Resolve não encontrado',
  NOT_RUNNING: 'Resolve fechado',
  SCRIPTING_DISABLED: 'Scripting externo desativado — Preferências › Sistema › Geral › External scripting using: Local',
  PYTHON_UNAVAILABLE: 'Python não encontrado para a bridge',
  READY_PARTIAL: 'conectado com limitações',
  READY: 'Conectado',
  MOCK: 'Simulador — nada é enviado ao DaVinci',
  ERROR: 'Erro ao consultar o DaVinci'
};

/** Rótulos curtos para barras laterais e cabeçalhos. */
const ENVIRONMENT_STATE_SHORT: Record<EditorEnvironmentState, EditorBadgeInfo> = {
  NOT_INSTALLED: { label: 'Não encontrado', tone: 'rose' },
  NOT_RUNNING: { label: 'Resolve fechado', tone: 'rose' },
  SCRIPTING_DISABLED: { label: 'Scripting desativado', tone: 'rose' },
  PYTHON_UNAVAILABLE: { label: 'Python ausente', tone: 'rose' },
  READY_PARTIAL: { label: 'Conectado (limitado)', tone: 'amber' },
  READY: { label: 'Conectado', tone: 'emerald' },
  MOCK: { label: 'Simulador', tone: 'amber' },
  ERROR: { label: 'Erro', tone: 'rose' }
};

/** Verdadeiro somente com DaVinci real acessível (READY ou READY_PARTIAL). Simulador nunca é "conectado". */
export function isEditorConnected(caps: EditorCapabilities | null | undefined): boolean {
  return caps?.environmentState === 'READY' || caps?.environmentState === 'READY_PARTIAL';
}

/** Verdadeiro quando o adaptador é o simulador (nada chega ao DaVinci). */
export function isEditorSimulated(caps: EditorCapabilities | null | undefined): boolean {
  if (!caps) return false;
  return caps.environmentState === 'MOCK' || caps.adapterMode === 'mock' || caps.isSimulated;
}

/** Selo de estado do ambiente. null = ainda não verificado (nunca "Conectado" por padrão). */
export function editorStatusBadge(caps: EditorCapabilities | null | undefined): EditorBadgeInfo {
  if (!caps) return { label: 'Verificando…', tone: 'slate' };
  return ENVIRONMENT_STATE_SHORT[caps.environmentState] ?? { label: 'Estado desconhecido', tone: 'rose' };
}

/** Modo do adaptador para o cabeçalho: "Simulador" ou "DaVinci real", conforme adapterMode. */
export function editorModeLabel(caps: EditorCapabilities | null | undefined): string {
  if (!caps) return 'Verificando…';
  return caps.adapterMode === 'mock' ? 'Simulador' : 'DaVinci real';
}

/** Versão só quando lida da API; null vira texto explícito (nunca inventar versão). */
export function editorVersionLabel(caps: EditorCapabilities | null | undefined): string {
  return caps?.version ?? 'versão não lida';
}

// ---- Capacidades ----

export const OPERATION_LABEL: Record<EditorOperation, string> = {
  detect_capabilities: 'Detectar capacidades',
  create_project: 'Criar projeto novo (TravelCut_*)',
  import_media: 'Importar mídia',
  create_timeline: 'Criar timeline',
  apply_edit_plan: 'Aplicar plano de edição',
  query_timeline: 'Consultar timeline',
  export_preview: 'Exportar prévia',
  export_project: 'Exportar projeto (.drp)',
  create_snapshot: 'Criar snapshot (.drp)'
};

export const VERIFICATION_LABEL: Record<CapabilityVerification, EditorBadgeInfo> = {
  documented: { label: 'documentada', tone: 'slate' },
  mock_pass: { label: 'só simulador', tone: 'amber' },
  host_pending: { label: 'a homologar no host', tone: 'amber' },
  host_pass: { label: 'homologada', tone: 'emerald' },
  unsupported: { label: 'não suportada', tone: 'rose' }
};

export function operationLabel(operation: string): string {
  return OPERATION_LABEL[operation as EditorOperation] ?? operation;
}

export function verificationBadge(verification: string): EditorBadgeInfo {
  return VERIFICATION_LABEL[verification as CapabilityVerification] ?? { label: verification, tone: 'slate' };
}

// ---- Bloqueios e erros ----

const BLOCKING_REASON_LABEL: Record<string, string> = {
  EDITOR_UNAVAILABLE: 'Editor indisponível neste ambiente. O manifesto fica gravado para aplicação manual.',
  PLAN_OUTDATED: 'plano desatualizado — gere de novo na Tela 03',
  SOURCE_FILE_MISSING: 'Há arquivo de mídia ausente em disco.',
  PLAN_TAMPERED: 'A revisão do plano não confere com o fingerprint gravado.',
  NO_CLIPS: 'O plano não tem clipes para enviar.',
  NO_PLAN: 'Não há plano editorial neste projeto. Gere um plano na Tela 03.',
  PROJECT_VERSION_CONFLICT: 'A montagem mudou desde a preparação. Prepare o envio de novo.',
  CONFIRMATION_REQUIRED: 'Confirme a troca do projeto aberto no DaVinci antes de enviar.',
  IDEMPOTENCY_KEY_REUSED: 'Esta chave de envio já foi usada com outra montagem. Prepare o envio de novo.',
  INVALID_IDEMPOTENCY_KEY: 'Chave de envio inválida. Prepare o envio de novo.',
  PROJECT_NOT_FOUND: 'Projeto não encontrado no host.',
  INVALID_ARGUMENT: 'Dados de envio inválidos.'
};

export function blockingReasonLabel(code: string): string {
  return BLOCKING_REASON_LABEL[code] ?? `Motivo não reconhecido pela tela (${code}).`;
}

/** Descreve erro vindo do IPC ou de Error comum. Nunca devolve mensagem vazia. */
export function describeEditorError(err: unknown): EditorErrorInfo {
  const rawCode = (err as { code?: unknown } | null)?.code;
  const code = typeof rawCode === 'string' && rawCode.length > 0 ? rawCode : null;
  const rawMessage =
    typeof err === 'string'
      ? err
      : typeof (err as { message?: unknown } | null)?.message === 'string'
        ? (err as { message: string }).message
        : '';
  const known = code ? BLOCKING_REASON_LABEL[code] : undefined;
  const message = known ?? (rawMessage.trim().length > 0 ? rawMessage : 'Falha desconhecida ao enviar ao DaVinci.');
  return { code, message };
}

/** Erro registrado no resultado (ERROR), sem inventar mensagem quando o host não enviou. */
export function resultErrorInfo(result: EditorApplyResult): EditorErrorInfo | null {
  if (!result.errorCode && !result.errorMessage) return null;
  const code = result.errorCode ?? null;
  const known = code ? BLOCKING_REASON_LABEL[code] : undefined;
  return { code, message: result.errorMessage ?? known ?? 'O editor não informou detalhes do erro.' };
}

// ---- Resultado ----

export const RESULT_STATUS_LABEL: Record<EditorResultStatus, EditorBadgeInfo> = {
  SIMULATED: { label: 'Simulado — nada foi criado no DaVinci', tone: 'amber' },
  DRY_RUN: { label: 'Simulação seca — nada foi enviado', tone: 'slate' },
  UNAVAILABLE: { label: 'Editor indisponível', tone: 'rose' },
  READY_REAL: { label: 'Concluído no DaVinci real', tone: 'emerald' },
  PARTIAL_REAL: { label: 'Parcial no DaVinci real', tone: 'amber' },
  ERROR: { label: 'Erro no envio', tone: 'rose' }
};

export function resultStatusBadge(status: string): EditorBadgeInfo {
  return RESULT_STATUS_LABEL[status as EditorResultStatus] ?? { label: status, tone: 'slate' };
}

const STEP_STATUS_LABEL: Record<string, EditorBadgeInfo> = {
  ok: { label: 'ok', tone: 'emerald' },
  failed: { label: 'falhou', tone: 'rose' },
  skipped: { label: 'ignorado', tone: 'slate' },
  simulated: { label: 'simulado', tone: 'amber' }
};

export function stepStatusBadge(status: string): EditorBadgeInfo {
  return STEP_STATUS_LABEL[status] ?? { label: status, tone: 'slate' };
}

/** Mapeamento de clipe: true/false/null (null = "não conferido", nunca "ok" por padrão). */
export function mappingBadge(matches: boolean | null): EditorBadgeInfo {
  if (matches === true) return { label: 'confere', tone: 'emerald' };
  if (matches === false) return { label: 'diverge', tone: 'rose' };
  return { label: 'não conferido', tone: 'slate' };
}

/** Snapshot .drp só existe quando o host devolve caminho relativo (nunca no mock). */
export function hasSnapshot(result: EditorApplyResult): boolean {
  return typeof result.snapshotRelativePath === 'string' && result.snapshotRelativePath.length > 0;
}

export function hasRecordNotSaved(warnings: EditorWarning[]): boolean {
  return warnings.some((w) => w.code === 'RECORD_NOT_SAVED');
}

/** Ordena do mais recente para o mais antigo sem alterar a lista original. */
export function sortApplicationsNewestFirst(list: EditorApplyResult[]): EditorApplyResult[] {
  return [...list].sort((a, b) => {
    const ta = Date.parse(a.startedAtUtc);
    const tb = Date.parse(b.startedAtUtc);
    return (Number.isNaN(tb) ? 0 : tb) - (Number.isNaN(ta) ? 0 : ta);
  });
}

// ---- Preparação e envio ----

/** Taxa da timeline a partir do timeBase do plano (ex.: 1001/30000 → "29.97"). */
export function timelineFrameRateLabel(tb: TimelineTimeBaseDto): string {
  if (tb.frameDurationNum <= 0 || tb.frameDurationDen <= 0) return '—';
  const fps = tb.frameDurationDen / tb.frameDurationNum;
  return String(Number(fps.toFixed(3)));
}

/** Motivo de o botão de envio estar desabilitado (vazio = habilitado). */
export function sendBlockedReason(
  preview: EditorApplyPreview | null,
  confirmed: boolean,
  busy: boolean
): string | null {
  if (busy) return 'Envio em andamento.';
  if (!preview) return 'Prepare o envio antes de enviar.';
  if (!preview.canApply) {
    const first = preview.blockingReasons[0];
    return first ? blockingReasonLabel(first) : 'A preparação não permite o envio.';
  }
  if (preview.requiresProjectSwitchConfirmation && !confirmed) {
    return 'Marque a confirmação de troca de projeto.';
  }
  return null;
}

export function canSendApply(preview: EditorApplyPreview | null, confirmed: boolean, busy: boolean): boolean {
  return sendBlockedReason(preview, confirmed, busy) === null;
}

/** Corpo de editor.apply: revisão e fingerprint vêm do dry-run; confirmação só quando o host exige. */
export function buildApplyRequest(
  projectId: string,
  preview: EditorApplyPreview,
  confirmed: boolean,
  idempotencyKey: string
): ApplyEditorPlanRequest {
  return {
    projectId,
    revision: preview.revision,
    assemblyFingerprint: preview.assemblyFingerprint,
    confirmProjectSwitch: preview.requiresProjectSwitchConfirmation ? confirmed : false,
    idempotencyKey
  };
}

const DEFAULT_IDEMPOTENCY_PATTERN = /^[A-Za-z0-9_-]{1,100}$/;

function defaultRandomUuid(): string | null {
  try {
    const c = globalThis.crypto as Crypto | undefined;
    return c && typeof c.randomUUID === 'function' ? c.randomUUID() : null;
  } catch {
    return null;
  }
}

function fallbackKey(): string {
  const stamp = Date.now().toString(36);
  const rand = Math.random().toString(36).slice(2, 14);
  return `${stamp}${rand}`;
}

/**
 * Chave de idempotência por preparação: UUID sem hífens (ou fallback). Gerada uma vez por preparação
 * e reutilizada nas novas tentativas do mesmo envio. O gerador é injetável para teste.
 */
export function newIdempotencyKey(random: () => string | null = defaultRandomUuid): string {
  const raw = random()?.replace(/-/g, '') ?? '';
  return DEFAULT_IDEMPOTENCY_PATTERN.test(raw) ? raw : fallbackKey();
}

/** Número de revisão no formato do manifesto (r0001). */
export function revisionLabel(revision: number): string {
  return `r${String(revision).padStart(4, '0')}`;
}

export function formatUtcDateTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleString('pt-BR');
}

export const SWITCH_CONFIRMATION_TEXT =
  'Entendo que o Resolve vai fechar o projeto aberto e abrir um projeto novo TravelCut_*. Salvei meu trabalho.';
