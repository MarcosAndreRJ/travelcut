import {
  IpcRequest,
  IpcResponse,
  ProjectSummary,
  EditSuggestion,
  EditorCapabilities,
  MediaAssetDto,
  ScanMediaResultDto,
  MediaDetailsDto,
  ChronologyReportDto,
  DirectoryEntryDto,
  ImportCandidateDto,
  ImportItemStatusDto,
  ImportItemResult,
  ImportJobStatusDto,
  InspectCandidatesResponse,
  ListDirectoryResponse,
  MediaKind,
  SelectFilesResponse,
  StartImportRequest,
  ThumbnailResponse,
  AnalysisEnvironmentDto,
  MediaAnalysisDto,
  PipelineStageStatusDto,
  PipelineStatusDto,
  PipelineDiagnosticsDto,
  PipelineStage,
  PipelineMediaStatusDto,
  StageState,
  StartPipelineRequest,
  RetryFailedPipelineResponse,
  EditPlanDto,
  EditorialProfileDto,
  AutomationProfile,
  AutomationProfileDto,
  AutomationOptionsDto,
  PlanAppliedCutDto,
  PlanClipDto,
  PlanIssueDto,
  PlanPendingItemDto,
  PlanRevisionInfoDto,
  PlanSectionDto,
  PlanTrackDto,
  PlanTransitionDto,
  PlanEditorialOperationDto,
  ReconcilePlanResponse,
  PlanValidationDto,
  TimelineTimeBaseDto,
  BatchDecideRequest,
  DecisionParametersDto,
  DecisionAction,
  DecisionResultDto,
  EditDecisionDto,
  SubmitDecisionRequest,
  ApplyEditorPlanRequest,
  EditorApplyPreview,
  EditorApplyResult,
  EditorClipMapping,
  EditorClipPreview,
  EditorOperation,
  EditorOperationCapability,
  EditorRejectedOp,
  EditorStepResult,
  EditorWarning,
  PrepareEditorApplyRequest,
  MotionCapabilitiesDto,
  ToolDiagnosticDto,
  EnvironmentAssistantDiagnosticDto,
  EnvironmentInstallPlanDto,
  ToolInstallProgressDto,
  StartToolInstallRequest,
  StartModelDownloadRequest,
  TestToolResponse,
  AutoLocateResponse,
  TravelCutSettingsDto
} from '../types/ipc';
import { revisionLabel, timelineFrameRateLabel } from '../components/editor/editorModel';

const DEFAULT_IPC_TIMEOUT_MS = 10000;
const INSPECT_IPC_TIMEOUT_MS = 120000;
const THUMBNAIL_IPC_TIMEOUT_MS = 30000;
const PLAN_IPC_TIMEOUT_MS = 30000;
/** Decisões (submit/undo/redo/lote) regeneram o plano no host: mesmo limite de plan.* (30 s). */
const DECISION_IPC_TIMEOUT_MS = PLAN_IPC_TIMEOUT_MS;
/**
 * project.open/create podem demorar (validação de pastas, leitura de mídias, criação de banco).
 * Após o limite o host pode ainda estar processando: a UI deve dizer isso, não tratar como falha certa.
 */
export const PROJECT_IPC_TIMEOUT_MS = 120000;
/**
 * environment.startInstall/startDownloadModel só respondem depois da confirmação nativa do host.
 * Com o timeout padrão, um usuário lendo o diálogo por mais de 10 s perdia o jobId e o job seguia sem acompanhamento.
 */
export const NATIVE_CONFIRMATION_IPC_TIMEOUT_MS = 600000;
/** analysis.environment pode rodar o probe do Python (até 120 s no host); com 10 s a primeira consulta expirava. */
export const ENVIRONMENT_IPC_TIMEOUT_MS = 150000;
/** Código estável de IpcError quando a resposta do host não chega no prazo. */
export const IPC_TIMEOUT_CODE = 'IPC_TIMEOUT';
const MOCK_IMPORT_DURATION_MS = 4000;
const MOCK_PIPELINE_DURATION_MS = 1200;
const MOCK_DEMO_ROOT = 'D:\\Videos\\demo-project';
/**
 * DEMONSTRAÇÃO: a lista de candidatos fica vazia por padrão, pois no modo mock as etapas
 * de transcrição e silêncio terminam 'unavailable' (sem candidatos reais). Troque para true
 * apenas para inspecionar visualmente a tela de Revisão com exemplos rotulados.
 */
const MOCK_SHOW_SAMPLE_CANDIDATES = false;
/** DEMONSTRAÇÃO: base de tempo do plano mock (29,97 fps, NDF). */
const MOCK_PLAN_TIMEBASE: TimelineTimeBaseDto = {
  frameDurationNum: 1001,
  frameDurationDen: 30000,
  label: 'DEMO 29,97 fps'
};

/**
 * DEMONSTRAÇÃO: capacidades do simulador do editor (editor/1). Nada é enviado ao DaVinci; nenhuma
 * operação tem verificação "host_pass" aqui. Versão e produto ficam null (não lidos de nenhuma API).
 */
const MOCK_EDITOR_OPERATIONS: EditorOperationCapability[] = [
  { operation: 'detect_capabilities', supported: true, verification: 'mock_pass', note: 'DEMO: resposta local do simulador.' },
  { operation: 'create_project', supported: true, verification: 'mock_pass', note: 'DEMO: nenhum projeto é criado no DaVinci.' },
  { operation: 'import_media', supported: true, verification: 'mock_pass', note: 'DEMO: etapa simulada.' },
  { operation: 'create_timeline', supported: true, verification: 'mock_pass', note: 'DEMO: etapa simulada.' },
  { operation: 'apply_edit_plan', supported: true, verification: 'mock_pass', note: 'DEMO: etapa simulada; a homologação depende do host.' },
  { operation: 'query_timeline', supported: false, verification: 'host_pending', note: 'Conferência da timeline real: a homologar no host.' },
  { operation: 'export_preview', supported: false, verification: 'unsupported', note: 'DEMO: o simulador não renderiza prévia (no DaVinci real: a homologar no host).' },
  { operation: 'export_project', supported: false, verification: 'host_pending', note: 'Exportação .drp real: a homologar no host.' },
  { operation: 'create_snapshot', supported: false, verification: 'host_pending', note: 'Snapshot .drp real: a homologar no host.' }
];

const MOCK_APPLY_STEP_OPERATIONS: EditorOperation[] = ['create_project', 'import_media', 'create_timeline', 'apply_edit_plan'];

/**
 * DEMONSTRAÇÃO: capacidades de animação (motion/1). Simulador: nenhum vídeo é gerado e nenhuma versão
 * de renderer é inventada (rendererVersion/nodeVersion ficam null, como no host sem leitura).
 */
function mockMotionCapabilities(): MotionCapabilitiesDto {
  return {
    schemaVersion: 'motion/1',
    adapterMode: 'mock',
    environmentState: 'MOCK',
    isAvailable: true,
    isSimulated: true,
    nodeVersion: null,
    rendererVersion: null,
    statusMessage: 'DEMO: simulador de animações. Nenhum arquivo de vídeo é gerado.',
    reasonCode: null,
    templates: ['title_card', 'lower_third', 'map_route'].map((templateId) => ({
      templateId,
      supported: true,
      verification: 'mock_pass',
      note: 'DEMO: somente simulação.'
    })),
    checkedAtUtc: new Date().toISOString()
  };
}

function mockEditorCapabilities(): EditorCapabilities {
  const supported = (op: EditorOperation): boolean =>
    MOCK_EDITOR_OPERATIONS.some((o) => o.operation === op && o.supported);
  return {
    schemaVersion: 'editor/1',
    adapterMode: 'mock',
    environmentState: 'MOCK',
    isAvailable: true,
    isSimulated: true,
    productName: null,
    version: null,
    statusMessage: 'DEMO: simulador do adaptador do DaVinci. Nada é enviado ao DaVinci Resolve.',
    reasonCode: null,
    operations: MOCK_EDITOR_OPERATIONS.map((o) => ({ ...o })),
    checkedAtUtc: new Date().toISOString(),
    canCreateProject: supported('create_project'),
    canImportMedia: supported('import_media'),
    canCreateTimeline: supported('create_timeline'),
    canApplyEditPlan: supported('apply_edit_plan'),
    canExportPreview: supported('export_preview')
  };
}

/** DEMONSTRAÇÃO: impressão simplificada do manifesto (não é o hash canônico do host). */
const mockAssemblyFingerprintOf = (planFingerprint: string, clipCount: number): string =>
  `demo-asm-${planFingerprint}-c${clipCount}`;

const MOCK_IDEMPOTENCY_KEY_PATTERN = /^[A-Za-z0-9_-]{1,100}$/;

/** Payload de plan.generate (profile e timeBase reservados; hoje sempre null). */
export interface GenerateEditPlanRequest {
  projectId: string;
  profile?: EditorialProfileDto | null;
  timeBase?: null;
}

/** Resposta de plan.list. */
export interface PlanRevisionsResponse {
  revisions: PlanRevisionInfoDto[];
}

const clonePlanData = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

/** Ações aceitas por decision.submit (modify = aprovar com intervalo ajustado). */
const MOCK_DECISION_ACTIONS: readonly DecisionAction[] = ['approve', 'reject', 'defer', 'modify'];
/** Ações aceitas por suggestions.batchDecide (modify proibido no lote). */
const MOCK_BATCH_ACTIONS: readonly DecisionAction[] = ['approve', 'reject', 'defer'];

/** DEMONSTRAÇÃO: estado efetivo de um candidato no mock de decisões. */
interface MockDecisionState {
  status: EditSuggestion['status'];
  effectiveStartSeconds: number | null;
  effectiveEndSeconds: number | null;
  /** Ação que produziu este estado; redo regrava essa ação original. */
  lastAction: DecisionAction | null;
  /** F14.8: parâmetros (zoom) da decisão; herdados por decisões seguintes, como o intervalo. */
  parameters?: DecisionParametersDto | null;
}

/** DEMONSTRAÇÃO (espelho do host): zoom só de sugestões de zoom; escala limitada a 1,01–1,12; foco em 0–1. */
function mockNormalizeParameters(
  parameters: DecisionParametersDto | null | undefined,
  suggestedAction: string
): DecisionParametersDto | null {
  const zoom = parameters?.zoom;
  if (!zoom) return null;
  if (suggestedAction !== 'zoom') {
    throw new IpcError('DEMO: parameters.zoom só se aplica a sugestões de zoom.', 'INVALID_ARGUMENT');
  }
  const ok = (v: unknown) => typeof v === 'number' && Number.isFinite(v);
  if (!ok(zoom.scale) || zoom.scale <= 0) {
    throw new IpcError('DEMO: parameters.zoom.scale inválida.', 'INVALID_ARGUMENT');
  }
  if (!ok(zoom.focalPointX) || !ok(zoom.focalPointY) ||
      zoom.focalPointX < 0 || zoom.focalPointX > 1 || zoom.focalPointY < 0 || zoom.focalPointY > 1) {
    throw new IpcError('DEMO: ponto focal deve estar entre 0 e 1.', 'INVALID_ARGUMENT');
  }
  if (!ok(zoom.durationSeconds) || zoom.durationSeconds < 0) {
    throw new IpcError('DEMO: parameters.zoom.durationSeconds inválida.', 'INVALID_ARGUMENT');
  }
  const scale = Math.min(1.12, Math.max(1.01, zoom.scale));
  return { zoom: { ...zoom, zoomType: zoom.zoomType || 'punch_in', scale, startScale: 1, endScale: scale } };
}

/** Erro do host com código estável (ex.: PIPELINE_IN_PROGRESS). */
export class IpcError extends Error {
  readonly code: string | null;

  constructor(message: string, code: string | null = null) {
    super(message);
    this.name = 'IpcError';
    this.code = code;
  }
}

const MOCK_PIPELINE_STAGE_ORDER: { stage: PipelineStage; label: string }[] = [
  { stage: 'transcription', label: 'Transcrição (Whisper)' },
  { stage: 'silence', label: 'Detecção de silêncio' },
  { stage: 'hesitation', label: 'Hesitações' },
  { stage: 'scene', label: 'Classificação de cena' },
  { stage: 'candidates', label: 'Candidatos a corte' }
];

interface MockPipelineJob {
  jobId: string;
  mediaIds: string[] | null;
  startedAt: number;
  canceledAt: number | null;
  /** true depois que o resultado de demonstração foi aplicado às mídias analisadas. */
  applied: boolean;
}

interface MockImportJob {
  jobId: string;
  request: StartImportRequest;
  startedAt: number;
  canceledAt: number | null;
  /** true depois que os itens concluídos entraram na biblioteca mock. */
  applied: boolean;
}

const MOCK_VIDEO_EXT = ['mp4', 'mov', 'mts', 'm2ts', 'mxf', 'avi', 'mkv', 'insv'];
const MOCK_AUDIO_EXT = ['wav', 'mp3', 'm4a', 'aac', 'flac'];
const MOCK_IMAGE_EXT = ['jpg', 'jpeg', 'png', 'heic', 'dng', 'arw', 'nef'];

const mockBasename = (path: string): string => path.split(/[\\/]/).filter(Boolean).pop() ?? path;

const mockParent = (path: string): string | null => {
  const trimmed = path.replace(/[\\/]+$/, '');
  const idx = Math.max(trimmed.lastIndexOf('\\'), trimmed.lastIndexOf('/'));
  if (idx < 0) return null;
  if (idx === 2) return trimmed.slice(0, 3);
  return trimmed.slice(0, idx) || null;
};

const mockExtension = (name: string): string => {
  const idx = name.lastIndexOf('.');
  return idx >= 0 ? name.slice(idx + 1).toLowerCase() : '';
};

const mockKindOf = (name: string): MediaKind | null => {
  const ext = mockExtension(name);
  if (MOCK_VIDEO_EXT.includes(ext)) return 'Video';
  if (MOCK_AUDIO_EXT.includes(ext)) return 'Audio';
  if (MOCK_IMAGE_EXT.includes(ext)) return 'Image';
  return null;
};

const mockHash = (text: string): number => {
  let h = 0;
  for (let i = 0; i < text.length; i++) h = (h * 31 + text.charCodeAt(i)) >>> 0;
  return h;
};

const mockJoin = (dir: string, name: string): string => `${dir.replace(/[\\/]+$/, '')}\\${name}`;

const mockNormalize = (path: string): string => path.replace(/[\\/]+$/, '').toLowerCase();

const mockCandidate = (fullPath: string): ImportCandidateDto => {
  const fileName = mockBasename(fullPath);
  const kind: MediaKind = mockKindOf(fileName) ?? 'Video';
  const h = mockHash(fullPath);
  const is4k = h % 3 === 0;
  const parent = mockParent(fullPath);
  return {
    fullPath,
    fileName,
    originFolder: parent ? mockBasename(parent) : '',
    kind,
    container: mockExtension(fileName),
    byteLength: 40_000_000 + (h % 1_800_000_000),
    durationSeconds: kind === 'Image' ? null : 20 + (h % 400) + 0.5,
    width: kind === 'Video' ? (is4k ? 3840 : 1920) : null,
    height: kind === 'Video' ? (is4k ? 2160 : 1080) : null,
    codec: kind === 'Audio' ? 'pcm_s24le' : kind === 'Video' ? 'hevc' : null,
    metadataStatus: 'ok',
    insideProject: false,
    alreadyInLibrary: false
  };
};

const mockAssetFromCandidate = (item: ImportCandidateDto, projectId: string, destination: string): MediaAssetDto => ({
  mediaId: `mock_${mockHash(item.fullPath).toString(16)}`,
  projectId,
  relativePath: `${destination}/${item.fileName}`,
  byteLength: item.byteLength,
  kind: item.kind.toLowerCase(),
  codec: item.codec ?? '',
  width: item.width ?? 0,
  height: item.height ?? 0,
  fps: 0,
  durationSeconds: item.durationSeconds ?? 0,
  creationTimeUtc: null,
  hasTranscript: false,
  hasVAD: false,
  sceneClassification: null,
  thumbnailUrl: null,
  tags: [],
  metadataStatus: item.metadataStatus
});

const mockDirEntry = (dir: string, name: string): DirectoryEntryDto => ({
  name,
  fullPath: mockJoin(dir, name),
  isDirectory: true,
  byteLength: null,
  kind: null
});

const mockFileEntry = (dir: string, name: string, kind: MediaKind | null, byteLength: number): DirectoryEntryDto => ({
  name,
  fullPath: mockJoin(dir, name),
  isDirectory: false,
  byteLength,
  kind
});

const mockDirectoryEntries = (path: string): DirectoryEntryDto[] => {
  const norm = mockNormalize(path);
  if (norm === mockNormalize(MOCK_DEMO_ROOT)) {
    return [
      mockDirEntry(path, '1-Bruto'),
      mockDirEntry(path, '2-Assets'),
      mockDirEntry(path, '3-Trabalho'),
      mockDirEntry(path, '4-Final'),
      mockFileEntry(path, 'DEMO_CLIP_0001.MP4', 'Video', 1_200_000_000),
      mockFileEntry(path, 'DEMO_CLIP_0002.MOV', 'Video', 2_400_000_000)
    ];
  }
  if (norm === mockNormalize(mockParent(MOCK_DEMO_ROOT) ?? '')) {
    return [mockDirEntry(path, 'demo-project'), mockDirEntry(path, 'Outras-Viagens')];
  }
  return [
    mockDirEntry(path, 'DEMO-Pasta-A'),
    mockDirEntry(path, 'DEMO-Pasta-B'),
    mockFileEntry(path, 'DEMO_CLIP_0003.MP4', 'Video', 800_000_000),
    mockFileEntry(path, 'DEMO_TRILHA.WAV', 'Audio', 180_000_000),
    mockFileEntry(path, 'DEMO_leia-me.txt', null, 120)
  ];
};

class IpcBridgeService {
  private handlers = new Map<string, { resolve: (val: any) => void; reject: (err: any) => void; timer: ReturnType<typeof setTimeout> }>();
  private isWebView2Available: boolean;
  private mockImportJobs = new Map<string, MockImportJob>();
  // Projeto criado em modo mock começa vazio; a biblioteca cresce com as importações mock concluídas.
  private mockFreshProject = false;
  private mockImportedAssets: MediaAssetDto[] = [];
  private mockPipeline: MockPipelineJob | null = null;
  private mockAnalyzedMediaIds = new Set<string>();
  // DEMONSTRAÇÃO: revisões do plano editorial; cada plan.generate/plan.revert acrescenta uma revisão.
  private mockPlanRevisions: EditPlanDto[] = [];
  // Perfil de automação do projeto mock (null = nunca escolhido → 'balanced', isDefault=true).
  private mockAutomationProfile: AutomationProfile | null = null;
  private mockAutoProcess: boolean | null = null;
  private mockProfile: EditorialProfileDto = {
    profileId: 'demo-profile',
    version: 1,
    silencePolicy: 'balanced',
    pace: 'normal',
    jokePreservation: 'strict',
    zoomStrength: 'subtle',
    speedBroll: '2x',
    musicPolicy: 'suggest_only',
    reviewAmbiguous: true,
    autoApplyTrivialCuts: false
  };

  // Mock State for browser testing or fallback
  private mockProjectSummary: ProjectSummary = {
    projectId: 'e5b7a192-3c82-4f15-8f6a-93a0b5f190c1',
    name: 'Expedição Patagônia 2026',
    rootPath: 'D:\\Videos\\demo-project',
    fourFoldersValid: true,
    missingFolder: undefined,
    mediaCount: 42,
    suggestionsCount: 18,
    approvedCutsCount: 7,
    totalOriginalDurationSeconds: 4320, // 1h 12m
    estimatedCutDurationSeconds: null, // sem cortes aprovados no mock
    lastModifiedUtc: new Date().toISOString()
  };

  private mockSuggestions: EditSuggestion[] = [
    {
      suggestionId: 'sug-001',
      mediaId: 'med-01',
      startSeconds: 14.2,
      endSeconds: 17.8,
      kind: 'silence',
      rationale: 'Silêncio prolongado detectado (3.6s) após início de gravação da trilha.',
      confidenceBand: 'high',
      needsHumanReview: false,
      suggestedAction: 'cut',
      status: 'Proposed',
      author: 'rules-engine-vad'
    },
    {
      suggestionId: 'sug-002',
      mediaId: 'med-01',
      startSeconds: 45.1,
      endSeconds: 47.3,
      kind: 'filler',
      rationale: '[DEMO] trecho de exemplo: hesitação verbal sinalizada pelo mock (sem transcrição real).',
      confidenceBand: 'medium',
      needsHumanReview: true,
      suggestedAction: 'cut',
      status: 'Approved',
      author: 'rules-engine-nlp'
    },
    {
      suggestionId: 'sug-003',
      mediaId: 'med-04',
      startSeconds: 2.0,
      endSeconds: 18.5,
      kind: 'speedbroll',
      rationale: 'Plano longo de caminhada na montanha sem diálogo (16.5s). Sugerida aceleração 2x.',
      confidenceBand: 'medium',
      needsHumanReview: true,
      suggestedAction: 'speed2x',
      status: 'Proposed',
      author: 'rules-engine-visual'
    },
    {
      suggestionId: 'sug-004',
      mediaId: 'med-07',
      startSeconds: 112.0,
      endSeconds: 115.4,
      kind: 'silence',
      rationale: 'Pausa morta de respiração entre tópicos do diário de bordo (3.4s).',
      confidenceBand: 'high',
      needsHumanReview: false,
      suggestedAction: 'cut',
      status: 'Proposed',
      author: 'rules-engine-vad'
    },
    {
      suggestionId: 'sug-005',
      mediaId: 'med-12',
      startSeconds: 34.0,
      endSeconds: 34.0,
      kind: 'subtlezoom',
      rationale: 'Corte consecutivo no mesmo enquadramento fixo. Aplicar salto sutil de escala 1.05x para suavizar jump cut.',
      confidenceBand: 'high',
      needsHumanReview: false,
      suggestedAction: 'zoom_105',
      status: 'Approved',
      author: 'rules-engine-visual'
    }
  ];

  // DEMONSTRAÇÃO: decisões em memória. Estado efetivo por candidato, pilhas de undo/redo e log append-only.
  private mockDecisionStates = new Map<string, MockDecisionState>();
  private mockDecisionPast = new Map<string, MockDecisionState[]>();
  private mockDecisionFuture = new Map<string, MockDecisionState[]>();
  private mockDecisionLog: EditDecisionDto[] = [];
  private mockDecisionSeq = 0;
  private mockDecisionVersionOf = new Map<string, number>();

  private mockCapabilities: EditorCapabilities = mockEditorCapabilities();
  // DEMONSTRAÇÃO: envios simulados em memória (mais recente primeiro), como o registro em 3-Trabalho/editor.
  private mockApplications: EditorApplyResult[] = [];
  private mockApplicationSeq = 0;

  private mockSettings: TravelCutSettingsDto = {
    configuredToolPaths: {
      python: 'C:\\Python311\\python.exe',
      ffmpeg: 'C:\\ffmpeg\\bin\\ffmpeg.exe',
      node: 'C:\\Program Files\\nodejs\\node.exe',
      hyperframes: 'C:\\Users\\User\\AppData\\Local\\TravelCutStudio\\tools\\hyperframes\\node_modules\\hyperframes\\dist\\cli.js'
    },
    mediaSourceFolders: ['D:\\Videos\\Gopro', 'D:\\Videos\\Drone'],
    whisperModel: 'large-v3',
    preferCuda: true
  };
  private mockInstallJobs = new Map<string, { toolKey: string; startedAt: number; canceled: boolean }>();
  private mockDownloadJobs = new Map<string, { modelName: string; startedAt: number; canceled: boolean }>();

  private mockMediaAssets: MediaAssetDto[] = [
    {
      mediaId: 'med-01',
      projectId: 'e5b7a192-3c82-4f15-8f6a-93a0b5f190c1',
      relativePath: '1-Bruto/A-Roll/DJI_0042_ChegadaAeroporto_4K.MP4',
      byteLength: 1932735283,
      kind: 'video',
      codec: 'hevc',
      width: 3840,
      height: 2160,
      fps: 59.94,
      durationSeconds: 165.2,
      creationTimeUtc: '2026-04-12T08:30:00Z',
      hasTranscript: false,
      hasVAD: false,
      sceneClassification: null,
      thumbnailUrl: '/assets/8.webp'
    },
    {
      mediaId: 'med-02',
      projectId: 'e5b7a192-3c82-4f15-8f6a-93a0b5f190c1',
      relativePath: '1-Bruto/B-Roll/GOPRO_HERO11_Shibuya_Crossing.MP4',
      byteLength: 2576980377,
      kind: 'video',
      codec: 'hevc',
      width: 3840,
      height: 2160,
      fps: 59.94,
      durationSeconds: 252.0,
      creationTimeUtc: '2026-04-12T10:15:20Z',
      hasTranscript: false,
      hasVAD: false,
      sceneClassification: null,
      thumbnailUrl: '/assets/11.webp'
    },
    {
      mediaId: 'med-03',
      projectId: 'e5b7a192-3c82-4f15-8f6a-93a0b5f190c1',
      relativePath: '1-Bruto/Drone/MAVIC3_MonteFuji_Amanhecer.MP4',
      byteLength: 1288490188,
      kind: 'video',
      codec: 'h264',
      width: 5120,
      height: 2700,
      fps: 29.97,
      durationSeconds: 110.5,
      creationTimeUtc: '2026-04-13T06:05:10Z',
      hasTranscript: false,
      hasVAD: false,
      sceneClassification: null,
      thumbnailUrl: '/assets/8.webp'
    },
    {
      mediaId: 'med-04',
      projectId: 'e5b7a192-3c82-4f15-8f6a-93a0b5f190c1',
      relativePath: '1-Bruto/A-Roll/SONY_A7S3_TemploAsakusa_Relato.MP4',
      byteLength: 2254857830,
      kind: 'video',
      codec: 'hevc',
      width: 3840,
      height: 2160,
      fps: 23.976,
      durationSeconds: 195.0,
      creationTimeUtc: '2026-04-13T14:20:00Z',
      hasTranscript: false,
      hasVAD: false,
      sceneClassification: null,
      thumbnailUrl: '/assets/11.webp'
    },
    {
      mediaId: 'med-05',
      projectId: 'e5b7a192-3c82-4f15-8f6a-93a0b5f190c1',
      relativePath: '1-Bruto/Audio/RODE_Wireless_AudioExterno_Guia.WAV',
      byteLength: 188743680,
      kind: 'audio',
      codec: 'pcm_s24le',
      width: 0,
      height: 0,
      fps: 0,
      durationSeconds: 330.0,
      creationTimeUtc: '2026-04-13T14:19:55Z',
      hasTranscript: false,
      hasVAD: false,
      sceneClassification: null,
      thumbnailUrl: '/assets/8.webp'
    }
  ];

  constructor() {
    this.isWebView2Available = typeof window !== 'undefined' && !!(window as any).chrome?.webview;

    if (typeof window !== 'undefined') {
      // No WebView2, soltar arquivos sem cancelar o evento faz o host navegar até o arquivo.
      // A zona de soltar da Tela 07 continua recebendo o drop pelo handler React.
      const cancelDefault = (event: Event) => event.preventDefault();
      window.addEventListener('dragover', cancelDefault);
      window.addEventListener('drop', cancelDefault);
    }

    if (this.isWebView2Available) {
      (window as any).chrome.webview.addEventListener('message', (event: any) => {
        const res: IpcResponse = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
        if (res && res.requestId && this.handlers.has(res.requestId)) {
          const { resolve, reject, timer } = this.handlers.get(res.requestId)!;
          clearTimeout(timer);
          this.handlers.delete(res.requestId);
          const isSuccess = res.ok === true || res.success === true;
          if (isSuccess) {
            const data = res.payload !== undefined ? res.payload : (res.data !== undefined ? res.data : res);
            resolve(data);
          } else {
            const errorMsg = res.error?.message || res.errorMessage || res.error?.code || res.errorCode || 'Erro na requisição IPC';
            const errorCode: string | null = res.error?.code ?? res.errorCode ?? null;
            reject(new IpcError(errorMsg, errorCode));
          }
        }
      });
    }
  }

  /** true fora do WebView2: as respostas vêm dos mocks de demonstração. */
  public get isMock(): boolean {
    return !this.isWebView2Available;
  }

  public async send<T = any>(type: string, payload?: any, options?: { timeoutMs?: number }): Promise<T> {
    if (!this.isWebView2Available) {
      return this.handleMockRequest(type, payload);
    }

    const request: IpcRequest = {
      version: 1,
      requestId: this.newRequestId(),
      type,
      payload
    };

    return this.postToHost<T>(request, options?.timeoutMs ?? DEFAULT_IPC_TIMEOUT_MS);
  }

  /**
   * Envia mensagem com arquivos anexos (WebView2 postMessageWithAdditionalObjects).
   * Necessário para resolver caminhos de arquivos arrastados (o File do navegador não expõe o caminho).
   */
  public async sendWithFiles<T = any>(type: string, files: File[]): Promise<T> {
    if (!this.isWebView2Available) {
      return this.handleMockRequest(type, { fileNames: files.map((file) => file.name) });
    }

    const request: IpcRequest = {
      version: 1,
      requestId: this.newRequestId(),
      type,
      payload: {}
    };

    return this.postToHost<T>(request, DEFAULT_IPC_TIMEOUT_MS, files);
  }

  private newRequestId(): string {
    return crypto.randomUUID ? crypto.randomUUID() : `req_${Date.now()}_${Math.random()}`;
  }

  private postToHost<T>(request: IpcRequest, timeoutMs: number, files?: File[]): Promise<T> {
    const { requestId, type } = request;

    return new Promise<T>((resolve, reject) => {
      const timer = setTimeout(() => {
        if (this.handlers.has(requestId)) {
          this.handlers.delete(requestId);
          reject(new IpcError(`Timeout ao aguardar resposta IPC para ${type}`, IPC_TIMEOUT_CODE));
        }
      }, timeoutMs);

      this.handlers.set(requestId, {
        resolve: (value: any) => {
          clearTimeout(timer);
          resolve(value);
        },
        reject: (error: any) => {
          clearTimeout(timer);
          reject(error);
        },
        timer
      });

      try {
        const webview = (window as any).chrome.webview;
        const json = JSON.stringify(request);
        if (files) {
          webview.postMessageWithAdditionalObjects(json, files);
        } else {
          webview.postMessage(json);
        }
      } catch (error) {
        clearTimeout(timer);
        this.handlers.delete(requestId);
        reject(error);
      }
    });
  }

  private async handleMockRequest(type: string, payload?: any): Promise<any> {
    // Simulação determinística de latência de IPC local
    await new Promise(r => setTimeout(r, 60));

    switch (type) {
      case 'system.ping':
        return { timestampUtc: new Date().toISOString(), status: 'ok' };

      case 'project.open':
        if (payload?.rootPath) {
          this.mockProjectSummary.rootPath = payload.rootPath;
          const folderName = payload.rootPath.split(/[/\\]/).filter(Boolean).pop();
          if (folderName) this.mockProjectSummary.name = folderName;
        }
        return { summary: this.mockProjectSummary, status: 'ready' };

      case 'project.create':
        if (payload?.rootPath) {
          this.mockProjectSummary.rootPath = payload.rootPath;
          if (payload?.name) {
            this.mockProjectSummary.name = payload.name;
          }
        }
        // Projeto novo: sem mídias, sugestões ou cortes (contadores zerados).
        this.mockFreshProject = true;
        this.mockImportedAssets = [];
        this.mockPlanRevisions = [];
        Object.assign(this.mockProjectSummary, {
          mediaCount: 0,
          assetsCount: 0,
          suggestionsCount: 0,
          approvedCutsCount: 0,
          pendingReviewsCount: 0,
          totalOriginalDurationSeconds: 0,
          estimatedCutDurationSeconds: null
        });
        return {
          project: this.mockProjectSummary,
          summary: this.mockProjectSummary,
          createdFolders: ['1-Bruto', '2-Assets', '3-Trabalho', '4-Final'],
          status: 'created'
        };

      case 'suggestions.list':
        return {
          suggestions: MOCK_SHOW_SAMPLE_CANDIDATES
            ? this.mockSuggestions.map((s) => this.mockEffective(s.suggestionId))
            : []
        };

      case 'analysis.environment':
        return this.mockEnvironment();

      case 'analysis.getMedia':
        return this.mockMediaAnalysis(payload?.mediaId);

      
      case 'decision.submit':
        return this.mockSubmitDecision(payload as SubmitDecisionRequest);

      case 'decision.undo':
        return this.mockUndoDecision(payload?.suggestionId);

      case 'decision.redo':
        return this.mockRedoDecision(payload?.suggestionId);

      case 'suggestions.batchDecide':
        return this.mockBatchDecide(payload as BatchDecideRequest);

      case 'decision.list':
        return { decisions: this.mockDecisionLog.map((d) => ({ ...d })) };

      case 'dialog.selectFolder':
        return { selectedPath: 'D:\\Videos\\demo-project', canceled: false };

      case 'editor.capabilities':
        return this.mockCapabilities;

      case 'motion.capabilities':
        return mockMotionCapabilities();

      case 'editor.prepareApply':
        return this.mockPrepareApply(payload as PrepareEditorApplyRequest);

      case 'editor.apply':
        return this.mockApply(payload as ApplyEditorPlanRequest);

      case 'editor.applications': {
        const request = payload as { projectId?: string } | undefined; // ListEditorApplicationsRequest
        this.assertMockProject(request?.projectId);
        return { applications: JSON.parse(JSON.stringify(this.mockApplications)) as EditorApplyResult[] };
      }

      case 'plan.get': {
        this.assertMockProject(payload?.projectId);
        const latest = this.mockPlanRevisions[this.mockPlanRevisions.length - 1];
        if (!latest) return null;
        const requested = payload?.revision;
        const plan = requested === null || requested === undefined ? latest : this.mockPlanFind(requested);
        return clonePlanData(plan);
      }

      case 'plan.generate': {
        this.assertMockProject(payload?.projectId);
        const latest = this.mockPlanRevisions[this.mockPlanRevisions.length - 1];
        const revision = (latest?.revision ?? 0) + 1;
        const plan = this.mockBuildPlan(revision, latest?.revision ?? null);
        this.mockPlanRevisions.push(plan);
        return clonePlanData(plan);
      }

      case 'plan.list': {
        this.assertMockProject(payload?.projectId);
        const revisions: PlanRevisionInfoDto[] = this.mockPlanRevisions.map((p) => ({
          planId: p.planId,
          revision: p.revision,
          baseRevision: p.baseRevision,
          createdAtUtc: p.createdAtUtc,
          author: p.author,
          planFingerprint: p.planFingerprint,
          isValid: p.validation.isValid,
          clipCount: p.clips.length,
          timelineDurationFrames: p.summary.timelineDurationFrames
        }));
        return { revisions };
      }

      case 'plan.validate': {
        this.assertMockProject(payload?.projectId);
        return this.mockValidatePlan(this.mockPlanFind(payload?.revision));
      }

      case 'plan.revert': {
        this.assertMockProject(payload?.projectId);
        const target = this.mockPlanFind(payload?.toRevision);
        const latestRevision = this.mockPlanRevisions[this.mockPlanRevisions.length - 1].revision;
        const revision = latestRevision + 1;
        const copy = clonePlanData(target);
        copy.revision = revision;
        copy.baseRevision = target.revision;
        copy.createdAtUtc = new Date().toISOString();
        copy.planFingerprint = `demo-plan-r${revision}`;
        copy.validation = this.mockValidatePlan(copy);
        this.mockPlanRevisions.push(copy);
        return clonePlanData(copy);
      }
      case 'plan.saveManualRevision': {
        this.assertMockProject(payload?.projectId);
        const plan = payload?.plan as EditPlanDto;
        const latestRev = this.mockPlanRevisions[this.mockPlanRevisions.length - 1]?.revision ?? 0;
        if (payload?.baseRevision !== latestRev) {
          throw new IpcError(
            `DEMO: baseRevision R${payload?.baseRevision ?? '?'} difere da revisão vigente R${latestRev}.`,
            'PLAN_STALE_BASE'
          );
        }
        const rev = latestRev + 1;
        const copy = clonePlanData(plan);
        copy.revision = rev;
        copy.baseRevision = plan.revision;
        copy.author = payload?.author ?? 'user';
        copy.createdAtUtc = new Date().toISOString();
        copy.planFingerprint = `manual-plan-r${rev}`;
        copy.validation = this.mockValidatePlan(copy);
        this.mockPlanRevisions.push(copy);
        return clonePlanData(copy);
      }
      case 'plan.reconcile': {
        this.assertMockProject(payload?.projectId);
        const latest = this.mockPlanRevisions[this.mockPlanRevisions.length - 1];
        return {
          reconciledPlan: clonePlanData(latest),
          hasConflicts: false,
          conflicts: []
        };
      }

      case 'profile.get':
        return { ...this.mockProfile };

      case 'automation.profile.get': {
        this.assertMockProject(payload?.projectId);
        return {
          projectId: payload.projectId,
          profile: this.mockAutomationProfile ?? 'balanced',
          isDefault: this.mockAutomationProfile === null
        } satisfies AutomationProfileDto;
      }

      case 'automation.profile.set': {
        this.assertMockProject(payload?.projectId);
        if (!['conservative', 'balanced', 'aggressive'].includes(payload?.profile)) {
          throw new IpcError('Perfil de automação inválido (use conservative, balanced ou aggressive).', 'INVALID_ARGUMENT');
        }
        this.mockAutomationProfile = payload.profile as AutomationProfile;
        return { projectId: payload.projectId, profile: this.mockAutomationProfile, isDefault: false } satisfies AutomationProfileDto;
      }

      case 'automation.options.get': {
        this.assertMockProject(payload?.projectId);
        return {
          projectId: payload.projectId,
          autoProcess: this.mockAutoProcess ?? true,
          isDefault: this.mockAutoProcess === null
        } satisfies AutomationOptionsDto;
      }

      case 'automation.options.set': {
        this.assertMockProject(payload?.projectId);
        if (typeof payload?.autoProcess !== 'boolean') {
          throw new IpcError('autoProcess inválido (booleano).', 'INVALID_ARGUMENT');
        }
        this.mockAutoProcess = payload.autoProcess;
        return { projectId: payload.projectId, autoProcess: payload.autoProcess, isDefault: false } satisfies AutomationOptionsDto;
      }

      case 'system.openFolder':
        return { success: true, openedPath: payload?.path };

      case 'pipeline.start':
        return this.mockPipelineStart(payload as StartPipelineRequest);

      case 'pipeline.status':
        return this.mockPipelineStatus(payload?.projectId);

      case 'pipeline.diagnostics':
        // DEMO: não há processo do worker no navegador.
        return { jobId: this.mockPipeline?.jobId ?? null, executions: [] };

      case 'pipeline.cancel': {
        const job = this.mockPipeline;
        const canceled = !!job && job.jobId === payload?.jobId && this.mockPipelineState(job) === 'running';
        if (canceled && job) job.canceledAt = Date.now();
        return { canceled };
      }

      case 'media.scan':
        return {
          totalFilesFound: this.mockMediaAssets.length,
          newAssetsAdded: 2,
          updatedAssets: 0,
          unchangedAssets: this.mockMediaAssets.length - 2,
          totalBytes: this.mockMediaAssets.reduce((acc, m) => acc + m.byteLength, 0),
          durationSecondsTotal: this.mockMediaAssets.reduce((acc, m) => acc + m.durationSeconds, 0)
        };

      case 'media.list':
        return this.mockFreshProject ? [...this.mockImportedAssets] : [...this.mockMediaAssets];

      case 'media.details': {
        const asset = this.mockMediaAssets.find(m => m.mediaId === payload?.mediaId) || this.mockMediaAssets[0];
        return {
          mediaId: asset.mediaId,
          projectId: asset.projectId,
          relativePath: asset.relativePath,
          fullPath: `D:\\Videos\\demo-project\\${asset.relativePath}`,
          byteLength: asset.byteLength,
          kind: asset.kind,
          fingerprint: 'sha256_mock_fingerprint_8fbc',
          lastWriteUtc: new Date().toISOString(),
          codec: asset.codec,
          width: asset.width,
          height: asset.height,
          frameRate: asset.fps,
          timeBase: `${asset.fps}:1`,
          durationSeconds: asset.durationSeconds,
          durationTicks: Math.round(asset.durationSeconds * 10000000),
          audioChannels: 2,
          audioSampleRate: 48000,
          creationTimeUtc: asset.creationTimeUtc,
          sourceDateEvidence: 'quicktime_meta_creation_time',
          isVariableFrameRate: false
        };
      }

      case 'media.chronology':
        return {
          sessions: [
            {
              sessionIndex: 1,
              name: 'Sessão 01 - Chegada & Aeroporto',
              startTimeUtc: '2026-04-12T08:30:00Z',
              endTimeUtc: '2026-04-12T08:32:45Z',
              totalClips: 1,
              totalDurationSeconds: 165.2,
              cameraModels: ['DJI Osmo Pocket 3'],
              clips: [
                {
                  mediaId: 'med-01',
                  relativePath: '1-Bruto/A-Roll/DJI_0042_ChegadaAeroporto_4K.MP4',
                  kind: 'video',
                  cameraModel: 'DJI Osmo Pocket 3',
                  recordedAtUtc: '2026-04-12T08:30:00Z',
                  durationSeconds: 165.2,
                  estimatedEndAtUtc: '2026-04-12T08:32:45Z',
                  timestampSource: 'quicktime_meta_creation_time',
                  confidence: 'high',
                  sessionIndex: 1,
                  anomaly: null
                }
              ]
            },
            {
              sessionIndex: 2,
              name: 'Sessão 02 - Shibuya & Exploração',
              startTimeUtc: '2026-04-12T10:15:20Z',
              endTimeUtc: '2026-04-12T10:19:32Z',
              totalClips: 1,
              totalDurationSeconds: 252.0,
              cameraModels: ['GoPro HERO11 Black'],
              clips: [
                {
                  mediaId: 'med-02',
                  relativePath: '1-Bruto/B-Roll/GOPRO_HERO11_Shibuya_Crossing.MP4',
                  kind: 'video',
                  cameraModel: 'GoPro HERO11 Black',
                  recordedAtUtc: '2026-04-12T10:15:20Z',
                  durationSeconds: 252.0,
                  estimatedEndAtUtc: '2026-04-12T10:19:32Z',
                  timestampSource: 'quicktime_meta_creation_time',
                  confidence: 'high',
                  sessionIndex: 2,
                  anomaly: null
                }
              ]
            },
            {
              sessionIndex: 3,
              name: 'Sessão 03 - Monte Fuji Amanhecer',
              startTimeUtc: '2026-04-13T06:05:10Z',
              endTimeUtc: '2026-04-13T06:07:00Z',
              totalClips: 1,
              totalDurationSeconds: 110.5,
              cameraModels: ['DJI Mavic 3 Pro'],
              clips: [
                {
                  mediaId: 'med-03',
                  relativePath: '1-Bruto/Drone/MAVIC3_MonteFuji_Amanhecer.MP4',
                  kind: 'video',
                  cameraModel: 'DJI Mavic 3 Pro',
                  recordedAtUtc: '2026-04-13T06:05:10Z',
                  durationSeconds: 110.5,
                  estimatedEndAtUtc: '2026-04-13T06:07:00Z',
                  timestampSource: 'quicktime_meta_creation_time',
                  confidence: 'high',
                  sessionIndex: 3,
                  anomaly: null
                }
              ]
            },
            {
              sessionIndex: 4,
              name: 'Sessão 04 - Templo Asakusa & Áudio Gravado',
              startTimeUtc: '2026-04-13T14:19:55Z',
              endTimeUtc: '2026-04-13T14:25:25Z',
              totalClips: 2,
              totalDurationSeconds: 525.0,
              cameraModels: ['Sony Alpha 7S III', 'Rode Wireless PRO'],
              clips: [
                {
                  mediaId: 'med-05',
                  relativePath: '1-Bruto/Audio/RODE_Wireless_AudioExterno_Guia.WAV',
                  kind: 'audio',
                  cameraModel: 'Rode Wireless PRO',
                  recordedAtUtc: '2026-04-13T14:19:55Z',
                  durationSeconds: 330.0,
                  estimatedEndAtUtc: '2026-04-13T14:25:25Z',
                  timestampSource: 'riff_info_creation_date',
                  confidence: 'high',
                  sessionIndex: 4,
                  anomaly: null
                },
                {
                  mediaId: 'med-04',
                  relativePath: '1-Bruto/A-Roll/SONY_A7S3_TemploAsakusa_Relato.MP4',
                  kind: 'video',
                  cameraModel: 'Sony Alpha 7S III',
                  recordedAtUtc: '2026-04-13T14:20:00Z',
                  durationSeconds: 195.0,
                  estimatedEndAtUtc: '2026-04-13T14:23:15Z',
                  timestampSource: 'quicktime_meta_creation_time',
                  confidence: 'high',
                  sessionIndex: 4,
                  anomaly: null
                }
              ]
            }
          ],
          totalClips: 5,
          totalDurationSeconds: 1052.7,
          earliestUtc: '2026-04-12T08:30:00Z',
          latestUtc: '2026-04-13T14:25:25Z',
          anomaliesDetected: 0
        };

      case 'fs.listDirectory': {
        const path: string | null = payload?.path ?? null;
        if (path === null) {
          const drives: DirectoryEntryDto[] = ['K:\\', 'D:\\'].map((drive) => ({
            name: drive,
            fullPath: drive,
            isDirectory: true,
            byteLength: null,
            kind: null
          }));
          const listing: ListDirectoryResponse = { path: null, parentPath: null, entries: drives };
          return listing;
        }
        const listing: ListDirectoryResponse = {
          path,
          parentPath: mockParent(path),
          entries: mockDirectoryEntries(path)
        };
        return listing;
      }

      case 'dialog.selectFiles': {
        const isAssets = payload?.filter === 'assets';
        const folder = `${MOCK_DEMO_ROOT}\\${isAssets ? '2-Assets' : '1-Bruto'}`;
        const names = isAssets
          ? ['DEMO_TRILHA_01.MP3', 'DEMO_FOTO_0001.JPG']
          : ['DEMO_CLIP_0004.MP4', 'DEMO_CLIP_0005.MOV'];
        const selection: SelectFilesResponse = {
          paths: names.map((name) => mockJoin(folder, name)),
          canceled: false
        };
        return selection;
      }

      case 'media.inspectCandidates': {
        const paths: string[] = payload?.paths ?? [];
        const items: ImportCandidateDto[] = [];
        for (const path of paths) {
          if (mockKindOf(mockBasename(path))) {
            items.push(mockCandidate(path));
          } else {
            // Caminho sem extensão de mídia é tratado como pasta: o host expandiria os arquivos.
            for (const name of ['DEMO_PASTA_0001.MP4', 'DEMO_PASTA_0002.MP4', 'DEMO_PASTA_0003.WAV']) {
              items.push(mockCandidate(mockJoin(path, name)));
            }
          }
        }
        const response: InspectCandidatesResponse = {
          items,
          ffprobeAvailable: true,
          truncated: false,
          errors: []
        };
        return response;
      }

      case 'media.getThumbnail': {
        const thumbnail: ThumbnailResponse = {
          status: 'not_supported',
          dataUrl: null,
          reason: 'Modo demonstração'
        };
        return thumbnail;
      }

      case 'dnd.resolvePaths': {
        const names: string[] = (payload?.fileNames ?? []).filter((name: string) => mockKindOf(name) !== null);
        return { paths: names.map((name) => mockJoin(`${MOCK_DEMO_ROOT}\\DEMO-Arrastados`, name)) };
      }

      case 'media.import.start': {
        const jobId = `mock_import_${Date.now()}`;
        const request = payload as StartImportRequest;
        this.mockImportJobs.set(jobId, { jobId, request, startedAt: Date.now(), canceledAt: null, applied: false });
        return { jobId };
      }

      case 'media.import.status':
        return this.mockImportStatus(payload?.jobId);

      case 'media.import.cancel': {
        const job = this.mockImportJobs.get(payload?.jobId);
        if (job && job.canceledAt === null) job.canceledAt = Date.now();
        return { success: true };
      }

      case 'settings.get':
        return { ...this.mockSettings };

      case 'settings.setToolPath': {
        const { toolKey, path } = (payload ?? {}) as { toolKey: string; path: string };
        if (toolKey) {
          if (!this.mockSettings.configuredToolPaths) this.mockSettings.configuredToolPaths = {};
          const dict = this.mockSettings.configuredToolPaths || {};
          dict[toolKey] = path;
          this.mockSettings.configuredToolPaths = dict;
        }
        return { configured: true };
      }

      case 'settings.setMediaSourceFolders': {
        const { folders } = (payload ?? {}) as { folders: string[] };
        this.mockSettings.mediaSourceFolders = folders ?? [];
        return { success: true };
      }

      case 'settings.addMediaSourceFolder': {
        const { folder } = (payload ?? {}) as { folder: string };
        if (folder && !this.mockSettings.mediaSourceFolders.includes(folder)) {
          this.mockSettings.mediaSourceFolders.push(folder);
        }
        return { success: true };
      }

      case 'settings.removeMediaSourceFolder': {
        const { folder } = (payload ?? {}) as { folder: string };
        this.mockSettings.mediaSourceFolders = this.mockSettings.mediaSourceFolders.filter((f) => f !== folder);
        return { success: true };
      }

      case 'environment.diagnose': {
        const diag: EnvironmentAssistantDiagnosticDto = {
          evaluatedAtUtc: new Date().toISOString(),
          tools: [
            {
              toolKey: 'python',
              displayName: 'Python 3.11+',
              state: 'READY',
              isReady: true,
              version: '3.11.9',
              executablePath: this.mockSettings.configuredToolPaths?.['python'] || 'C:\\Python311\\python.exe',
              details: 'Python instalado e verificado com sucesso no sistema.',
              supportedActions: ['refresh', 'locate', 'configure_path', 'test'],
              winGetPackageId: 'Python.Python.3.11'
            },
            {
              toolKey: 'whisper',
              displayName: 'OpenAI Whisper & PyTorch',
              state: 'READY_GPU',
              isReady: true,
              version: 'large-v3',
              executablePath: 'C:\\Users\\User\\.cache\\whisper\\large-v3.pt',
              details: 'Whisper pronto com aceleração CUDA (NVIDIA GeForce RTX 4070).',
              supportedActions: ['refresh', 'test'],
              metadata: { device: 'cuda', model: 'large-v3', modelCached: 'True' }
            },
            {
              toolKey: 'ffmpeg',
              displayName: 'FFmpeg / FFprobe',
              state: 'READY',
              isReady: true,
              version: '7.0.1',
              executablePath: this.mockSettings.configuredToolPaths?.['ffmpeg'] || 'C:\\ffmpeg\\bin\\ffmpeg.exe',
              details: 'FFmpeg e FFprobe funcionais com suporte a decodificação HEVC e extração de áudio.',
              supportedActions: ['refresh', 'locate', 'configure_path', 'test'],
              winGetPackageId: 'Gyan.FFmpeg'
            },
            {
              toolKey: 'node',
              displayName: 'Node.js LTS',
              state: 'READY',
              isReady: true,
              version: '22.4.0',
              executablePath: this.mockSettings.configuredToolPaths?.['node'] || 'C:\\Program Files\\nodejs\\node.exe',
              details: 'Node.js LTS disponível para execução de scripts e ferramentas de motion graphics.',
              supportedActions: ['refresh', 'locate', 'configure_path', 'test'],
              winGetPackageId: 'OpenJS.NodeJS.LTS'
            },
            {
              toolKey: 'hyperframes',
              displayName: 'HyperFrames Motion Renderer',
              state: 'READY',
              isReady: true,
              version: '1.2.0',
              executablePath: this.mockSettings.configuredToolPaths?.['hyperframes'] || 'C:\\Users\\User\\AppData\\Local\\TravelCutStudio\\tools\\hyperframes\\node_modules\\hyperframes\\dist\\cli.js',
              details: 'Renderizador de motion graphics pronto e verificado no diretório persistente.',
              supportedActions: ['refresh', 'locate', 'configure_path', 'test']
            }
          ],
          allReady: true,
          timestampUtc: new Date().toISOString(),
          systemInfo: {
            osDescription: 'Microsoft Windows 11 Pro 64-bit',
            architecture: 'X64',
            isWindows: true,
            totalMemoryMb: 16384,
            freeDiskSpaceMb: 124500,
            targetCacheDirectory: 'C:\\Users\\User\\.cache\\whisper',
            hasWinget: true,
            winGetAvailable: true
          }
        };
        return diag;
      }

      case 'environment.diagnoseTool': {
        const { toolKey } = (payload ?? {}) as { toolKey: string };
        const diag = (await this.handleMockRequest('environment.diagnose')) as EnvironmentAssistantDiagnosticDto;
        return diag.tools.find((t) => t.toolKey === toolKey) ?? diag.tools[0];
      }

      case 'environment.getInstallPlan': {
        const { toolKey } = (payload ?? {}) as { toolKey: string };
        const plan: EnvironmentInstallPlanDto = {
          toolKey: toolKey || 'python',
          title: `Instalação assistida de ${toolKey}`,
          description: `Configuração automática de ${toolKey} para o TravelCut Studio.`,
          canAutoInstall: true,
          estimatedTime: '2 a 3 minutos',
          estimatedDownloadSize: '~150 MB',
          requiresElevation: false,
          requiresUserConfirmation: true,
          steps: [
            { stepNumber: 1, title: 'Download de pacote', command: `winget install --id ${toolKey}`, description: 'Download dos binários oficiais.' },
            { stepNumber: 2, title: 'Configuração de ambiente', command: 'travelcut configure', description: 'Registro dos caminhos no TravelCut.' }
          ]
        };
        return plan;
      }

      case 'environment.startInstall': {
        const jobId = `mock_inst_${Date.now()}`;
        this.mockInstallJobs.set(jobId, { toolKey: payload?.toolKey ?? 'tool', startedAt: Date.now(), canceled: false });
        return { jobId };
      }

      case 'environment.getInstallProgress': {
        const job = this.mockInstallJobs.get(payload?.jobId);
        const elapsed = job ? (Date.now() - job.startedAt) / 1000 : 10;
        const percent = job?.canceled ? 0 : Math.min(100, Math.round(elapsed * 25));
        const progress: ToolInstallProgressDto = {
          jobId: payload?.jobId ?? 'unknown',
          toolKey: job?.toolKey ?? 'python',
          stage: job?.canceled ? 'Cancelado' : percent >= 100 ? 'Concluído' : 'Instalando componentes...',
          progressPercent: percent,
          currentMessage: job?.canceled ? 'Operação cancelada pelo usuário.' : percent >= 100 ? 'Instalação concluída com sucesso!' : 'Executando comando de instalação...',
          outputLogs: [
            'Iniciando rotina de instalação assistida...',
            `Verificando pré-requisitos para ${job?.toolKey}...`,
            percent >= 50 ? 'Baixando pacotes necessários...' : '',
            percent >= 100 ? 'Ambiente configurado e testado com sucesso.' : ''
          ].filter(Boolean),
          canCancel: percent < 100 && !job?.canceled,
          isCompleted: percent >= 100 || !!job?.canceled,
          isSuccess: percent >= 100 && !job?.canceled,
          isFailed: false
        };
        return progress;
      }

      case 'environment.cancelInstall': {
        const job = this.mockInstallJobs.get(payload?.jobId);
        if (job) job.canceled = true;
        return { canceled: true };
      }

      case 'environment.startDownloadModel': {
        const jobId = `mock_dl_${Date.now()}`;
        this.mockDownloadJobs.set(jobId, { modelName: payload?.modelName ?? 'large-v3', startedAt: Date.now(), canceled: false });
        return { jobId };
      }

      case 'environment.getModelDownloadProgress': {
        const job = this.mockDownloadJobs.get(payload?.jobId);
        const elapsed = job ? (Date.now() - job.startedAt) / 1000 : 10;
        const percent = job?.canceled ? 0 : Math.min(100, Math.round(elapsed * 20));
        const progress: ToolInstallProgressDto = {
          jobId: payload?.jobId ?? 'unknown',
          toolKey: 'whisper',
          stage: job?.canceled ? 'Cancelado' : percent >= 100 ? 'Concluído' : 'Baixando modelo large-v3...',
          progressPercent: percent,
          bytesDownloaded: Math.round((percent / 100) * 3094792307),
          totalBytes: 3094792307,
          isCanceled: !!job?.canceled,
          currentMessage: job?.canceled ? 'Download cancelado.' : percent >= 100 ? 'DEMO: download e verificação SHA-256 simulados.' : `DEMO: transferindo ${Math.round((percent / 100) * 2880)} MB de 2880 MB...`,
          outputLogs: [
            'Conectando ao Azure Blob Storage...',
            'Verificando espaço em disco: 120 GB livres (necessário: 3.5 GB)...',
            percent >= 100 ? 'Verificando hash SHA-256 oficial: OK.' : ''
          ].filter(Boolean),
          canCancel: percent < 100 && !job?.canceled,
          isCompleted: percent >= 100 || !!job?.canceled,
          isSuccess: percent >= 100 && !job?.canceled,
          isFailed: false
        };
        return progress;
      }

      case 'environment.cancelModelDownload': {
        const job = this.mockDownloadJobs.get(payload?.jobId);
        if (job) job.canceled = true;
        return { canceled: true };
      }

      case 'environment.testTool': {
        const key = payload?.toolKey ?? 'tool';
        const res: TestToolResponse = {
          toolKey: key,
          success: true,
          message: `Teste funcional de ${key} executado com sucesso no ambiente local.`,
          outputLogs: [
            `Invocando probe funcional para ${key}...`,
            'Exit code: 0',
            'stdout: OK: versão validada e processo finalizado sem erros.'
          ]
        };
        return res;
      }

      case 'environment.configureToolPath': {
        const { toolKey, path } = (payload ?? {}) as { toolKey: string; path: string };
        if (toolKey) {
          const dict = this.mockSettings.configuredToolPaths || {};
          dict[toolKey] = path;
          this.mockSettings.configuredToolPaths = dict;
        }
        return { configured: true };
      }

      case 'environment.autoLocateTool': {
        const key = payload?.toolKey ?? 'tool';
        const res: AutoLocateResponse = {
          toolKey: key,
          candidates: [
            this.mockSettings.configuredToolPaths?.[key] || `C:\\Tools\\${key}\\${key}.exe`,
            `C:\\Program Files\\${key}\\${key}.exe`
          ]
        };
        return res;
      }

      default:
        return { success: true };
    }
  }

  // ---- Plano editorial (F06) — DEMONSTRAÇÃO: planos montados a partir das mídias mock, sem cortes ----

  private assertMockProject(projectId: string | undefined): void {
    if (projectId !== this.mockProjectSummary.projectId) {
      throw new IpcError('DEMO: projeto não encontrado no ambiente simulado.', 'PROJECT_NOT_FOUND');
    }
  }

  private mockPlanFind(revision: number | undefined): EditPlanDto {
    const found = this.mockPlanRevisions.find((p) => p.revision === revision);
    if (!found) {
      throw new IpcError(`DEMO: revisão R${revision ?? '?'} não existe no ambiente simulado.`, 'PLAN_NOT_FOUND');
    }
    return found;
  }

  /**
   * Monta um plano pequeno com as mídias de vídeo mock (durationSeconds conhecido).
   * Cada vídeo vira uma seção (hipótese) em V1 com par A1 vinculado. Não há cortes aprovados no mock,
   * então removedSourceUs e os contadores de corte são 0 de fato.
   */
  private mockBuildPlan(revision: number, baseRevision: number | null): EditPlanDto {
    const tb = MOCK_PLAN_TIMEBASE;
    const source = this.mockFreshProject ? this.mockImportedAssets : this.mockMediaAssets;
    const videos = source.filter((a) => a.kind === 'video' && a.durationSeconds > 0).slice(0, 3);
    if (videos.length === 0) {
      throw new IpcError('DEMO: nenhuma mídia de vídeo analisada para planejar.', 'NO_MEDIA_TO_PLAN');
    }
    const roles = ['abertura', 'desenvolvimento', 'encerramento'];
    const sections: PlanSectionDto[] = [];
    const clips: PlanClipDto[] = [];
    const transitions: PlanTransitionDto[] = [];
    let cursor = 0;

    videos.forEach((media, index) => {
      const sectionId = `demo-sec-${index + 1}`;
      const videoId = `demo-v-${index + 1}`;
      const audioId = `demo-a-${index + 1}`;
      const durationFrames = Math.round((media.durationSeconds * tb.frameDurationDen) / tb.frameDurationNum);
      const sourceOutUs = Math.round(media.durationSeconds * 1_000_000);
      const start = cursor;
      const flaggedForReview = index === 1;

      sections.push({
        sectionId,
        order: index + 1,
        role: roles[index] ?? 'desenvolvimento',
        title: `Sessão ${index + 1}`,
        rationale: 'DEMO: ordem cronológica das mídias mock; a estrutura narrativa é uma hipótese até revisão.',
        isHypothesis: true,
        startFrame: start,
        endFrame: start + durationFrames
      });

      const base = {
        mediaId: media.mediaId,
        relativePath: media.relativePath,
        sectionId,
        sourceInUs: 0,
        sourceOutUs,
        timelineStartFrame: start,
        durationFrames,
        speedNum: 1,
        speedDen: 1,
        zoom: null,
        suggestionIds: [],
        confidenceBand: 'medium' as const
      };

      clips.push({
        ...base,
        clipId: videoId,
        trackId: 'V1',
        linkedClipId: audioId,
        rationale: flaggedForReview
          ? 'DEMO: trecho marcado para revisão humana (exemplo do mock).'
          : 'DEMO: trecho integral da mídia (nenhum corte aprovado no mock).',
        evidence: ['DEMO: duração conhecida pelo mock.'],
        needsHumanReview: flaggedForReview
      });
      clips.push({
        ...base,
        clipId: audioId,
        trackId: 'A1',
        linkedClipId: videoId,
        rationale: 'DEMO: áudio vinculado ao trecho de vídeo.',
        evidence: ['DEMO: mesmo intervalo de fonte do clipe de vídeo.'],
        needsHumanReview: false
      });

      if (index > 0) {
        transitions.push({
          fromClipId: `demo-v-${index}`,
          toClipId: videoId,
          kind: 'cut',
          durationFrames: 0,
          needsHumanReview: false
        });
      }
      cursor += durationFrames;
    });

    const pending: PlanPendingItemDto[] = [];
    for (const asset of source.filter((a) => a.kind !== 'video')) {
      pending.push({
        suggestionId: null,
        mediaId: asset.mediaId,
        kind: 'media_excluded',
        message: `DEMO: ${mockBasename(asset.relativePath)} é áudio e não entra nos trechos de vídeo deste plano.`
      });
    }
    const proposed = MOCK_SHOW_SAMPLE_CANDIDATES
      ? this.mockSuggestions.filter((s) => this.mockStateOf(s.suggestionId).status === 'Proposed')
      : [];
    for (const suggestion of proposed) {
      pending.push({
        suggestionId: suggestion.suggestionId,
        mediaId: suggestion.mediaId,
        kind: 'suggestion_pending',
        message: `DEMO: sugestão ${suggestion.suggestionId} aguarda decisão na Revisão.`
      });
    }

    const tracks: PlanTrackDto[] = [
      { trackId: 'V1', kind: 'video', label: 'V1 · Vídeo principal' },
      { trackId: 'A1', kind: 'dialogue', label: 'A1 · Diálogo' },
      { trackId: 'A2', kind: 'music', label: 'A2 · Música (somente sugestão)' }
    ];
    const appliedCuts: PlanAppliedCutDto[] = [];
    const now = new Date().toISOString();
    const plan: EditPlanDto = {
      schemaVersion: 'demo',
      planId: 'demo-plan-editorial',
      projectId: this.mockProjectSummary.projectId,
      revision,
      baseRevision,
      createdAtUtc: now,
      author: 'DEMO (mock)',
      profile: { ...this.mockProfile },
      timeBase: { ...tb },
      mediaSetFingerprint: `demo-media-${videos.length}`,
      planFingerprint: `demo-plan-r${revision}`,
      timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone,
      tracks,
      sections,
      clips,
      transitions,
      appliedCuts,
      pending,
      summary: {
        sourceDurationUs: videos.reduce((sum, v) => sum + Math.round(v.durationSeconds * 1_000_000), 0),
        timelineDurationFrames: cursor,
        removedSourceUs: 0,
        approvedCutsApplied: appliedCuts.length,
        autoCutsApplied: 0,
        pendingSuggestions: proposed.length,
        excludedMedia: source.filter((a) => a.kind !== 'video').length
      },
      validation: { isValid: true, issues: [], validatedAtUtc: now }
    };
    plan.validation = this.mockValidatePlan(plan);
    return plan;
  }

  /** Validação estrutural real sobre o plano mock: duração, intervalo de fonte, seção e sobreposição na trilha. */
  private mockValidatePlan(plan: EditPlanDto): PlanValidationDto {
    const issues: PlanIssueDto[] = [];
    const sectionIds = new Set(plan.sections.map((s) => s.sectionId));
    for (const clip of plan.clips) {
      if (clip.durationFrames <= 0) {
        issues.push({ severity: 'error', code: 'CLIP_EMPTY', message: `DEMO: ${clip.clipId} tem duração zero.`, clipId: clip.clipId });
      }
      if (clip.sourceOutUs <= clip.sourceInUs) {
        issues.push({ severity: 'error', code: 'SOURCE_RANGE_INVALID', message: `DEMO: ${clip.clipId} tem fim de fonte antes do início.`, clipId: clip.clipId });
      }
      if (clip.sectionId !== null && !sectionIds.has(clip.sectionId)) {
        issues.push({ severity: 'error', code: 'SECTION_MISSING', message: `DEMO: ${clip.clipId} aponta para seção inexistente.`, clipId: clip.clipId });
      }
      if (clip.needsHumanReview) {
        issues.push({ severity: 'warning', code: 'NEEDS_HUMAN_REVIEW', message: `DEMO: ${clip.clipId} aguarda revisão humana.`, clipId: clip.clipId });
      }
    }
    const byTrack = new Map<string, PlanClipDto[]>();
    for (const clip of plan.clips) {
      const list = byTrack.get(clip.trackId) ?? [];
      list.push(clip);
      byTrack.set(clip.trackId, list);
    }
    for (const list of byTrack.values()) {
      list.sort((a, b) => a.timelineStartFrame - b.timelineStartFrame);
      for (let i = 1; i < list.length; i++) {
        const prev = list[i - 1];
        if (list[i].timelineStartFrame < prev.timelineStartFrame + prev.durationFrames) {
          issues.push({ severity: 'error', code: 'TRACK_OVERLAP', message: `DEMO: ${list[i].clipId} sobrepõe ${prev.clipId} na mesma trilha.`, clipId: list[i].clipId });
        }
      }
    }
    return {
      isValid: !issues.some((i) => i.severity === 'error'),
      issues,
      validatedAtUtc: new Date().toISOString()
    };
  }

  // ---- Envio ao editor (editor/1) — DEMONSTRAÇÃO: simulador, nada chega ao DaVinci ----

  /**
   * Dry-run do envio a partir do plano mock: clipes V1 em frames (fonte e timeline), bloqueios vindos da
   * validação do plano e manifesto de faz de conta. Mesmas regras do host: sem plano → NO_PLAN.
   */
  private mockBuildApplyPreview(revision: number | null): EditorApplyPreview {
    if (revision !== null && (!Number.isInteger(revision) || revision < 1)) {
      throw new IpcError('DEMO: número de revisão inválido.', 'INVALID_ARGUMENT');
    }
    if (revision === null && this.mockPlanRevisions.length === 0) {
      throw new IpcError('DEMO: não há plano editorial neste projeto. Gere um plano na Tela 03.', 'NO_PLAN');
    }
    const plan = revision === null ? this.mockPlanRevisions[this.mockPlanRevisions.length - 1] : this.mockPlanFind(revision);
    const tb = plan.timeBase;
    const assetOf = (mediaId: string) =>
      [...this.mockImportedAssets, ...this.mockMediaAssets].find((a) => a.mediaId === mediaId);
    const timelineFps = tb.frameDurationDen / tb.frameDurationNum;

    const v1 = plan.clips
      .filter((c) => c.trackId === 'V1')
      .sort((a, b) => a.timelineStartFrame - b.timelineStartFrame);

    const clips: EditorClipPreview[] = v1.map((c) => {
      const fps = assetOf(c.mediaId)?.fps ?? timelineFps;
      return {
        clipId: c.clipId,
        relativePath: c.relativePath,
        sourceStartFrame: Math.round((c.sourceInUs / 1_000_000) * fps),
        sourceEndFrameExclusive: Math.round((c.sourceOutUs / 1_000_000) * fps),
        recordFrame: c.timelineStartFrame,
        durationFrames: c.durationFrames
      };
    });

    const rejected: EditorRejectedOp[] = v1
      .filter((c) => c.speedNum !== c.speedDen)
      .map((c) => ({
        kind: 'speed',
        clipId: c.clipId,
        code: 'CAPABILITY_UNSUPPORTED',
        message: 'DEMO: velocidade diferente de 1x não é aplicada pelo simulador.'
      }));

    const warnings: EditorWarning[] = v1
      .filter((c) => c.needsHumanReview)
      .map((c) => ({
        code: 'NEEDS_HUMAN_REVIEW',
        message: 'DEMO: trecho aguarda revisão humana antes do envio.',
        clipId: c.clipId
      }));
    const distinctFps = new Set(v1.map((c) => assetOf(c.mediaId)?.fps ?? timelineFps));
    if (distinctFps.size > 1) {
      warnings.push({
        code: 'MIXED_FRAME_RATE',
        message: 'DEMO: mídias com taxas de quadros diferentes da timeline; a conformação pelo Resolve não foi verificada — conferir no host.',
        clipId: null
      });
    }

    const blocking: string[] = [];
    for (const issue of plan.validation.issues) {
      if (issue.severity === 'error' && !blocking.includes(issue.code)) blocking.push(issue.code);
    }
    if (clips.length === 0) blocking.push('NO_CLIPS');

    const projectName = this.mockProjectSummary.name.replace(/[^A-Za-z0-9]+/g, '_').replace(/^_+|_+$/g, '') || 'Projeto';
    // aaaammddThhmmss (UTC), no formato do nome do projeto do host.
    const stamp = new Date().toISOString().slice(0, 19).replace(/[-:]/g, '');
    const rev = revisionLabel(plan.revision);

    return {
      status: 'DRY_RUN',
      planId: plan.planId,
      revision: plan.revision,
      planFingerprint: plan.planFingerprint,
      assemblyFingerprint: mockAssemblyFingerprintOf(plan.planFingerprint, clips.length),
      editorProjectName: `TravelCut_${projectName}_${rev}_${stamp}`,
      timelineName: `DEMO ${this.mockProjectSummary.name} ${rev}`,
      timelineFrameRate: timelineFrameRateLabel(tb),
      clips,
      rejected,
      warnings,
      manifestRelativePath: `3-Trabalho/editor/${plan.planId}/${rev}.assembly.json`,
      capabilities: mockEditorCapabilities(),
      canApply: blocking.length === 0,
      blockingReasons: blocking,
      // Mesma regra do host: só o adaptador real do DaVinci exige confirmação de troca de projeto.
      requiresProjectSwitchConfirmation: false
    };
  }

  private mockPrepareApply(request: PrepareEditorApplyRequest | undefined): EditorApplyPreview {
    this.assertMockProject(request?.projectId);
    return this.mockBuildApplyPreview(request?.revision ?? null);
  }

  /** Mesma ordem de validação do host: fingerprint → bloqueios → confirmação → idempotência. */
  private mockApply(request: ApplyEditorPlanRequest | undefined): EditorApplyResult {
    this.assertMockProject(request?.projectId);
    const key = request?.idempotencyKey ?? '';
    if (!MOCK_IDEMPOTENCY_KEY_PATTERN.test(key)) {
      throw new IpcError('DEMO: chave de envio inválida.', 'INVALID_IDEMPOTENCY_KEY');
    }
    const preview = this.mockBuildApplyPreview(request?.revision ?? null);
    if (request?.assemblyFingerprint !== preview.assemblyFingerprint) {
      throw new IpcError('DEMO: a montagem mudou desde a preparação. Prepare o envio de novo.', 'PROJECT_VERSION_CONFLICT');
    }
    if (!preview.canApply) {
      const first = preview.blockingReasons[0];
      throw new IpcError(`DEMO: envio bloqueado (${first}).`, first);
    }
    if (preview.requiresProjectSwitchConfirmation && !request?.confirmProjectSwitch) {
      throw new IpcError('DEMO: confirme a troca do projeto aberto no DaVinci.', 'CONFIRMATION_REQUIRED');
    }

    const sameKey = this.mockApplications.filter(
      (a) => a.projectId === request?.projectId && a.idempotencyKey === key
    );
    if (sameKey.some((a) => a.assemblyFingerprint !== preview.assemblyFingerprint)) {
      throw new IpcError('DEMO: esta chave de envio já foi usada com outra montagem.', 'IDEMPOTENCY_KEY_REUSED');
    }
    const reusable = sameKey.find((a) => a.status !== 'ERROR');
    if (reusable) {
      return JSON.parse(JSON.stringify({ ...reusable, reusedPrevious: true })) as EditorApplyResult;
    }

    this.mockApplicationSeq += 1;
    const applicationId = `demo-app-${String(this.mockApplicationSeq).padStart(3, '0')}`;
    const startedAtUtc = new Date().toISOString();
    const steps: EditorStepResult[] = MOCK_APPLY_STEP_OPERATIONS.map((operation) => ({
      operation,
      status: 'simulated',
      message: 'DEMO: etapa simulada; nada foi criado no DaVinci.',
      durationMs: null
    }));
    const clipMappings: EditorClipMapping[] = preview.clips.map((c) => ({
      clipId: c.clipId,
      editorItemId: null,
      observedRecordFrame: null,
      observedDurationFrames: null,
      matches: null
    }));
    const record: EditorApplyResult = {
      schemaVersion: 'editor-apply/1',
      applicationId,
      status: 'SIMULATED',
      adapterMode: 'mock',
      isSimulated: true,
      projectId: request?.projectId ?? this.mockProjectSummary.projectId,
      planId: preview.planId,
      revision: preview.revision,
      assemblyFingerprint: preview.assemblyFingerprint,
      idempotencyKey: key,
      editorProjectName: preview.editorProjectName,
      timelineName: preview.timelineName,
      editorVersion: null,
      steps,
      clipMappings,
      rejected: preview.rejected,
      warnings: preview.warnings,
      snapshotRelativePath: null,
      recordRelativePath: `3-Trabalho/editor/${preview.planId}/applications/${applicationId}.json`,
      startedAtUtc,
      finishedAtUtc: new Date().toISOString(),
      errorCode: null,
      errorMessage: null,
      reusedPrevious: false
    };
    this.mockApplications.unshift(record);
    return JSON.parse(JSON.stringify(record)) as EditorApplyResult;
  }

  // ---- Análise audiovisual e pipeline — DEMONSTRAÇÃO (sem FFmpeg/Whisper reais) ----

  private mockEnvironment(): AnalysisEnvironmentDto {
    return {
      ffmpegAvailable: false,
      ffmpegVersion: null,
      pythonAvailable: false,
      whisperAvailable: false,
      whisperEngine: null,
      whisperModel: 'large-v3',
      cudaAvailable: null,
      reasons: [
        'DEMO: FFmpeg não disponível no ambiente simulado.',
        'DEMO: Whisper não encontrado no ambiente simulado.'
      ]
    };
  }

  private currentMockMedia(): MediaAssetDto[] {
    return this.mockFreshProject ? this.mockImportedAssets : this.mockMediaAssets;
  }

  private mockPipelineState(job: MockPipelineJob): 'running' | 'canceled' | 'completed_with_issues' {
    if (job.canceledAt !== null) return 'canceled';
    if (Date.now() - job.startedAt < MOCK_PIPELINE_DURATION_MS) return 'running';
    return 'completed_with_issues';
  }

  private mockPipelineStart(request: StartPipelineRequest): { jobId: string } {
    if (this.mockPipeline && this.mockPipelineState(this.mockPipeline) === 'running') {
      throw new IpcError('Já existe uma análise em andamento (demonstração).', 'PIPELINE_IN_PROGRESS');
    }
    const job: MockPipelineJob = {
      jobId: `mock_pipeline_${Date.now()}`,
      mediaIds: request?.mediaIds ?? null,
      startedAt: Date.now(),
      canceledAt: null,
      applied: false
    };
    this.mockPipeline = job;
    return { jobId: job.jobId };
  }

  /**
   * Status simulado: transcrição e silêncio terminam 'unavailable' (sem Whisper/VAD reais),
   * etapas dependentes ficam 'skipped'. Nenhum texto ou candidato é inventado.
   */
  private mockPipelineStatus(projectId: string | undefined): PipelineStatusDto {
    const media = this.currentMockMedia();
    const job = this.mockPipeline;
    const targets = job?.mediaIds ? media.filter((m) => job.mediaIds?.includes(m.mediaId)) : media;
    const environment = this.mockEnvironment();

    if (!job) {
      return {
        jobId: null,
        projectId: projectId ?? '',
        state: 'idle',
        stages: MOCK_PIPELINE_STAGE_ORDER.map(({ stage, label }) => ({
          stage,
          label,
          state: 'pending' as StageState,
          processedItems: 0,
          totalItems: media.length,
          reason: null
        })),
        media: [],
        currentMedia: null,
        environment,
        startedAtUtc: null,
        finishedAtUtc: null
      };
    }

    const state = this.mockPipelineState(job);
    const total = targets.length;
    const stageDefs: Record<PipelineStage, { state: StageState; reason: string | null; processed: number }> = {
      transcription: { state: 'unavailable', reason: 'DEMO: Whisper não encontrado no ambiente simulado.', processed: 0 },
      silence: { state: 'unavailable', reason: 'DEMO: detecção de silêncio indisponível neste ambiente simulado.', processed: 0 },
      hesitation: { state: 'skipped', reason: 'Depende da transcrição, que está indisponível (demonstração).', processed: 0 },
      scene: { state: 'unavailable', reason: 'DEMO: classificação de cena não executada (simulação).', processed: 0 },
      candidates: { state: 'skipped', reason: 'Depende de silêncios e hesitações (demonstração).', processed: 0 }
    };

    let stages: PipelineStageStatusDto[];
    let mediaStages: Partial<Record<PipelineStage, StageState>>;
    if (state === 'running') {
      stages = MOCK_PIPELINE_STAGE_ORDER.map(({ stage, label }) => ({
        stage,
        label,
        state: stage === 'transcription' ? 'running' : 'pending',
        processedItems: 0,
        totalItems: total,
        reason: null
      }));
      mediaStages = { transcription: 'running', silence: 'pending', hesitation: 'pending', scene: 'pending', candidates: 'pending' };
    } else if (state === 'canceled') {
      stages = MOCK_PIPELINE_STAGE_ORDER.map(({ stage, label }) => ({
        stage, label, state: 'canceled' as StageState, processedItems: 0, totalItems: total, reason: 'Cancelado (demonstração).'
      }));
      mediaStages = { transcription: 'canceled', silence: 'canceled', hesitation: 'canceled', scene: 'canceled', candidates: 'canceled' };
    } else {
      stages = MOCK_PIPELINE_STAGE_ORDER.map(({ stage, label }) => ({
        stage,
        label,
        state: stageDefs[stage].state,
        processedItems: stageDefs[stage].processed,
        totalItems: total,
        reason: stageDefs[stage].reason
      }));
      mediaStages = {
        transcription: stageDefs.transcription.state,
        silence: stageDefs.silence.state,
        hesitation: stageDefs.hesitation.state,
        scene: stageDefs.scene.state,
        candidates: stageDefs.candidates.state
      };
      if (!job.applied) {
        job.applied = true;
        for (const m of targets) this.mockAnalyzedMediaIds.add(m.mediaId);
      }
    }

    const mediaStatuses: PipelineMediaStatusDto[] = targets.map((m) => ({
      mediaId: m.mediaId,
      relativePath: m.relativePath,
      stageStates: { ...mediaStages },
      fromCache: false,
      message: null
    }));

    return {
      jobId: job.jobId,
      projectId: projectId ?? '',
      state,
      stages,
      media: mediaStatuses,
      currentMedia: state === 'running' ? targets[0]?.mediaId ?? null : null,
      environment,
      startedAtUtc: new Date(job.startedAt).toISOString(),
      finishedAtUtc: state === 'running' ? null : new Date(job.startedAt + MOCK_PIPELINE_DURATION_MS).toISOString()
    };
  }

  // ---- Decisões (F07) — DEMONSTRAÇÃO: mesmas regras de erro do host (INVALID_ACTION, INVALID_INTERVAL,
  // SUGGESTION_NOT_FOUND, NOTHING_TO_UNDO/REDO). Sem plano no mock: planRevision sempre null.

  private mockRequireSuggestion(suggestionId: string | undefined): EditSuggestion {
    const base = this.mockSuggestions.find((s) => s.suggestionId === suggestionId);
    if (!base) {
      throw new IpcError('DEMO: candidato não encontrado no ambiente simulado.', 'SUGGESTION_NOT_FOUND');
    }
    return base;
  }

  private mockStateOf(suggestionId: string): MockDecisionState {
    let state = this.mockDecisionStates.get(suggestionId);
    if (!state) {
      const base = this.mockSuggestions.find((s) => s.suggestionId === suggestionId);
      state = {
        status: base?.status ?? 'Proposed',
        effectiveStartSeconds: null,
        effectiveEndSeconds: null,
        lastAction: null
      };
      this.mockDecisionStates.set(suggestionId, state);
    }
    return state;
  }

  private mockEffective(suggestionId: string): EditSuggestion {
    const base = this.mockRequireSuggestion(suggestionId);
    const state = this.mockStateOf(suggestionId);
    return {
      ...base,
      status: state.status,
      effectiveStartSeconds: state.effectiveStartSeconds,
      effectiveEndSeconds: state.effectiveEndSeconds,
      canUndo: (this.mockDecisionPast.get(suggestionId)?.length ?? 0) > 0,
      canRedo: (this.mockDecisionFuture.get(suggestionId)?.length ?? 0) > 0,
      // F14.8: sem plano autoaplicado no mock, só existe origem humana (decisão ativa).
      origin: (this.mockDecisionPast.get(suggestionId)?.length ?? 0) > 0 ? 'human' : null,
      policyVersion: null,
      policyReason: null,
      parameters: state.parameters ?? base.parameters ?? null
    };
  }

  /** Grava novo estado: o anterior vai para o undo e o redo é invalidado (como no host). */
  private mockCommit(suggestionId: string, next: MockDecisionState): void {
    const current = this.mockStateOf(suggestionId);
    const past = this.mockDecisionPast.get(suggestionId) ?? [];
    past.push(current);
    this.mockDecisionPast.set(suggestionId, past);
    this.mockDecisionFuture.set(suggestionId, []);
    this.mockDecisionStates.set(suggestionId, next);
  }

  private mockLog(
    suggestionId: string,
    action: DecisionAction | 'undo',
    reviewer: string,
    notes: string,
    state: MockDecisionState
  ): void {
    this.mockDecisionSeq += 1;
    // version é por sugestão (contador do próprio candidato), como no host; decisionId é global.
    const version = (this.mockDecisionVersionOf.get(suggestionId) ?? 0) + 1;
    this.mockDecisionVersionOf.set(suggestionId, version);
    this.mockDecisionLog.push({
      decisionId: `mock_dec_${this.mockDecisionSeq}`,
      suggestionId,
      action,
      reviewer,
      notes,
      timestampUtc: new Date().toISOString(),
      version,
      baseRevision: null,
      sourceStartSeconds: state.effectiveStartSeconds,
      sourceEndSeconds: state.effectiveEndSeconds,
      parameters: state.parameters ?? null
    });
  }

  private mockDecisionResult(suggestionIds: string[], recordedCount: number, missing: string[]): DecisionResultDto {
    const approvedCount = this.mockSuggestions.filter(
      (s) => this.mockStateOf(s.suggestionId).status === 'Approved'
    ).length;
    this.mockProjectSummary.approvedCutsCount = approvedCount;
    return {
      suggestions: suggestionIds.map((id) => this.mockEffective(id)),
      recordedCount,
      missingSuggestionIds: missing,
      planRevision: null,
      planChanged: false,
      planError: null
    };
  }

  private mockSubmitDecision(request: SubmitDecisionRequest): DecisionResultDto {
    const suggestionId = request?.suggestionId;
    const target = this.mockRequireSuggestion(suggestionId);
    if (!request?.action || !MOCK_DECISION_ACTIONS.includes(request.action)) {
      throw new IpcError('DEMO: ação de decisão inválida.', 'INVALID_ACTION');
    }
    const id = suggestionId as string;
    const action = request.action;
    if (request.parameters?.zoom && action !== 'approve' && action !== 'modify') {
      throw new IpcError('DEMO: parameters só é aceito em approve ou modify.', 'INVALID_ARGUMENT');
    }
    const normalized = mockNormalizeParameters(request.parameters, target.suggestedAction);
    const inheritedParameters = normalized ?? this.mockDecisionStates.get(id)?.parameters ?? null;
    let next: MockDecisionState;
    if (action === 'modify') {
      const start = request.modifiedStartSeconds;
      const end = request.modifiedEndSeconds;
      if (
        typeof start !== 'number' || typeof end !== 'number' ||
        !Number.isFinite(start) || !Number.isFinite(end) || start < 0 || end <= start
      ) {
        throw new IpcError('DEMO: intervalo inválido (entrada deve ser menor que saída e não negativa).', 'INVALID_INTERVAL');
      }
      next = {
        status: 'Approved', effectiveStartSeconds: start, effectiveEndSeconds: end, lastAction: 'modify',
        parameters: inheritedParameters
      };
    } else {
      // Como no host: approve/reject/defer mantêm o intervalo efetivo atual (ajuste de modify não se perde).
      const current = this.mockDecisionStates.get(id);
      next = {
        status: action === 'approve' ? 'Approved' : action === 'reject' ? 'Rejected' : 'Deferred',
        effectiveStartSeconds: current?.effectiveStartSeconds ?? null,
        effectiveEndSeconds: current?.effectiveEndSeconds ?? null,
        lastAction: action,
        parameters: inheritedParameters
      };
    }
    this.mockCommit(id, next);
    this.mockLog(id, action, request.reviewer ?? 'Editor', request.notes ?? '', next);
    return this.mockDecisionResult([id], 1, []);
  }

  private mockUndoDecision(suggestionId: string | undefined): DecisionResultDto {
    this.mockRequireSuggestion(suggestionId);
    const id = suggestionId as string;
    const past = this.mockDecisionPast.get(id) ?? [];
    if (past.length === 0) {
      throw new IpcError('DEMO: nada para desfazer neste candidato.', 'NOTHING_TO_UNDO');
    }
    const restored = past.pop() as MockDecisionState;
    const future = this.mockDecisionFuture.get(id) ?? [];
    future.push(this.mockStateOf(id));
    this.mockDecisionFuture.set(id, future);
    this.mockDecisionStates.set(id, restored);
    this.mockLog(id, 'undo', 'Editor', '', restored);
    return this.mockDecisionResult([id], 1, []);
  }

  private mockRedoDecision(suggestionId: string | undefined): DecisionResultDto {
    this.mockRequireSuggestion(suggestionId);
    const id = suggestionId as string;
    const future = this.mockDecisionFuture.get(id) ?? [];
    const next = future.pop();
    if (!next || next.lastAction === null) {
      throw new IpcError('DEMO: nada para refazer neste candidato.', 'NOTHING_TO_REDO');
    }
    const past = this.mockDecisionPast.get(id) ?? [];
    past.push(this.mockStateOf(id));
    this.mockDecisionPast.set(id, past);
    this.mockDecisionFuture.set(id, future);
    this.mockDecisionStates.set(id, next);
    // Redo regrava a ação original no log (append-only).
    this.mockLog(id, next.lastAction, 'Editor', '', next);
    return this.mockDecisionResult([id], 1, []);
  }

  /** Lote atômico: valida tudo antes de gravar; ids inexistentes voltam em missingSuggestionIds. */
  private mockBatchDecide(request: BatchDecideRequest): DecisionResultDto {
    if (!request?.action || !MOCK_BATCH_ACTIONS.includes(request.action)) {
      throw new IpcError('DEMO: ação inválida para lote (modify não é permitido).', 'INVALID_ACTION');
    }
    const ids = Array.from(new Set(Array.isArray(request.suggestionIds) ? request.suggestionIds : []));
    const known = ids.filter((id) => this.mockSuggestions.some((s) => s.suggestionId === id));
    const missing = ids.filter((id) => !known.includes(id));
    // F14.8: parâmetros em lote só com approve e só para sugestões de zoom (tudo ou nada: valida antes de gravar).
    if (request.parameters?.zoom && request.action !== 'approve') {
      throw new IpcError('DEMO: parameters em lote só é aceito com approve.', 'INVALID_ARGUMENT');
    }
    const batchParameters = new Map<string, DecisionParametersDto | null>();
    for (const id of known) {
      batchParameters.set(id, mockNormalizeParameters(request.parameters, this.mockRequireSuggestion(id).suggestedAction));
    }
    for (const id of known) {
      const status: EditSuggestion['status'] =
        request.action === 'approve' ? 'Approved' : request.action === 'reject' ? 'Rejected' : 'Deferred';
      const current = this.mockDecisionStates.get(id);
      const next: MockDecisionState = {
        status,
        effectiveStartSeconds: current?.effectiveStartSeconds ?? null,
        effectiveEndSeconds: current?.effectiveEndSeconds ?? null,
        lastAction: request.action,
        parameters: batchParameters.get(id) ?? current?.parameters ?? null
      };
      this.mockCommit(id, next);
      this.mockLog(id, request.action, request.reviewer ?? 'Editor', request.notes ?? '', next);
    }
    return this.mockDecisionResult(known, known.length, missing);
  }

  /** Análise por mídia simulada: 'not_run' até uma execução de demonstração; depois 'unavailable' com motivo. */
  private mockMediaAnalysis(mediaId: string | undefined): MediaAnalysisDto {
    const analyzed = !!mediaId && this.mockAnalyzedMediaIds.has(mediaId);
    const reason = 'DEMO: Whisper não encontrado no ambiente simulado.';
    return {
      mediaId: mediaId ?? '',
      transcriptionStatus: analyzed ? 'unavailable' : 'not_run',
      transcriptionReason: analyzed ? reason : null,
      transcript: null,
      silenceStatus: analyzed ? 'unavailable' : 'not_run',
      silenceReason: analyzed ? 'DEMO: detecção de silêncio indisponível neste ambiente simulado.' : null,
      silences: [],
      hesitations: [],
      scene: null,
      cutCandidates: [],
      analyzedAtUtc: analyzed ? new Date().toISOString() : null
    };
  }

  /** Progresso simulado por tempo decorrido. Existe somente no modo demonstração (DEMO). */
  private mockImportStatus(jobId: string | undefined): ImportJobStatusDto {
    const job = jobId ? this.mockImportJobs.get(jobId) : undefined;
    if (!job) {
      throw new Error('Job de importação não encontrado (modo demonstração).');
    }

    const { request } = job;
    const total = request.paths.length;
    const now = Date.now();
    const elapsed = Math.min(MOCK_IMPORT_DURATION_MS, (job.canceledAt ?? now) - job.startedAt);
    const fraction = elapsed / MOCK_IMPORT_DURATION_MS;
    const isCanceled = job.canceledAt !== null;
    const isDone = !isCanceled && elapsed >= MOCK_IMPORT_DURATION_MS;
    const processed = isDone ? total : Math.floor(fraction * total);
    const state: ImportJobStatusDto['state'] = isCanceled ? 'canceled' : isDone ? 'completed' : 'running';

    let stage: ImportJobStatusDto['stage'];
    if (isDone) stage = 'done';
    else if (fraction < 0.1) stage = 'preflight';
    else if (fraction < 0.5) stage = request.copyFiles ? 'copy' : 'metadata';
    else if (fraction < 0.7) stage = 'metadata';
    else if (fraction < 0.9) stage = 'thumbnails';
    else stage = 'register';

    const items: ImportItemStatusDto[] = request.paths.map((sourcePath, index) => {
      const name = mockBasename(sourcePath);
      let result: ImportItemResult = 'pending';
      if (isCanceled && index >= processed) {
        result = 'canceled';
      } else if (index < processed) {
        result = request.copyFiles
          ? (index === total - 1 && total > 1 ? 'renamed' : 'imported')
          : 'registered_in_place';
      } else if (index === processed && !isDone) {
        result = request.copyFiles ? 'copying' : 'pending';
      }
      const destinationRelativePath =
        request.copyFiles && (result === 'imported' || result === 'renamed')
          ? `${request.destination}\\${name}`
          : null;
      return {
        sourcePath,
        result,
        destinationRelativePath,
        message: result === 'renamed' ? 'DEMO: nome ajustado para evitar conflito' : null
      };
    });

    // Mesmo comportamento esperado do backend real: sem análise de fala disponível, status 'unavailable' do início ao fim.
    if (isDone && !job.applied) {
      job.applied = true;
      if (this.mockFreshProject) {
        const added = request.paths
          .filter((path) => mockKindOf(mockBasename(path)) !== null)
          .map((path) => mockAssetFromCandidate(mockCandidate(path), request.projectId, request.destination));
        const known = new Set(this.mockImportedAssets.map((asset) => asset.mediaId));
        for (const asset of added) {
          if (!known.has(asset.mediaId)) this.mockImportedAssets.push(asset);
        }
        this.mockProjectSummary.mediaCount = this.mockImportedAssets.length;
      }
    }

    const bytesTotal = total * 1_000_000;
    return {
      jobId: job.jobId,
      projectId: request.projectId,
      state,
      stage,
      totalFiles: total,
      processedFiles: processed,
      bytesTotal,
      bytesCopied: isDone ? bytesTotal : Math.round(fraction * bytesTotal),
      currentFile: !isDone && processed < total ? request.paths[processed] : null,
      speechAnalysis: request.detectSpeech ? 'unavailable' : 'not_requested',
      message: isDone
        ? 'Modo demonstração: nenhum arquivo foi copiado ou analisado de verdade.'
        : isCanceled
          ? 'Importação cancelada (demonstração).'
          : null,
      items,
      startedAtUtc: new Date(job.startedAt).toISOString(),
      finishedAtUtc: state === 'running' ? null : new Date(now).toISOString()
    };
  }

  // ---- Análise audiovisual e pipeline (F05) — nomes conforme types/ipc.ts ----

  /** refresh=true faz o host ignorar o cache de ambiente (após instalar ou configurar). */
  public async getAnalysisEnvironment(refresh = false): Promise<AnalysisEnvironmentDto> {
    return this.send<AnalysisEnvironmentDto>('analysis.environment', refresh ? { refresh: true } : undefined, {
      timeoutMs: ENVIRONMENT_IPC_TIMEOUT_MS
    });
  }

  /** Capacidades de animação (motion/1); a leitura real vem do host, nunca de constantes. */
  public async getMotionCapabilities(): Promise<MotionCapabilitiesDto> {
    return this.send<MotionCapabilitiesDto>('motion.capabilities');
  }

  /**
   * Abre projeto existente. autoCreateFolders=false por padrão: o host responde INVALID_FOUR_FOLDERS
   * quando faltam pastas canônicas, e a UI deve pedir confirmação antes de reenviar com true.
   */
  public async openProject(rootPath: string, autoCreateFolders: boolean): Promise<{ summary: ProjectSummary }> {
    return this.send<{ summary: ProjectSummary }>(
      'project.open',
      { rootPath, autoCreateFolders },
      { timeoutMs: PROJECT_IPC_TIMEOUT_MS }
    );
  }

  public async createProject(
    name: string,
    rootPath: string,
    createFoldersIfNotExists: boolean
  ): Promise<{ summary: ProjectSummary }> {
    return this.send<{ summary: ProjectSummary }>(
      'project.create',
      { name, rootPath, createFoldersIfNotExists },
      { timeoutMs: PROJECT_IPC_TIMEOUT_MS }
    );
  }

  /** Erro PIPELINE_IN_PROGRESS chega como IpcError com code. */
  public async startPipeline(request: StartPipelineRequest): Promise<{ jobId: string }> {
    return this.send<{ jobId: string }>('pipeline.start', request);
  }

  public async getPipelineStatus(projectId: string): Promise<PipelineStatusDto> {
    return this.send<PipelineStatusDto>('pipeline.status', { projectId });
  }

  /** Diagnóstico técnico do worker (contém caminhos locais): chamar só após ação explícita do usuário. */
  public async getPipelineDiagnostics(projectId: string): Promise<PipelineDiagnosticsDto> {
    return this.send<PipelineDiagnosticsDto>('pipeline.diagnostics', { projectId });
  }

  /** Reenfileira só as mídias com etapa falha/cancelada do último job. Erro NO_MEDIA_TO_ANALYZE se nada falhou. */
  public async retryFailedPipeline(projectId: string): Promise<RetryFailedPipelineResponse> {
    return this.send<RetryFailedPipelineResponse>('pipeline.retryFailed', { projectId });
  }

  public async cancelPipeline(jobId: string): Promise<{ canceled: boolean }> {
    return this.send<{ canceled: boolean }>('pipeline.cancel', { jobId });
  }

  public async getMediaAnalysis(projectId: string, mediaId: string): Promise<MediaAnalysisDto> {
    return this.send<MediaAnalysisDto>('analysis.getMedia', { projectId, mediaId });
  }

  public async listSuggestions(projectId: string): Promise<EditSuggestion[]> {
    const res = await this.send<{ suggestions: EditSuggestion[] } | EditSuggestion[]>('suggestions.list', { projectId });
    if (Array.isArray(res)) return res;
    return res?.suggestions ?? [];
  }

  public async scanMedia(projectId: string, brutePath?: string): Promise<ScanMediaResultDto> {
    return this.send<ScanMediaResultDto>('media.scan', { projectId, brutePath });
  }

  public async listMedia(projectId: string): Promise<MediaAssetDto[]> {
    const res = await this.send<MediaAssetDto[] | { media: MediaAssetDto[] }>('media.list', { projectId });
    if (Array.isArray(res)) return res;
    if (res && typeof res === 'object' && Array.isArray((res as any).media)) return (res as any).media;
    throw new Error(
      `Falha de contrato IPC em 'media.list': esperava array MediaAssetDto[], mas recebeu payload inválido (${res === null ? 'null' : typeof res}).`
    );
  }

  public async getMediaDetails(mediaId: string): Promise<MediaDetailsDto> {
    return this.send<MediaDetailsDto>('media.details', { mediaId });
  }

  public async getChronology(projectId: string, thresholdSeconds?: number): Promise<ChronologyReportDto> {
    return this.send<ChronologyReportDto>('media.chronology', { projectId, thresholdSeconds });
  }

  // ---- Plano editorial (F06) — nomes conforme types/ipc.ts ----

  /** null = ainda não há plano para o projeto. revision null/omitido = plano mais recente. */
  public async getEditPlan(projectId: string, revision: number | null = null): Promise<EditPlanDto | null> {
    const plan = await this.send<EditPlanDto | null>('plan.get', { projectId, revision });
    return plan ?? null;
  }

  /** Cria nova revisão a partir das mídias analisadas. Erros: NO_MEDIA_TO_PLAN, PROJECT_NOT_FOUND. */
  /** profile null usa o perfil persistido do projeto; informe o perfil da base para reproduzi-lo. */
  public async generateEditPlan(projectId: string, profile: EditorialProfileDto | null = null): Promise<EditPlanDto> {
    const request: GenerateEditPlanRequest = { projectId, profile, timeBase: null };
    return this.send<EditPlanDto>('plan.generate', request, { timeoutMs: PLAN_IPC_TIMEOUT_MS });
  }

  public async listPlanRevisions(projectId: string): Promise<PlanRevisionInfoDto[]> {
    const res = await this.send<PlanRevisionsResponse | PlanRevisionInfoDto[]>('plan.list', { projectId });
    if (Array.isArray(res)) return res;
    return (res as any)?.revisions ?? [];
  }

  public async validatePlan(projectId: string, revision: number): Promise<PlanValidationDto> {
    return this.send<PlanValidationDto>('plan.validate', { projectId, revision });
  }

  /** Cria uma nova revisão com o conteúdo de toRevision (o histórico é preservado). */
  public async revertPlan(projectId: string, toRevision: number): Promise<EditPlanDto> {
    return this.send<EditPlanDto>('plan.revert', { projectId, toRevision }, { timeoutMs: PLAN_IPC_TIMEOUT_MS });
  }

  /** F14.2: Grava uma revisão editorial manual (não destrutiva) no plano. */
  /** baseRevision deve ser a revisão vigente carregada (senão o host recusa com PLAN_STALE_BASE). */
  public async savePlanRevision(
    projectId: string,
    plan: EditPlanDto,
    baseRevision: number,
    author: string = 'user',
    operations?: PlanEditorialOperationDto[]
  ): Promise<EditPlanDto> {
    return this.send<EditPlanDto>(
      'plan.saveManualRevision',
      { projectId, plan, baseRevision, author, operations },
      { timeoutMs: PLAN_IPC_TIMEOUT_MS }
    );
  }

  /** F14.2: Reconcilia edições manuais com uma nova revisão gerada automaticamente (F14.1). */
  public async reconcilePlan(projectId: string, targetRevision?: number): Promise<ReconcilePlanResponse> {
    return this.send<ReconcilePlanResponse>(
      'plan.reconcile',
      { projectId, targetRevision },
      { timeoutMs: PLAN_IPC_TIMEOUT_MS }
    );
  }

  public async getEditorialProfile(): Promise<EditorialProfileDto> {
    return this.send<EditorialProfileDto>('profile.get');
  }

  /** F14.8: perfil de automação editorial persistido do projeto (padrão 'balanced', isDefault=true). */
  public async getAutomationProfile(projectId: string): Promise<AutomationProfileDto> {
    return this.send<AutomationProfileDto>('automation.profile.get', { projectId });
  }

  /** F14.8: define o perfil de automação do projeto. Erros: INVALID_ARGUMENT, PROJECT_NOT_FOUND. */
  public async setAutomationProfile(projectId: string, profile: AutomationProfile): Promise<AutomationProfileDto> {
    return this.send<AutomationProfileDto>('automation.profile.set', { projectId, profile });
  }

  /** F14.8: opções de automação do projeto (autoProcess padrão true). */
  public async getAutomationOptions(projectId: string): Promise<AutomationOptionsDto> {
    return this.send<AutomationOptionsDto>('automation.options.get', { projectId });
  }

  /** F14.8: liga/desliga o processamento automático pós-análise. Erros: INVALID_ARGUMENT, PROJECT_NOT_FOUND. */
  public async setAutomationOptions(projectId: string, autoProcess: boolean): Promise<AutomationOptionsDto> {
    return this.send<AutomationOptionsDto>('automation.options.set', { projectId, autoProcess });
  }

  // ---- Importação de mídia (Tela 07) — nomes conforme types/ipc.ts ----

  public async listDirectory(path: string | null): Promise<ListDirectoryResponse> {
    return this.send<ListDirectoryResponse>('fs.listDirectory', { path });
  }

  public async selectFiles(filter: 'video' | 'assets'): Promise<SelectFilesResponse> {
    return this.send<SelectFilesResponse>('dialog.selectFiles', { filter });
  }

  public async selectFolder(): Promise<{ selectedPath: string; canceled: boolean }> {
    return this.send<{ selectedPath: string; canceled: boolean }>('dialog.selectFolder');
  }

  public async openFolder(path: string): Promise<unknown> {
    return this.send('system.openFolder', { path });
  }

  /** Caminhos de arquivos ou pastas; pastas são expandidas pelo host. */
  public async inspectCandidates(projectId: string, paths: string[]): Promise<InspectCandidatesResponse> {
    return this.send<InspectCandidatesResponse>(
      'media.inspectCandidates',
      { projectId, paths },
      { timeoutMs: INSPECT_IPC_TIMEOUT_MS }
    );
  }

  public async getThumbnail(projectId: string, path: string): Promise<ThumbnailResponse> {
    return this.send<ThumbnailResponse>(
      'media.getThumbnail',
      { projectId, path },
      { timeoutMs: THUMBNAIL_IPC_TIMEOUT_MS }
    );
  }

  public async startImport(request: StartImportRequest): Promise<{ jobId: string }> {
    return this.send<{ jobId: string }>('media.import.start', request);
  }

  public async getImportStatus(jobId: string): Promise<ImportJobStatusDto> {
    return this.send<ImportJobStatusDto>('media.import.status', { jobId });
  }

  public async cancelImport(jobId: string): Promise<unknown> {
    return this.send('media.import.cancel', { jobId });
  }

  /** Resolve caminhos reais dos arquivos arrastados para a janela (somente WebView2 fornece). */
  public async resolveDroppedPaths(files: File[]): Promise<string[]> {
    const res = await this.sendWithFiles<{ paths: string[] }>('dnd.resolvePaths', files);
    return res?.paths ?? [];
  }

  // ---- Decisões (F07) — nomes conforme types/ipc.ts. Erros chegam como IpcError com code. ----

  /** approve | reject | defer | modify (modify exige modifiedStartSeconds/modifiedEndSeconds). */
  public async submitDecision(request: SubmitDecisionRequest): Promise<DecisionResultDto> {
    return this.send<DecisionResultDto>('decision.submit', request, { timeoutMs: DECISION_IPC_TIMEOUT_MS });
  }

  /** Erro NOTHING_TO_UNDO quando não há decisão anterior. */
  public async undoDecision(suggestionId: string): Promise<DecisionResultDto> {
    return this.send<DecisionResultDto>('decision.undo', { suggestionId }, { timeoutMs: DECISION_IPC_TIMEOUT_MS });
  }

  /** Erro NOTHING_TO_REDO quando não há desfazer pendente para refazer. */
  public async redoDecision(suggestionId: string): Promise<DecisionResultDto> {
    return this.send<DecisionResultDto>('decision.redo', { suggestionId }, { timeoutMs: DECISION_IPC_TIMEOUT_MS });
  }

  /** Lote atômico; modify é proibido (INVALID_ACTION). */
  public async batchDecide(request: BatchDecideRequest): Promise<DecisionResultDto> {
    return this.send<DecisionResultDto>('suggestions.batchDecide', request, { timeoutMs: DECISION_IPC_TIMEOUT_MS });
  }

  /** Histórico append-only de decisões do projeto. */
  public async listDecisions(projectId: string): Promise<EditDecisionDto[]> {
    const res = await this.send<{ decisions: EditDecisionDto[] }>('decision.list', { projectId });
    return res?.decisions ?? [];
  }

  /** Cria pré-corte manual com intervalo marcado pelo usuário na Biblioteca (F14.1). */
  public async createManualSuggestion(params: {
    projectId: string;
    mediaId: string;
    startSeconds: number;
    endSeconds: number;
    rationale?: string;
  }): Promise<{ suggestion: EditSuggestion }> {
    return this.send<{ suggestion: EditSuggestion }>('suggestion.createManual', params, { timeoutMs: DECISION_IPC_TIMEOUT_MS });
  }

  // ==========================================================================
  // Assistente de Ambiente & Instalações Assistidas (F12)
  // ==========================================================================

  public async diagnoseEnvironment(): Promise<EnvironmentAssistantDiagnosticDto> {
    return this.send<EnvironmentAssistantDiagnosticDto>('environment.diagnose');
  }

  public async diagnoseTool(toolKey: string): Promise<ToolDiagnosticDto> {
    return this.send<ToolDiagnosticDto>('environment.diagnoseTool', { toolKey });
  }

  public async getEnvironmentInstallPlan(toolKey: string): Promise<EnvironmentInstallPlanDto> {
    return this.send<EnvironmentInstallPlanDto>('environment.getInstallPlan', { toolKey });
  }

  public async startToolInstall(request: StartToolInstallRequest): Promise<{ jobId: string }> {
    return this.send<{ jobId: string }>('environment.startInstall', request, { timeoutMs: NATIVE_CONFIRMATION_IPC_TIMEOUT_MS });
  }

  public async getToolInstallProgress(jobId: string): Promise<ToolInstallProgressDto | null> {
    return this.send<ToolInstallProgressDto | null>('environment.getInstallProgress', { jobId });
  }

  public async cancelToolInstall(jobId: string): Promise<{ canceled: boolean }> {
    return this.send<{ canceled: boolean }>('environment.cancelInstall', { jobId });
  }

  public async startModelDownload(request: StartModelDownloadRequest): Promise<{ jobId: string }> {
    return this.send<{ jobId: string }>('environment.startDownloadModel', request, { timeoutMs: NATIVE_CONFIRMATION_IPC_TIMEOUT_MS });
  }

  public async getModelDownloadProgress(jobId: string): Promise<ToolInstallProgressDto | null> {
    return this.send<ToolInstallProgressDto | null>('environment.getModelDownloadProgress', { jobId });
  }

  public async cancelModelDownload(jobId: string): Promise<{ canceled: boolean }> {
    return this.send<{ canceled: boolean }>('environment.cancelModelDownload', { jobId });
  }

  public async testTool(toolKey: string): Promise<TestToolResponse> {
    return this.send<TestToolResponse>('environment.testTool', { toolKey });
  }

  public async configureToolPath(toolKey: string, path: string): Promise<{ success: boolean }> {
    return this.send<{ success: boolean }>('environment.configureToolPath', { toolKey, path });
  }

  public async autoLocateTool(toolKey: string): Promise<AutoLocateResponse> {
    return this.send<AutoLocateResponse>('environment.autoLocateTool', { toolKey });
  }

  // ==========================================================================
  // Configurações Persistentes (F12)
  // ==========================================================================

  public async getSettings(): Promise<TravelCutSettingsDto> {
    return this.send<TravelCutSettingsDto>('settings.get');
  }

  public async setToolPath(toolKey: string, path: string): Promise<{ success: boolean }> {
    return this.send<{ success: boolean }>('settings.setToolPath', { toolKey, path });
  }

  public async setMediaSourceFolders(folders: string[]): Promise<{ success: boolean }> {
    return this.send<{ success: boolean }>('settings.setMediaSourceFolders', { folders });
  }

  public async addMediaSourceFolder(folder: string): Promise<{ success: boolean }> {
    return this.send<{ success: boolean }>('settings.addMediaSourceFolder', { folder });
  }

  public async removeMediaSourceFolder(folder: string): Promise<{ success: boolean }> {
    return this.send<{ success: boolean }>('settings.removeMediaSourceFolder', { folder });
  }
}

export const ipc = new IpcBridgeService();
