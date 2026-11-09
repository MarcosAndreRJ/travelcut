import type {
  AnalysisEnvironmentDto,
  ImportCandidateDto,
  ImportDestination,
  ImportItemResult,
  ImportJobState,
  ImportJobStatusDto,
  MediaKind,
  MetadataStatus
} from '../../types/ipc';

// Utilitários puros da Tela 07 (importação). Nenhuma regra de negócio: apenas formatação,
// agrupamento de exibição e deduplicação visual. Valores null são exibidos como "—".

export interface ImportRow {
  /** Chave estável: caminho normalizado (pathKey). */
  key: string;
  item: ImportCandidateDto;
  selected: boolean;
}

export interface ImportOptions {
  copyFiles: boolean;
  generateThumbnails: boolean;
  analyzeMetadata: boolean;
  detectSpeech: boolean;
}

export const DEFAULT_IMPORT_OPTIONS: ImportOptions = {
  copyFiles: true,
  generateThumbnails: true,
  analyzeMetadata: true,
  detectSpeech: true
};

export const IMPORT_OPTIONS_STORAGE_KEY = 'travelcut.import.options';

/** Preferências locais da tela. detectSpeech fica de fora: a escolha de automação é persistida no host (AutoProcess). */
export type LocalImportOptions = Omit<ImportOptions, 'detectSpeech'>;

export const loadLocalImportOptions = (): Partial<LocalImportOptions> => {
  try {
    const raw = localStorage.getItem(IMPORT_OPTIONS_STORAGE_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    const out: Partial<LocalImportOptions> = {};
    if (typeof parsed.copyFiles === 'boolean') out.copyFiles = parsed.copyFiles;
    if (typeof parsed.generateThumbnails === 'boolean') out.generateThumbnails = parsed.generateThumbnails;
    if (typeof parsed.analyzeMetadata === 'boolean') out.analyzeMetadata = parsed.analyzeMetadata;
    return out;
  } catch {
    return {};
  }
};

export const saveLocalImportOptions = (options: ImportOptions): void => {
  const local: LocalImportOptions = {
    copyFiles: options.copyFiles,
    generateThumbnails: options.generateThumbnails,
    analyzeMetadata: options.analyzeMetadata
  };
  try {
    localStorage.setItem(IMPORT_OPTIONS_STORAGE_KEY, JSON.stringify(local));
  } catch {
    // Armazenamento indisponível: a escolha vale apenas nesta sessão.
  }
};

export type AnalysisToggleKey = 'generateThumbnails' | 'analyzeMetadata' | 'detectSpeech';

export interface AnalysisCapabilities {
  /** false enquanto o ambiente não foi lido ou falhou ao ser lido. */
  ready: boolean;
  /** Whisper roda em CPU (sem CUDA): a transcrição será mais lenta. */
  cpuFallback: boolean;
  /** Motivo de bloqueio por opção; ausente = disponível. */
  blocked: Partial<Record<AnalysisToggleKey, string>>;
}

const FFMPEG_MISSING_REASON = 'FFmpeg não encontrado — configure em Configurações';

/** Converte o ambiente lido do host em bloqueios de opção. Sem leitura, nada de análise é oferecido. */
export const resolveAnalysisCapabilities = (
  environment: AnalysisEnvironmentDto | null,
  environmentError: boolean
): AnalysisCapabilities => {
  if (environment === null) {
    const reason = environmentError
      ? 'Não foi possível verificar o ambiente de análise'
      : 'Verificando o ambiente de análise…';
    return {
      ready: false,
      cpuFallback: false,
      blocked: { generateThumbnails: reason, analyzeMetadata: reason, detectSpeech: reason }
    };
  }
  const blocked: Partial<Record<AnalysisToggleKey, string>> = {};
  if (!environment.ffmpegAvailable) {
    blocked.generateThumbnails = FFMPEG_MISSING_REASON;
    blocked.analyzeMetadata = FFMPEG_MISSING_REASON;
    blocked.detectSpeech = FFMPEG_MISSING_REASON;
  } else if (!environment.whisperAvailable) {
    blocked.detectSpeech = environment.whisperUnavailableReason || 'Whisper indisponível neste ambiente';
  }
  return {
    ready: true,
    cpuFallback: environment.cpuFallback === true && blocked.detectSpeech === undefined,
    blocked
  };
};

/** Opções efetivas enviadas ao host: opções bloqueadas viram false; metadados são forçados com fala ou miniaturas. */
export const effectiveImportOptions = (options: ImportOptions, caps: AnalysisCapabilities): ImportOptions => {
  const generateThumbnails = options.generateThumbnails && caps.blocked.generateThumbnails === undefined;
  const detectSpeech = options.detectSpeech && caps.blocked.detectSpeech === undefined;
  const analyzeMetadata =
    caps.blocked.analyzeMetadata === undefined && (options.analyzeMetadata || generateThumbnails || detectSpeech);
  return { copyFiles: options.copyFiles, generateThumbnails, analyzeMetadata, detectSpeech };
};

/** Motivos de bloqueio exibidos nos toggles (inclui metadados obrigatórios). */
export const importToggleLocks = (
  effective: ImportOptions,
  caps: AnalysisCapabilities
): Partial<Record<keyof ImportOptions, string>> => {
  const locks: Partial<Record<keyof ImportOptions, string>> = { ...caps.blocked };
  if (locks.analyzeMetadata === undefined && (effective.generateThumbnails || effective.detectSpeech)) {
    locks.analyzeMetadata = 'Obrigatória enquanto transcrição ou miniaturas estiverem ligadas';
  }
  return locks;
};

export const IMPORT_DESTINATIONS: ReadonlyArray<ImportDestination> = ['1-Bruto', '2-Assets'];

export const TERMINAL_JOB_STATES: ReadonlyArray<ImportJobState> = [
  'completed',
  'completed_with_errors',
  'failed',
  'canceled'
];

export const isTerminalJobState = (state: ImportJobState): boolean => TERMINAL_JOB_STATES.includes(state);

const SUCCESSFUL_RESULTS: ReadonlyArray<ImportItemResult> = [
  'imported',
  'renamed',
  'registered_in_place',
  'skipped_duplicate'
];

export const isSuccessfulResult = (result: ImportItemResult): boolean => SUCCESSFUL_RESULTS.includes(result);

export const IMPORT_RESULT_LABEL: Record<ImportItemResult, string> = {
  pending: 'Aguardando',
  copying: 'Copiando',
  imported: 'Importado',
  renamed: 'Importado com novo nome',
  registered_in_place: 'Registrado no local',
  skipped_duplicate: 'Já existia (idêntico)',
  failed: 'Falhou',
  canceled: 'Cancelado'
};

export const IMPORT_STAGE_LABEL: Record<ImportJobStatusDto['stage'], string> = {
  preflight: 'Verificação prévia',
  copy: 'Copiando arquivos',
  verify: 'Verificando cópias',
  metadata: 'Analisando metadados',
  thumbnails: 'Gerando miniaturas',
  register: 'Registrando na biblioteca',
  done: 'Concluído'
};

export const IMPORT_JOB_STATE_LABEL: Record<ImportJobState, string> = {
  queued: 'Na fila',
  running: 'Em andamento',
  completed: 'Concluída',
  completed_with_errors: 'Concluída com erros',
  failed: 'Falhou',
  canceled: 'Cancelada'
};

export const METADATA_STATUS_REASON: Record<MetadataStatus, string> = {
  ok: 'Metadados lidos',
  ffprobe_unavailable: 'FFprobe indisponível — configure o FFmpeg em Configurações',
  failed: 'Falha ao ler metadados deste arquivo',
  not_requested: 'Metadados não solicitados nesta importação'
};

export const messageOf = (err: unknown): string => (err instanceof Error ? err.message : String(err));

/** Chave de comparação de caminhos (Windows é case-insensitive; barra final ignorada). */
export const pathKey = (path: string): string => path.trim().replace(/[\\/]+$/, '').toLowerCase();

export const basenameOf = (path: string): string => path.split(/[\\/]/).filter(Boolean).pop() ?? path;

export const dirnameOf = (path: string): string => {
  const idx = Math.max(path.lastIndexOf('\\'), path.lastIndexOf('/'));
  if (idx < 0) return path;
  if (idx === 2 && /^[A-Za-z]:$/.test(path.slice(0, 2))) return path.slice(0, 3);
  return path.slice(0, idx);
};

/** Pasta pai; retorna null quando já está na raiz (unidade) ou não há pai. */
export const parentDirectoryOf = (path: string): string | null => {
  const trimmed = path.replace(/[\\/]+$/, '');
  const idx = Math.max(trimmed.lastIndexOf('\\'), trimmed.lastIndexOf('/'));
  if (idx < 0) return null;
  if (idx === 2 && /^[A-Za-z]:$/.test(trimmed.slice(0, 2))) return trimmed.slice(0, 3);
  const parent = trimmed.slice(0, idx);
  return parent.length > 0 ? parent : null;
};

export const joinPath = (base: string, child: string): string => {
  const sep = base.includes('\\') ? '\\' : '/';
  return `${base.replace(/[\\/]+$/, '')}${sep}${child}`;
};

export const appendCandidates = (prev: ImportRow[], items: ImportCandidateDto[]): ImportRow[] => {
  const keys = new Set(prev.map((row) => row.key));
  const additions: ImportRow[] = [];
  for (const item of items) {
    const key = pathKey(item.fullPath);
    if (keys.has(key)) continue;
    keys.add(key);
    additions.push({ key, item, selected: true });
  }
  return additions.length > 0 ? [...prev, ...additions] : prev;
};

const fmt = (value: number, digits: number): string =>
  value.toLocaleString('pt-BR', { minimumFractionDigits: digits, maximumFractionDigits: digits });

/** hh:mm:ss; null quando desconhecido. */
export const formatDurationClock = (seconds: number | null | undefined): string | null => {
  if (seconds === null || seconds === undefined || !Number.isFinite(seconds) || seconds < 0) return null;
  const total = Math.round(seconds);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return [h, m, s].map((v) => v.toString().padStart(2, '0')).join(':');
};

/** Texto longo para totais: "27min 20s" ou "1h 12min 05s". */
export const formatDurationLong = (seconds: number): string => {
  const total = Math.round(seconds);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return h > 0 ? `${h}h ${m}min ${s}s` : `${m}min ${s}s`;
};

export const formatBytes = (bytes: number): string => {
  const GB = 1024 ** 3;
  const MB = 1024 ** 2;
  const KB = 1024;
  if (bytes >= GB) return `${fmt(bytes / GB, 1)} GB`;
  if (bytes >= MB) return `${fmt(bytes / MB, 0)} MB`;
  if (bytes >= KB) return `${fmt(bytes / KB, 0)} KB`;
  return `${bytes} B`;
};

/** Rótulo por altura: ≥2160 4K, ≥1440 QHD, ≥1080 FHD, ≥720 HD; abaixo disso não há rótulo. */
export const resolutionTier = (height: number): string | null => {
  if (height >= 2160) return '4K';
  if (height >= 1440) return 'QHD';
  if (height >= 1080) return 'FHD';
  if (height >= 720) return 'HD';
  return null;
};

export const formatResolution = (width: number | null, height: number | null): string | null => {
  if (!width || !height) return null;
  const tier = resolutionTier(height);
  return tier ? `${width} × ${height} (${tier})` : `${width} × ${height}`;
};

const extensionOf = (fileName: string): string => {
  const idx = fileName.lastIndexOf('.');
  return idx >= 0 ? fileName.slice(idx + 1).toLowerCase() : '';
};

const KIND_BASE_LABEL: Record<MediaKind, string> = {
  Video: 'Vídeo',
  Audio: 'Áudio',
  Image: 'Imagem'
};

/** "Vídeo (MP4)" a partir de kind + container (fallback: extensão do arquivo). */
export const kindLabel = (kind: MediaKind, container: string, fileName: string): string => {
  const base = KIND_BASE_LABEL[kind] ?? 'Arquivo';
  const format = (container || extensionOf(fileName)).split(',')[0].trim().replace(/^\./, '').toUpperCase();
  return format ? `${base} (${format})` : base;
};

export interface ResolutionGroup {
  label: string;
  count: number;
}

export interface ImportSummary {
  counts: Record<MediaKind, number>;
  total: number;
  durationSecondsTotal: number;
  hasKnownDuration: boolean;
  /** Vídeos/áudios sem duração conhecida (não entram no total). */
  unknownDurationCount: number;
  bytesTotal: number;
  resolutionGroups: ResolutionGroup[];
  unknownResolutionCount: number;
}

const RESOLUTION_ORDER = ['4K', 'QHD', 'FHD', 'HD'];

const resolutionRank = (label: string): number => {
  const idx = RESOLUTION_ORDER.indexOf(label);
  if (idx >= 0) return idx;
  // Abaixo de 720p: rótulo "NNNp", do maior para o menor.
  return 100 + (10000 - parseInt(label, 10));
};

export const summarizeItems = (items: ImportCandidateDto[]): ImportSummary => {
  const counts: Record<MediaKind, number> = { Video: 0, Audio: 0, Image: 0 };
  let durationSecondsTotal = 0;
  let hasKnownDuration = false;
  let unknownDurationCount = 0;
  let bytesTotal = 0;
  let unknownResolutionCount = 0;
  const groups = new Map<string, number>();

  for (const item of items) {
    counts[item.kind] = (counts[item.kind] ?? 0) + 1;
    bytesTotal += item.byteLength || 0;

    if (item.kind !== 'Image') {
      if (item.durationSeconds === null || item.durationSeconds === undefined) {
        unknownDurationCount += 1;
      } else {
        durationSecondsTotal += item.durationSeconds;
        hasKnownDuration = true;
      }
    }

    if (item.kind === 'Video') {
      if (item.height) {
        const label = resolutionTier(item.height) ?? `${item.height}p`;
        groups.set(label, (groups.get(label) ?? 0) + 1);
      } else {
        unknownResolutionCount += 1;
      }
    }
  }

  const resolutionGroups = [...groups.entries()]
    .map(([label, count]) => ({ label, count }))
    .sort((a, b) => resolutionRank(a.label) - resolutionRank(b.label));

  return {
    counts,
    total: items.length,
    durationSecondsTotal,
    hasKnownDuration,
    unknownDurationCount,
    bytesTotal,
    resolutionGroups,
    unknownResolutionCount
  };
};

export const formatResolutionGroups = (groups: ResolutionGroup[]): string =>
  groups.map((group) => `${group.label} (${group.count})`).join(' • ');

export const kindPlural = (kind: MediaKind, count: number): string => {
  const one = count === 1;
  if (kind === 'Video') return one ? 'vídeo' : 'vídeos';
  if (kind === 'Audio') return one ? 'áudio' : 'áudios';
  return one ? 'imagem' : 'imagens';
};

export const pluralItems = (count: number): string => (count === 1 ? 'item' : 'itens');
