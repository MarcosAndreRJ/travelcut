import type {
  AnalysisEnvironmentDto,
  EditorCapabilities,
  MotionCapabilitiesDto
} from '../../types/ipc';
import { editorStatusBadge, type EditorTone } from '../editor/editorModel';

// Derivação pura do estado de integrações para as telas (Painel, Verificação, Configurações).
// Fonte única: analysis.environment, editor.capabilities e motion.capabilities do host.
// Regra: sem leitura real => "Não verificado". Versão só quando o host informou. Nada é inventado.

export type IntegrationState = 'ready' | 'limited' | 'simulated' | 'missing' | 'unverified' | 'error';

export type IntegrationId =
  | 'dotnet'
  | 'webview2'
  | 'davinci'
  | 'ffmpeg'
  | 'python'
  | 'whisper'
  | 'cuda'
  | 'hyperframes'
  | 'claude';

export interface IntegrationRow {
  id: IntegrationId;
  name: string;
  state: IntegrationState;
  /** Rótulo curto exibido no selo (ex.: "Disponível", "Não verificado"). */
  label: string;
  tone: EditorTone;
  /** Versão lida do host; null quando não foi informada. */
  version: string | null;
  /** Explicação curta; mensagens do host são exibidas como vieram. */
  detail: string;
  /** Origem técnica do dado (rótulo de auditoria visível na tela). */
  source: string;
}

export interface IntegrationInputs {
  isMock: boolean;
  analysisEnv: AnalysisEnvironmentDto | null;
  /** Mensagem quando a consulta ao host falhou (diferente de "ainda não consultado"). */
  analysisError: string | null;
  editorCaps: EditorCapabilities | null;
  motionCaps: MotionCapabilitiesDto | null;
  motionError: string | null;
}

const TONE_BY_STATE: Record<IntegrationState, EditorTone> = {
  ready: 'emerald',
  limited: 'amber',
  simulated: 'amber',
  missing: 'rose',
  unverified: 'slate',
  error: 'rose'
};

const SRC_ANALYSIS = 'analysis.environment';
const SRC_EDITOR = 'editor.capabilities';
const SRC_MOTION = 'motion.capabilities';
const SRC_APP = 'aplicativo (processo local)';
const SRC_NONE = 'não consultado pelo app';

const NOT_VERIFIED_DETAIL = 'Sem leitura do host nesta tela. Use "Atualizar verificações" para consultar.';

function row(
  id: IntegrationId,
  name: string,
  state: IntegrationState,
  label: string,
  version: string | null,
  detail: string,
  source: string
): IntegrationRow {
  return { id, name, state, label, tone: TONE_BY_STATE[state], version, detail, source };
}

/** Versão só quando não vazia; string vazia vira null (nunca exibir "" como versão). */
const versionOrNull = (value: string | null | undefined): string | null =>
  typeof value === 'string' && value.trim().length > 0 ? value.trim() : null;

function davinciRow(caps: EditorCapabilities | null): IntegrationRow {
  if (!caps) {
    return row('davinci', 'DaVinci Resolve Studio', 'unverified', 'Não verificado', null, NOT_VERIFIED_DETAIL, SRC_EDITOR);
  }
  const badge = editorStatusBadge(caps);
  const detail = caps.statusMessage?.trim() || 'O host não informou detalhes.';
  const version = versionOrNull(caps.version);
  switch (caps.environmentState) {
    case 'READY':
      return row('davinci', 'DaVinci Resolve Studio', 'ready', badge.label, version, detail, SRC_EDITOR);
    case 'READY_PARTIAL':
      return row('davinci', 'DaVinci Resolve Studio', 'limited', badge.label, version, detail, SRC_EDITOR);
    case 'MOCK':
      return row('davinci', 'DaVinci Resolve Studio', 'simulated', badge.label, null, detail, SRC_EDITOR);
    case 'ERROR':
      return row('davinci', 'DaVinci Resolve Studio', 'error', badge.label, version, detail, SRC_EDITOR);
    default:
      return row('davinci', 'DaVinci Resolve Studio', 'missing', badge.label, version, detail, SRC_EDITOR);
  }
}

function motionRow(caps: MotionCapabilitiesDto | null, error: string | null): IntegrationRow {
  const name = 'HyperFrames (animações)';
  if (error) {
    return row('hyperframes', name, 'error', 'Falha na consulta', null, error, SRC_MOTION);
  }
  if (!caps) {
    return row('hyperframes', name, 'unverified', 'Não verificado', null, NOT_VERIFIED_DETAIL, SRC_MOTION);
  }
  const detail = caps.statusMessage?.trim() || 'O host não informou detalhes.';
  const version = versionOrNull(caps.rendererVersion);
  switch (caps.environmentState) {
    case 'READY':
      return row('hyperframes', name, 'ready', 'Disponível', version, detail, SRC_MOTION);
    case 'MOCK':
      return row('hyperframes', name, 'simulated', 'Simulador (sem vídeo)', null, detail, SRC_MOTION);
    case 'NOT_INSTALLED':
      return row('hyperframes', name, 'missing', 'Não instalado', null, detail, SRC_MOTION);
    case 'NODE_UNAVAILABLE':
      return row('hyperframes', name, 'missing', 'Node.js indisponível', null, detail, SRC_MOTION);
    case 'ERROR':
      return row('hyperframes', name, 'error', 'Erro na checagem', null, detail, SRC_MOTION);
    default:
      return row('hyperframes', name, 'unverified', 'Estado desconhecido', null, detail, SRC_MOTION);
  }
}

/** Linhas de análise (FFmpeg, Python, Whisper, CUDA) a partir de analysis.environment. */
function analysisRows(inputs: IntegrationInputs): IntegrationRow[] {
  const { analysisEnv: env, analysisError: error } = inputs;
  const failed = (id: IntegrationId, name: string): IntegrationRow =>
    row(id, name, 'error', 'Falha na consulta', null, error ?? 'Falha na consulta ao host.', SRC_ANALYSIS);
  const unknown = (id: IntegrationId, name: string): IntegrationRow =>
    row(id, name, 'unverified', 'Não verificado', null, NOT_VERIFIED_DETAIL, SRC_ANALYSIS);

  const ffmpegName = 'FFmpeg / FFprobe';
  const pythonName = 'Python (bridge e Whisper)';
  const whisperName = 'Whisper (transcrição)';
  const cudaName = 'Aceleração CUDA (GPU)';

  if (error) return [failed('ffmpeg', ffmpegName), failed('python', pythonName), failed('whisper', whisperName), failed('cuda', cudaName)];
  if (!env) return [unknown('ffmpeg', ffmpegName), unknown('python', pythonName), unknown('whisper', whisperName), unknown('cuda', cudaName)];

  const ffmpeg = env.ffmpegAvailable
    ? row('ffmpeg', ffmpegName, 'ready', 'Disponível', versionOrNull(env.ffmpegVersion), 'Metadados e proxies de mídia.', SRC_ANALYSIS)
    : row('ffmpeg', ffmpegName, 'missing', 'Não encontrado', null, 'Sem FFmpeg não há metadados, proxies nem forma de onda.', SRC_ANALYSIS);

  const python = env.pythonAvailable
    ? row('python', pythonName, 'ready', 'Disponível', null, 'Interpretador usado pelo Whisper e pela bridge do DaVinci.', SRC_ANALYSIS)
    : row('python', pythonName, 'missing', 'Não encontrado', null, 'Necessário para o Whisper e para a bridge do DaVinci.', SRC_ANALYSIS);

  const whisperDetail = env.whisperAvailable
    ? `Modelo: ${env.whisperModel || 'não informado'}.`
    : 'Transcrição indisponível sem Whisper instalado.';
  const whisper = env.whisperAvailable
    ? row('whisper', whisperName, 'ready', 'Disponível', versionOrNull(env.whisperEngine), whisperDetail, SRC_ANALYSIS)
    : row('whisper', whisperName, 'missing', 'Não encontrado', null, whisperDetail, SRC_ANALYSIS);

  const cuda =
    env.cudaAvailable === true
      ? row('cuda', cudaName, 'ready', 'Disponível (GPU)', null, 'Aceleração CUDA para o Whisper.', SRC_ANALYSIS)
      : env.cudaAvailable === false
        ? row('cuda', cudaName, 'limited', 'Indisponível (CPU)', null, 'A transcrição usará a CPU.', SRC_ANALYSIS)
        : unknown('cuda', cudaName);

  return [ffmpeg, python, whisper, cuda];
}

/**
 * Monta as linhas de integração na ordem de exibição. Só usa leituras reais recebidas por parâmetro;
 * em modo demonstração (navegador) .NET e WebView2 ficam "Não verificado" porque não existem aqui.
 */
export function buildIntegrationRows(inputs: IntegrationInputs): IntegrationRow[] {
  const dotnet = inputs.isMock
    ? row('dotnet', '.NET (runtime do aplicativo)', 'unverified', 'Não verificado', null,
        'Modo demonstração no navegador: o runtime .NET só existe no aplicativo.', SRC_NONE)
    : row('dotnet', '.NET (runtime do aplicativo)', 'ready', 'Em execução', null,
        'O aplicativo está em execução. A versão do runtime não é informada por esta tela.', SRC_APP);

  const webview2 = inputs.isMock
    ? row('webview2', 'WebView2', 'unverified', 'Não verificado', null,
        'Modo demonstração no navegador: sem WebView2 nesta sessão.', SRC_NONE)
    : row('webview2', 'WebView2', 'ready', 'Em execução', null,
        'Canal de mensagens com o host ativo nesta sessão.', SRC_APP);

  return [
    dotnet,
    webview2,
    davinciRow(inputs.editorCaps),
    ...analysisRows(inputs),
    motionRow(inputs.motionCaps, inputs.motionError),
    row('claude', 'Claude Code (MCP)', 'unverified', 'Não verificado pelo app', null,
      'Esta tela não consulta o MCP. Valide a conexão no computador host.', SRC_NONE)
  ];
}

export type IntegrationCounts = Record<IntegrationState, number>;

export function countByState(rows: readonly IntegrationRow[]): IntegrationCounts {
  const counts: IntegrationCounts = { ready: 0, limited: 0, simulated: 0, missing: 0, unverified: 0, error: 0 };
  for (const r of rows) counts[r.state] += 1;
  return counts;
}

const COUNT_TEXT: Record<IntegrationState, [string, string]> = {
  ready: ['pronto', 'prontos'],
  limited: ['com limitações', 'com limitações'],
  simulated: ['simulado', 'simulados'],
  missing: ['ausente', 'ausentes'],
  unverified: ['não verificado', 'não verificados'],
  error: ['com falha', 'com falha']
};

/** Resumo derivado das contagens reais, ex.: "2 prontos · 1 ausente · 3 não verificados". */
export function formatCounts(rows: readonly IntegrationRow[]): string {
  const counts = countByState(rows);
  const order: IntegrationState[] = ['ready', 'limited', 'simulated', 'missing', 'unverified', 'error'];
  const parts = order
    .filter((state) => counts[state] > 0)
    .map((state) => {
      const [singular, plural] = COUNT_TEXT[state];
      return `${counts[state]} ${counts[state] === 1 ? singular : plural}`;
    });
  return parts.length > 0 ? parts.join(' · ') : 'Nenhum item consultado.';
}

/**
 * Título geral. O Claude Code (MCP) não é consultado pelo app, então não entra na conta:
 * ele aparece como "não verificado pelo app" na própria linha.
 */
export function overallHeadline(rows: readonly IntegrationRow[]): { title: string; tone: EditorTone } {
  const checked = rows.filter((r) => r.id !== 'claude');
  const has = (state: IntegrationState): boolean => checked.some((r) => r.state === state);
  if (has('error')) return { title: 'Há falhas na consulta ao host', tone: 'rose' };
  if (has('missing')) return { title: 'Há itens ausentes', tone: 'rose' };
  if (has('unverified')) return { title: 'Verificação incompleta', tone: 'slate' };
  if (has('limited') || has('simulated')) return { title: 'Ambiente com limitações', tone: 'amber' };
  return { title: 'Ambiente pronto', tone: 'emerald' };
}
