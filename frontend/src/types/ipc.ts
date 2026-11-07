export interface IpcRequest<T = any> {
  version: number;
  requestId: string;
  type: string;
  payload?: T;
}

export interface IpcResponse<T = any> {
  version: number;
  requestId: string;
  ok?: boolean;
  success?: boolean;
  payload?: T;
  data?: T;
  error?: {
    code: string;
    message: string;
    details?: string;
  };
  errorCode?: string;
  errorMessage?: string;
  errorDetails?: string;
}

export interface ProjectSummary {
  projectId: string;
  name: string;
  rootPath: string;
  description?: string;
  fourFoldersValid: boolean;
  missingFolder?: string;
  mediaCount: number;
  assetsCount?: number;
  suggestionsCount: number;
  approvedCutsCount: number;
  pendingReviewsCount?: number;
  totalOriginalDurationSeconds: number;
  estimatedCutDurationSeconds: number | null; // null até existir corte real (nunca estimado por constante)
  mediaWithoutMetadataCount?: number;
  lastModifiedUtc: string;
}


// ---- Decisões (F07) — espelho de src/TravelCut.Contracts/Dtos/EditDtos.cs ----
// Append-only; estado efetivo = última decisão. "modify" = aprovar com intervalo ajustado.
// Após decidir, o host regenera o plano (nova revisão só se o conteúdo mudar).

export type DecisionAction = 'approve' | 'reject' | 'defer' | 'modify';

export interface EditDecisionDto {
  decisionId: string;
  suggestionId: string;
  action: DecisionAction | 'undo'; // redo regrava a ação original
  reviewer: string;
  notes: string;
  timestampUtc: string;
  version: number;
  baseRevision?: number | null;
  sourceStartSeconds?: number | null;
  sourceEndSeconds?: number | null;
}

export interface SubmitDecisionRequest {
  suggestionId: string;
  action: DecisionAction;
  reviewer: string;
  notes?: string | null;
  modifiedStartSeconds?: number | null; // obrigatório em "modify"
  modifiedEndSeconds?: number | null;
}

export interface UndoDecisionRequest {
  suggestionId: string;
}

export interface RedoDecisionRequest {
  suggestionId: string;
}

export interface BatchDecideRequest {
  suggestionIds: string[];
  action: 'approve' | 'reject' | 'defer';
  reviewer: string;
  notes?: string | null;
}

export interface DecisionResultDto {
  suggestions: EditSuggestion[];      // estado efetivo atualizado das afetadas
  recordedCount: number;
  missingSuggestionIds: string[];
  planRevision: number | null;        // null = projeto sem plano
  planChanged: boolean;               // nova revisão criada por esta decisão
  planError: string | null;           // regeneração falhou (decisão continua gravada)
}

export interface EditSuggestion {
  suggestionId: string;
  mediaId: string;
  startSeconds: number;
  endSeconds: number;
  kind: 'silence' | 'pause' | 'filler' | 'prolongation' | 'repetition' | 'false_start' | string;
  rationale: string;
  confidenceBand: 'low' | 'medium' | 'high' | string;
  needsHumanReview: boolean;
  suggestedAction: string;
  status: 'Proposed' | 'Approved' | 'Rejected' | 'Deferred' | string;
  author: string;
  effectiveStartSeconds?: number | null; // intervalo ajustado por "modify"
  effectiveEndSeconds?: number | null;
  canUndo?: boolean;
  canRedo?: boolean;
}

// ---- Prévia (F07) — espelho de src/TravelCut.Contracts/Dtos/PreviewDtos.cs ----
// <video> real servido por https://media.travelcut.studio/<caminho relativo> (somente leitura).
// Nunca declarar frame-accurate. Sem FFmpeg: waveform/proxy "ffmpeg_unavailable".

export interface PlaybackSourceDto {
  mediaId: string;
  status: 'original' | 'proxy' | 'unavailable';
  url: string | null;
  mimeType: string | null;
  codec: string | null;
  likelyPlayable: boolean | null;
  proxyStatus: 'none' | 'ready' | 'generating' | 'failed' | 'ffmpeg_unavailable';
  reason: string | null;
}

export interface WaveformDto {
  status: 'ok' | 'ffmpeg_unavailable' | 'no_audio' | 'failed';
  startSeconds: number;
  bucketSeconds: number;
  peaks: number[]; // 0..1
  reason: string | null;
}

export interface ProxyItemStatusDto {
  mediaId: string;
  state: 'pending' | 'running' | 'ready' | 'failed' | 'skipped';
  message: string | null;
}

export interface ProxyJobStatusDto {
  jobId: string;
  state: 'queued' | 'running' | 'completed' | 'completed_with_errors' | 'failed' | 'canceled' | 'ffmpeg_unavailable';
  processed: number;
  total: number;
  currentMedia: string | null;
  items: ProxyItemStatusDto[];
}

export interface TimelineClip {
  clipId: string;
  mediaId: string;
  sourceInSeconds: number;
  sourceOutSeconds: number;
  timelineStartSeconds: number;
  trackId: string;
  speed: number;
  sequenceOrder: number;
}

// ---- Editor / DaVinci (F08) — espelho de src/TravelCut.Contracts/Dtos/EditorDtos.cs (contrato editor/1) ----
// Mock nunca é integração real: isSimulated=true e status SIMULATED. Versão/produto só quando lidos da API (null = desconhecido).

export type EditorEnvironmentState =
  | 'NOT_INSTALLED' | 'NOT_RUNNING' | 'SCRIPTING_DISABLED' | 'PYTHON_UNAVAILABLE'
  | 'READY_PARTIAL' | 'READY' | 'MOCK' | 'ERROR';

export type EditorOperation =
  | 'detect_capabilities' | 'create_project' | 'import_media' | 'create_timeline' | 'apply_edit_plan'
  | 'query_timeline' | 'export_preview' | 'export_project' | 'create_snapshot';

export type CapabilityVerification = 'documented' | 'mock_pass' | 'host_pending' | 'host_pass' | 'unsupported';

export type EditorResultStatus = 'SIMULATED' | 'DRY_RUN' | 'UNAVAILABLE' | 'READY_REAL' | 'PARTIAL_REAL' | 'ERROR';

export interface EditorOperationCapability {
  operation: EditorOperation;
  supported: boolean;
  verification: CapabilityVerification;
  note: string | null;
}

export interface EditorCapabilities {
  schemaVersion: string; // "editor/1"
  adapterMode: 'mock' | 'davinci';
  environmentState: EditorEnvironmentState;
  isAvailable: boolean;
  isSimulated: boolean;
  productName: string | null;
  version: string | null;
  statusMessage: string;
  reasonCode: string | null;
  operations: EditorOperationCapability[];
  checkedAtUtc: string;
  // legado (derivados de operations)
  canCreateProject: boolean;
  canImportMedia: boolean;
  canCreateTimeline: boolean;
  canApplyEditPlan: boolean;
  canExportPreview: boolean;
}

export interface EditorRejectedOp {
  kind: 'speed' | 'transition' | 'track' | 'clip' | string;
  clipId: string | null;
  code: string;
  message: string;
}

export interface EditorWarning {
  code: string;
  message: string;
  clipId: string | null;
}

export interface EditorClipPreview {
  clipId: string;
  relativePath: string;
  sourceStartFrame: number;
  sourceEndFrameExclusive: number;
  recordFrame: number;
  durationFrames: number;
}

export interface EditorApplyPreview {
  status: 'DRY_RUN';
  planId: string;
  revision: number;
  planFingerprint: string;
  assemblyFingerprint: string;
  editorProjectName: string;
  timelineName: string;
  timelineFrameRate: string;
  clips: EditorClipPreview[];
  rejected: EditorRejectedOp[];
  warnings: EditorWarning[];
  manifestRelativePath: string;
  capabilities: EditorCapabilities;
  canApply: boolean;
  blockingReasons: string[];
  requiresProjectSwitchConfirmation: boolean;
}

export interface PrepareEditorApplyRequest {
  projectId: string;
  revision: number | null;
}

export interface ApplyEditorPlanRequest {
  projectId: string;
  revision: number;
  assemblyFingerprint: string;
  confirmProjectSwitch: boolean;
  idempotencyKey: string;
}

export interface EditorStepResult {
  operation: EditorOperation;
  status: 'ok' | 'failed' | 'skipped' | 'simulated';
  message: string;
  durationMs: number | null;
}

export interface EditorClipMapping {
  clipId: string;
  editorItemId: string | null;
  observedRecordFrame: number | null;
  observedDurationFrames: number | null;
  matches: boolean | null;
}

export interface EditorApplyResult {
  schemaVersion: string; // "editor-apply/1"
  applicationId: string;
  status: EditorResultStatus;
  adapterMode: 'mock' | 'davinci';
  isSimulated: boolean;
  projectId: string;
  planId: string;
  revision: number;
  assemblyFingerprint: string;
  idempotencyKey: string;
  editorProjectName: string | null;
  timelineName: string | null;
  editorVersion: string | null;
  steps: EditorStepResult[];
  clipMappings: EditorClipMapping[];
  rejected: EditorRejectedOp[];
  warnings: EditorWarning[];
  snapshotRelativePath: string | null;
  recordRelativePath: string;
  startedAtUtc: string;
  finishedAtUtc: string;
  errorCode: string | null;
  errorMessage: string | null;
  reusedPrevious: boolean;
}

export interface EditorApplicationsResponse {
  applications: EditorApplyResult[];
}

export interface MediaAssetDto {
  mediaId: string;
  projectId: string;
  relativePath: string;
  byteLength: number;
  kind: string;
  codec: string;
  width: number;
  height: number;
  fps: number;
  durationSeconds: number;
  creationTimeUtc?: string | null;
  hasTranscript: boolean;
  hasVAD: boolean;
  sceneClassification?: string | null;
  thumbnailUrl?: string | null;
  tags?: string[];
  metadataStatus?: MetadataStatus;
}

// ---- Importação de mídia (Tela 07) — espelho de src/TravelCut.Contracts/Dtos/ImportDtos.cs ----
// Valores desconhecidos chegam como null: exibir "—", nunca estimar.

export type MediaKind = 'Video' | 'Audio' | 'Image';
export type ImportDestination = '1-Bruto' | '2-Assets';

export interface DirectoryEntryDto {
  name: string;
  fullPath: string;
  isDirectory: boolean;
  byteLength?: number | null;
  kind?: MediaKind | null;
}

export interface ListDirectoryResponse {
  path: string | null; // null = raiz (unidades)
  parentPath: string | null;
  entries: DirectoryEntryDto[];
  truncated?: boolean; // true quando a pasta tem mais de 5000 entradas
}

export interface SelectFilesResponse {
  paths: string[];
  canceled: boolean;
}

export type MetadataStatus = 'ok' | 'ffprobe_unavailable' | 'failed' | 'not_requested';

export interface ImportCandidateDto {
  fullPath: string;
  fileName: string;
  originFolder: string;
  kind: MediaKind;
  container: string;
  byteLength: number;
  durationSeconds: number | null;
  width: number | null;
  height: number | null;
  codec: string | null;
  metadataStatus: MetadataStatus;
  insideProject: boolean;
  alreadyInLibrary: boolean;
}

export interface InspectCandidatesResponse {
  items: ImportCandidateDto[];
  ffprobeAvailable: boolean;
  truncated: boolean;
  errors: string[];
}

export interface ThumbnailResponse {
  status: 'ok' | 'ffmpeg_unavailable' | 'failed' | 'not_supported';
  dataUrl: string | null;
  reason: string | null;
}

export interface StartImportRequest {
  projectId: string;
  paths: string[];
  destination: ImportDestination;
  subfolder?: string | null;
  copyFiles: boolean;
  generateThumbnails: boolean;
  analyzeMetadata: boolean;
  detectSpeech: boolean;
  tags: string[];
}

export type ImportItemResult =
  | 'pending' | 'copying' | 'imported' | 'renamed' | 'registered_in_place'
  | 'skipped_duplicate' | 'failed' | 'canceled';

export interface ImportItemStatusDto {
  sourcePath: string;
  result: ImportItemResult;
  destinationRelativePath: string | null;
  message: string | null;
}

export type ImportJobState = 'queued' | 'running' | 'completed' | 'completed_with_errors' | 'failed' | 'canceled';

export interface ImportJobStatusDto {
  jobId: string;
  projectId: string;
  state: ImportJobState;
  stage: 'preflight' | 'copy' | 'verify' | 'metadata' | 'thumbnails' | 'register' | 'done';
  totalFiles: number;
  processedFiles: number;
  bytesTotal: number;
  bytesCopied: number;
  currentFile: string | null;
  speechAnalysis: 'not_requested' | 'queued' | 'unavailable';
  message: string | null;
  items: ImportItemStatusDto[];
  startedAtUtc: string;
  finishedAtUtc: string | null;
}

export interface ScanMediaResultDto {
  totalFilesFound: number;
  newAssetsAdded: number;
  updatedAssets: number;
  unchangedAssets: number;
  totalBytes: number;
  durationSecondsTotal: number;
}

export interface MediaDetailsDto {
  mediaId: string;
  projectId: string;
  relativePath: string;
  fullPath: string;
  byteLength: number;
  kind: string;
  fingerprint: string;
  lastWriteUtc: string;
  codec: string;
  width: number;
  height: number;
  frameRate: number;
  timeBase: string;
  durationSeconds: number;
  durationTicks: number;
  audioChannels: number;
  audioSampleRate: number;
  creationTimeUtc?: string | null;
  sourceDateEvidence: string;
  isVariableFrameRate: boolean;
}

export interface ChronologyItemDto {
  mediaId: string;
  relativePath: string;
  kind: string;
  cameraModel?: string | null;
  recordedAtUtc: string;
  durationSeconds: number;
  estimatedEndAtUtc: string;
  timestampSource: string;
  confidence: string;
  sessionIndex: number;
  anomaly?: string | null;
}

export interface ChronologySessionDto {
  sessionIndex: number;
  name: string;
  startTimeUtc: string;
  endTimeUtc: string;
  totalClips: number;
  totalDurationSeconds: number;
  cameraModels: string[];
  clips: ChronologyItemDto[];
}

export interface ChronologyReportDto {
  sessions: ChronologySessionDto[];
  totalClips: number;
  totalDurationSeconds: number;
  earliestUtc?: string | null;
  latestUtc?: string | null;
  anomaliesDetected: number;
}


// ---- Análise audiovisual e pipeline (F05) — espelho de src/TravelCut.Contracts/Dtos/SpeechAnalysisDtos.cs ----
// Nada é sintetizado: sem FFmpeg/Whisper o estado é 'unavailable' com motivo. Confiança é qualitativa.

export type ConfidenceBand = 'low' | 'medium' | 'high';
export type PipelineStage = 'transcription' | 'silence' | 'hesitation' | 'scene' | 'candidates';
export type StageState =
  | 'pending' | 'running' | 'completed' | 'completed_with_issues'
  | 'unavailable' | 'failed' | 'canceled' | 'skipped';
export type PipelineState = 'idle' | 'queued' | 'running' | 'completed' | 'completed_with_issues' | 'failed' | 'canceled';
export type AnalysisResultStatus = 'not_run' | 'completed' | 'unavailable' | 'failed';

export interface TranscriptWordDto {
  seq: number;
  start: number;
  end: number;
  text: string;
  confidence: number; // probabilidade reportada pelo Whisper
}

export interface TranscriptDto {
  transcriptId: string;
  mediaId: string;
  modelId: string;
  language: string;
  fullText: string;
  audioDurationSeconds: number;
  words: TranscriptWordDto[];
}

export interface VadSilenceSegmentDto {
  startSeconds: number;
  endSeconds: number;
  durationSeconds: number;
  noiseDb: number;
  confidence: ConfidenceBand;
}

export interface HesitationCandidateDto {
  startSeconds: number;
  endSeconds: number;
  detectedWord: string;
  type: 'hesitation' | 'false_start' | 'filler' | 'prolongation' | 'repetition' | string;
  confidenceBand: ConfidenceBand;
  contextBefore: string;
  contextAfter: string;
  needsHumanReview: boolean;
}

export interface SceneClassificationDto {
  mediaId: string;
  category: 'conversa' | 'estrada' | 'paisagem' | 'loja' | 'drone' | 'outro' | 'indeterminado' | string;
  confidenceBand: ConfidenceBand;
  isHypothesis: boolean;
  evidence: string[];
  hasSpeech: boolean | null;
}

export interface AnalysisEnvironmentDto {
  ffmpegAvailable: boolean;
  ffmpegVersion: string | null;
  pythonAvailable: boolean;
  whisperAvailable: boolean;
  whisperEngine: string | null;
  whisperModel: string;
  cudaAvailable: boolean | null;
  reasons: string[];
}

export interface StartPipelineRequest {
  projectId: string;
  mediaIds?: string[] | null;
  stages?: PipelineStage[] | null;
  language?: string | null;
  force: boolean;
}

export interface PipelineStageStatusDto {
  stage: PipelineStage;
  label: string;
  state: StageState;
  processedItems: number;
  totalItems: number;
  reason: string | null;
}

export interface PipelineMediaStatusDto {
  mediaId: string;
  relativePath: string;
  stageStates: Partial<Record<PipelineStage, StageState>>;
  fromCache: boolean;
  message: string | null;
}

export interface PipelineStatusDto {
  jobId: string | null;
  projectId: string;
  state: PipelineState;
  stages: PipelineStageStatusDto[];
  media: PipelineMediaStatusDto[];
  currentMedia: string | null;
  environment: AnalysisEnvironmentDto | null;
  startedAtUtc: string | null;
  finishedAtUtc: string | null;
  /** Atividade dentro da mídia; ausente quando não há job ou no modo demonstração. */
  activity?: PipelineActivityDto | null;
}

/** Diagnóstico do worker Whisper (pipeline.diagnostics): caminhos locais, só por ação explícita do usuário. */
export interface PipelineDiagnosticsDto {
  jobId: string | null;
  executions: WhisperExecutionDto[];
}

export interface WhisperExecutionDto {
  jobId: string;
  mediaId: string;
  requestId: string;
  mediaPath: string;
  startedAtUtc: string | null;
  finishedAtUtc: string | null;
  pythonExecutable: string;
  arguments: string[];
  workerScript: string;
  workingDirectory: string;
  environmentVariables: Record<string, string>;
  standardInput: string;
  model: string;
  language: string;
  device: string | null;
  processState: 'running' | 'completed' | 'failed' | 'canceled' | 'timed_out' | 'not_started' | string;
  exitCode: number | null;
  canceled: boolean;
  error: string | null;
  logLines: { atUtc: string; stream: 'stdout' | 'stderr' | string; text: string }[];
  droppedLogLines: number;
  diagnosticScript: string;
}

/** processedItems/totalItems contam mídias; aqui fica o que acontece dentro da mídia em análise. */
export interface PipelineActivityDto {
  currentStage: string | null;
  currentStageLabel: string | null;
  detail: string | null;
  /** Só com telemetria real do motor; null = progresso indeterminado. */
  percent: number | null;
  lastActivityAtUtc: string | null;
  cancelRequested: boolean;
  mediaCompleted: number;
  mediaTotal: number;
}

// ============================================================================
// Assistente de Ambiente e Instalações Assistidas (F12)
// ============================================================================

export type ToolEnvironmentState =
  | 'NOT_INSTALLED'
  | 'INSTALLED_NOT_FOUND'
  | 'NOT_CONFIGURED'
  | 'DEPENDENCY_MISSING'
  | 'MODEL_MISSING'
  | 'READY_CPU'
  | 'READY_GPU'
  | 'READY'
  | 'ERROR'
  | 'NOT_VERIFIED';

export interface ToolDiagnosticDto {
  toolKey: string;
  displayName: string;
  state: ToolEnvironmentState;
  isReady: boolean;
  version?: string | null;
  executablePath?: string | null;
  details: string;
  supportedActions: string[];
  winGetPackageId?: string | null;
  metadata?: Record<string, string>;
}

export interface SystemInfoDto {
  osDescription: string;
  architecture: string;
  isWindows: boolean;
  totalMemoryMb: number;
  freeDiskSpaceMb: number;
  targetCacheDirectory: string;
  hasWinget: boolean;
  wingetPath?: string | null;
  winGetAvailable?: boolean;
}

export interface EnvironmentAssistantDiagnosticDto {
  tools: ToolDiagnosticDto[];
  allReady: boolean;
  evaluatedAtUtc: string;
  timestampUtc?: string;
  systemInfo: SystemInfoDto;
}

export interface InstallStepDto {
  stepNumber: number;
  title: string;
  command: string;
  description: string;
}

export interface EnvironmentInstallPlanDto {
  toolKey: string;
  title: string;
  description: string;
  canAutoInstall: boolean;
  estimatedTime: string;
  estimatedMinutes?: number | null;
  estimatedDownloadSize: string;
  downloadSizeMb?: number | null;
  requiresElevation: boolean;
  requiresUserConfirmation: boolean;
  steps: InstallStepDto[];
  warning?: string | null;
  warningNotice?: string | null;
  dependencies?: string[];
  targetDirectory?: string | null;
  approvalToken?: string | null;
}

export interface ToolInstallProgressDto {
  jobId: string;
  toolKey: string;
  stage: string;
  progressPercent?: number | null;
  currentMessage: string;
  outputLogs: string[];
  canCancel: boolean;
  isCompleted: boolean;
  isSuccess: boolean;
  isFailed: boolean;
  isCanceled?: boolean;
  errorMessage?: string | null;
  startedAtUtc?: string | null;
  finishedAtUtc?: string | null;
  /** Telemetria real do download de modelo; ausente/null quando o host não conhece o valor. */
  bytesDownloaded?: number | null;
  totalBytes?: number | null;
}

export interface StartToolInstallRequest {
  toolKey: string;
  confirmed?: boolean;
  customInstallPath?: string | null;
  preferCuda?: boolean;
  options?: Record<string, string>;
  approvalToken?: string | null;
}

export interface StartModelDownloadRequest {
  modelName: string;
  confirmed?: boolean;
  destinationDirectory?: string | null;
}

export interface ConfigureToolPathRequest {
  toolKey: string;
  path: string;
}

export interface TestToolResponse {
  toolKey: string;
  success: boolean;
  message: string;
  details?: string | null;
  outputLogs: string[];
  logs?: string[];
}

export interface AutoLocateResponse {
  toolKey: string;
  candidates: string[];
}

export interface TravelCutSettingsDto {
  pythonPath?: string | null;
  nodePath?: string | null;
  hyperFramesCliPath?: string | null;
  ffmpegPath?: string | null;
  preferredWhisperDevice?: string | null;
  preferCuda?: boolean;
  whisperModel?: string | null;
  mediaSourceFolders: string[];
  configuredToolPaths?: Record<string, string>;
}

export interface MediaAnalysisDto {
  mediaId: string;
  transcriptionStatus: AnalysisResultStatus;
  transcriptionReason: string | null;
  transcript: TranscriptDto | null;
  silenceStatus: AnalysisResultStatus;
  silenceReason: string | null;
  silences: VadSilenceSegmentDto[];
  hesitations: HesitationCandidateDto[];
  scene: SceneClassificationDto | null;
  cutCandidates: EditSuggestion[];
  analyzedAtUtc: string | null;
}

// ---- Plano editorial (F06) — espelho de src/TravelCut.Contracts/Dtos/PlanDtos.cs ----
// Fonte em microssegundos (sourceInUs/sourceOutUs); timeline em frames do timeBase
// (frameDurationNum/frameDurationDen segundos por frame). Seções são hipóteses até revisão.

export interface EditorialProfileDto {
  profileId: string;
  version: number;
  silencePolicy: 'conservative' | 'balanced' | 'aggressive' | string;
  pace: 'calm' | 'normal' | 'fast' | string;
  jokePreservation: 'strict' | 'normal' | string;
  zoomStrength: 'off' | 'subtle' | 'medium' | string;
  speedBroll: 'off' | '2x' | '4x' | 'auto' | string;
  musicPolicy: 'suggest_only' | 'off' | string;
  reviewAmbiguous: boolean;
  autoApplyTrivialCuts: boolean;
}

export interface TimelineTimeBaseDto {
  frameDurationNum: number;
  frameDurationDen: number;
  label: string;
}

export interface PlanTrackDto {
  trackId: 'V1' | 'A1' | 'A2' | string;
  kind: 'video' | 'dialogue' | 'music' | string;
  label: string;
}

export interface PlanSectionDto {
  sectionId: string;
  order: number;
  role: 'abertura' | 'desenvolvimento' | 'encerramento' | string;
  title: string;
  rationale: string;
  isHypothesis: boolean;
  startFrame: number;
  endFrame: number;
}

export interface PlanClipDto {
  clipId: string;
  trackId: string;
  mediaId: string;
  relativePath: string;
  sectionId: string | null;
  sourceInUs: number;
  sourceOutUs: number;
  timelineStartFrame: number;
  durationFrames: number;
  speedNum: number;
  speedDen: number;
  linkedClipId: string | null;
  zoom: number | null;
  rationale: string;
  evidence: string[];
  suggestionIds: string[];
  confidenceBand: ConfidenceBand;
  needsHumanReview: boolean;
}

export interface PlanTransitionDto {
  fromClipId: string;
  toClipId: string;
  kind: 'cut' | 'dissolve' | 'fade' | string;
  durationFrames: number;
  needsHumanReview: boolean;
}

export interface PlanAppliedCutDto {
  suggestionId: string;
  mediaId: string;
  sourceInUs: number;
  sourceOutUs: number;
  kind: string;
  origin: 'approved_by_user' | 'auto_trivial' | 'residual_sub_frame';
}

export interface PlanPendingItemDto {
  suggestionId: string | null;
  mediaId: string | null;
  kind: 'suggestion_pending' | 'music_suggestion' | 'zoom_suggestion' | 'speed_suggestion' | 'media_excluded' | 'residual_segment' | string;
  clipId?: string | null; // zoom_suggestion, residual_segment
  message: string;
}

export interface PlanIssueDto {
  severity: 'error' | 'warning';
  code: string;
  message: string;
  clipId: string | null;
}

export interface PlanValidationDto {
  isValid: boolean;
  issues: PlanIssueDto[];
  validatedAtUtc: string;
}

export interface PlanSummaryDto {
  sourceDurationUs: number;
  timelineDurationFrames: number;
  removedSourceUs: number;
  approvedCutsApplied: number;
  autoCutsApplied: number;
  pendingSuggestions: number;
  excludedMedia: number;
}

export interface EditPlanDto {
  schemaVersion: string;
  planId: string;
  projectId: string;
  revision: number;
  baseRevision: number | null;
  createdAtUtc: string;
  author: string;
  profile: EditorialProfileDto;
  timeBase: TimelineTimeBaseDto;
  mediaSetFingerprint: string;
  planFingerprint: string;
  timeZoneId: string; // fuso dos títulos e evidências (preservado no revert)
  tracks: PlanTrackDto[];
  sections: PlanSectionDto[];
  clips: PlanClipDto[];
  transitions: PlanTransitionDto[];
  appliedCuts: PlanAppliedCutDto[];
  pending: PlanPendingItemDto[];
  summary: PlanSummaryDto;
  validation: PlanValidationDto;
}

export interface PlanRevisionInfoDto {
  planId: string;
  revision: number;
  baseRevision: number | null;
  createdAtUtc: string;
  author: string;
  planFingerprint: string;
  isValid: boolean;
  clipCount: number;
  timelineDurationFrames: number;
}

// ---- Animações (motion/1) — espelho de src/TravelCut.Contracts/Dtos/MotionDtos.cs ----
// Capacidade declarada por estado real. "mock" é simulado e nunca gera arquivo de vídeo.
// rendererVersion/nodeVersion são null quando o host não leu a versão (nunca inventar).

export type MotionEnvironmentState = 'NODE_UNAVAILABLE' | 'NOT_INSTALLED' | 'READY' | 'MOCK' | 'ERROR';

export type MotionTemplateId = 'title_card' | 'lower_third' | 'map_route';

export interface MotionTemplateCapabilityDto {
  templateId: MotionTemplateId | string;
  supported: boolean;
  verification: CapabilityVerification | string;
  note: string | null;
}

export interface MotionCapabilitiesDto {
  schemaVersion: string; // "motion/1"
  adapterMode: 'mock' | 'hyperframes' | string;
  environmentState: MotionEnvironmentState | string;
  isAvailable: boolean;
  isSimulated: boolean;
  nodeVersion: string | null;
  rendererVersion: string | null;
  statusMessage: string;
  reasonCode: string | null;
  templates: MotionTemplateCapabilityDto[];
  checkedAtUtc: string;
}
