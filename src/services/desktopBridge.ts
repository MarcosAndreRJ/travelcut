/**
 * TravelCut Studio - Desktop Bridge Service
 * Integrates Frontend React with the Tauri 2 Rust Native Backend.
 * Full support for both Native Tauri 2 (Rust) and Web Sandbox (mock/fallback).
 */

import { convertFileSrc } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import type { MediaAsset } from '../features/media/types';
import type {
  DiscoveredFile,
  FfmpegCapabilities,
  FilmstripOptions,
  InventoryOptions,
  JobStatus,
  MediaEngineConfig,
  MediaMetadata,
  MediaSegment,
  SegmentCategory,
  ThumbnailOptions,
  TranscodeOptions,
  TranscodeResult,
  WaveformData,
  WaveformOptions,
} from '../types/mediaEngine';

export * from '../types/mediaEngine';

export interface SystemDiagnostics {
  app_name: string;
  app_version: string;
  os: string;
  os_version: string;
  cpu_architecture: string;
  total_memory_bytes: number;
  available_memory_bytes: number;
  ffmpeg_detected: boolean;
  ffmpeg_path: string | null;
  ffprobe_detected: boolean;
  ffprobe_path: string | null;
  app_data_dir: string;
}

export interface MediaFileInfo {
  path: string;
  file_name: string;
  extension: string;
  size_bytes: number;
  modified_timestamp: number;
}

export interface ProbeResult {
  file_path: string;
  format_name: string;
  duration_seconds: number;
  bit_rate: number;
  width?: number;
  height?: number;
  video_codec?: string;
  audio_codec?: string;
  sample_rate?: number;
  channels?: number;
  raw_json?: string;
}

export interface RecentProjectItem {
  id: string;
  name: string;
  path: string;
  lastOpened: string;
  mediaCount?: number;
  totalDurationSeconds?: number;
  previewThumbnail?: string | null;
}

export interface ProjectStatePayload {
  version: string;
  updated_at: string;
  project_name: string;
  root_directory?: string;
  timeline_data: Record<string, unknown>;
  media_pool: unknown[];
  metadata: Record<string, unknown>;
}

// Declaração de tipos para globais do Tauri 2
declare global {
  interface Window {
    __TAURI_INTERNALS__?: {
      invoke: (cmd: string, args?: Record<string, unknown>) => Promise<unknown>;
    };
  }
}

/**
 * Verifica se a aplicação está em execução dentro da janela nativa do Tauri.
 */
export function isTauriDesktop(): boolean {
  return typeof window !== 'undefined' && typeof window.__TAURI_INTERNALS__?.invoke === 'function';
}

/**
 * Converte um caminho de arquivo absoluto local para uma URL de asset compativel com o elemento <video>.
 * No ambiente Tauri 2, utiliza convertFileSrc (protocolo asset://).
 * Em ambiente de teste/navegador web, retorna o proprio caminho ou URL original.
 */
export function getMediaAssetUrl(absoluteFilePath: string): string {
  if (!absoluteFilePath) return '';
  if (
    absoluteFilePath.startsWith('http://') ||
    absoluteFilePath.startsWith('https://') ||
    absoluteFilePath.startsWith('blob:') ||
    absoluteFilePath.startsWith('data:') ||
    absoluteFilePath.startsWith('asset://')
  ) {
    return absoluteFilePath;
  }

  if (isTauriDesktop()) {
    try {
      return convertFileSrc(absoluteFilePath);
    } catch (e) {
      console.warn('[DesktopBridge] Falha ao converter asset URL com convertFileSrc:', e);
    }
  }

  return absoluteFilePath;
}

/**
 * Carrega a forma de onda de um arquivo de midia via motor nativo Rust com cache.
 */
export async function loadMediaWaveform(filePath: string): Promise<WaveformData> {
  return mediaExtractWaveform(filePath);
}

/**
 * Função interna segura para invocar comandos Tauri ou retornar mock se no browser.
 */
async function safeInvoke<T>(cmd: string, args?: Record<string, unknown>, fallback?: () => Promise<T> | T): Promise<T> {
  if (isTauriDesktop() && window.__TAURI_INTERNALS__) {
    try {
      const result = await window.__TAURI_INTERNALS__.invoke(cmd, args);
      return result as T;
    } catch (err) {
      console.error(`[DesktopBridge] Erro ao invocar comando '${cmd}':`, err);
      throw err;
    }
  }

  if (fallback) {
    return await fallback();
  }

  throw new Error(`Comando nativo '${cmd}' chamado fora do ambiente desktop Tauri sem fallback.`);
}

// ============================================================================
// Core Desktop Commands
// ============================================================================

/**
 * Retorna diagnóstico completo de hardware, SO e ferramentas de mídia instaladas.
 */
export async function getDesktopDiagnostics(): Promise<SystemDiagnostics> {
  return safeInvoke<SystemDiagnostics>(
    'desktop_diagnostics',
    undefined,
    () => ({
      app_name: 'TravelCut Studio (Web Sandbox)',
      app_version: '0.1.0-web',
      os: 'WebBrowser',
      os_version: typeof navigator !== 'undefined' ? navigator.userAgent : 'Node/Mock',
      cpu_architecture: 'web-wasm',
      total_memory_bytes: 8 * 1024 * 1024 * 1024,
      available_memory_bytes: 4 * 1024 * 1024 * 1024,
      ffmpeg_detected: false,
      ffmpeg_path: null,
      ffprobe_detected: false,
      ffprobe_path: null,
      app_data_dir: '/virtual/app_data',
    })
  );
}

/**
 * Varre um diretório do disco em busca de arquivos de mídia (vídeo, áudio, imagens).
 */
export async function scanProjectFolder(folderPath: string, recursive = true): Promise<MediaFileInfo[]> {
  return safeInvoke<MediaFileInfo[]>(
    'scan_project_folder',
    { folderPath, recursive },
    () => []
  );
}

/**
 * Executa o ffprobe nativo para extrair metadados técnicos de um arquivo de mídia.
 */
export async function runMediaProbe(filePath: string): Promise<ProbeResult> {
  return safeInvoke<ProbeResult>(
    'run_ffprobe',
    { filePath },
    () => ({
      file_path: filePath,
      format_name: 'mock',
      duration_seconds: 0,
      bit_rate: 0,
    })
  );
}

/**
 * Salva o estado do projeto de forma atômica no caminho de arquivo especificado.
 */
export async function saveProjectState(filePath: string, state: ProjectStatePayload): Promise<boolean> {
  return safeInvoke<boolean>(
    'save_project_state',
    { filePath, state },
    () => {
      try {
        if (typeof localStorage !== 'undefined') {
          localStorage.setItem(`travelcut_project_${filePath}`, JSON.stringify(state));
        }
        return true;
      } catch {
        return false;
      }
    }
  );
}

/**
 * Carrega o estado de um projeto a partir do arquivo JSON no disco.
 */
export async function loadProjectState(filePath: string): Promise<ProjectStatePayload> {
  return safeInvoke<ProjectStatePayload>(
    'load_project_state',
    { filePath },
    () => {
      if (typeof localStorage !== 'undefined') {
        const saved = localStorage.getItem(`travelcut_project_${filePath}`);
        if (saved) {
          return JSON.parse(saved) as ProjectStatePayload;
        }
      }
      throw new Error(`Projeto '${filePath}' não encontrado no armazenamento local.`);
    }
  );
}

/**
 * Salva o arquivo de projeto (.travelcut ou .json) no caminho especificado.
 */
export async function saveProjectFile(filePath: string, state: ProjectStatePayload): Promise<boolean> {
  return safeInvoke<boolean>(
    'save_project_file',
    { filePath, stateJson: state },
    () => {
      try {
        if (typeof localStorage !== 'undefined') {
          localStorage.setItem(`travelcut_file_${filePath}`, JSON.stringify(state));
        }
        return true;
      } catch {
        return false;
      }
    }
  );
}

/**
 * Carrega o arquivo de projeto a partir de caminho de arquivo (.travelcut ou .json).
 */
export async function loadProjectFile(filePath: string): Promise<ProjectStatePayload> {
  return safeInvoke<ProjectStatePayload>(
    'load_project_file',
    { filePath },
    () => {
      if (typeof localStorage !== 'undefined') {
        const saved = localStorage.getItem(`travelcut_file_${filePath}`);
        if (saved) {
          return JSON.parse(saved) as ProjectStatePayload;
        }
      }
      throw new Error(`Arquivo de projeto '${filePath}' não encontrado.`);
    }
  );
}

/**
 * Abre diálogo nativo do sistema operacional para selecionar uma pasta de filmagens.
 */
export async function selectFolderDialog(title?: string, defaultPath?: string): Promise<string | null> {
  return safeInvoke<string | null>(
    'select_folder_dialog',
    { title, defaultPath },
    () => {
      const prompted = window.prompt(title || 'Insira o caminho absoluto da pasta no seu computador:');
      return prompted && prompted.trim().length > 0 ? prompted.trim() : null;
    }
  );
}

/**
 * Abre diálogo nativo do sistema operacional para selecionar um arquivo.
 */
export async function selectFileDialog(
  title?: string,
  defaultPath?: string,
  filterName?: string,
  extensions?: string[]
): Promise<string | null> {
  return safeInvoke<string | null>(
    'select_file_dialog',
    { title, defaultPath, filterName, extensions },
    () => {
      const prompted = window.prompt(title || 'Insira o caminho do arquivo:');
      return prompted && prompted.trim().length > 0 ? prompted.trim() : null;
    }
  );
}

/**
 * Recupera o histórico de projetos recentes do arquivo local.
 */
export async function getRecentProjects(): Promise<RecentProjectItem[]> {
  return safeInvoke<RecentProjectItem[]>(
    'get_recent_projects',
    undefined,
    () => {
      if (typeof localStorage !== 'undefined') {
        const raw = localStorage.getItem('travelcut_recent_projects');
        if (raw) {
          try {
            return JSON.parse(raw);
          } catch {
            return [];
          }
        }
      }
      return [];
    }
  );
}

/**
 * Adiciona um projeto ao histórico de projetos recentes.
 */
export async function addRecentProject(project: RecentProjectItem): Promise<void> {
  return safeInvoke<void>(
    'add_recent_project',
    { project },
    () => {
      if (typeof localStorage !== 'undefined') {
        try {
          const raw = localStorage.getItem('travelcut_recent_projects');
          const list: RecentProjectItem[] = raw ? JSON.parse(raw) : [];
          const filtered = list.filter((p) => p.path !== project.path);
          filtered.unshift(project);
          localStorage.setItem('travelcut_recent_projects', JSON.stringify(filtered.slice(0, 20)));
        } catch {
          // ignore fallback error
        }
      }
    }
  );
}

/**
 * Remove um projeto do histórico de recentes.
 */
export async function removeRecentProject(projectPath: string): Promise<void> {
  return safeInvoke<void>(
    'remove_recent_project',
    { projectPath },
    () => {
      if (typeof localStorage !== 'undefined') {
        try {
          const raw = localStorage.getItem('travelcut_recent_projects');
          if (raw) {
            const list: RecentProjectItem[] = JSON.parse(raw);
            const filtered = list.filter((p) => p.path !== projectPath);
            localStorage.setItem('travelcut_recent_projects', JSON.stringify(filtered));
          }
        } catch {
          // ignore
        }
      }
    }
  );
}

// ============================================================================
// Media Engine Commands (Tauri Native + Web Fallbacks)
// ============================================================================

/**
 * Inicializa o motor de mídia nativo com a configuração desejada.
 */
export async function mediaInitialize(config?: Partial<MediaEngineConfig>): Promise<FfmpegCapabilities> {
  return safeInvoke<FfmpegCapabilities>(
    'media_initialize',
    { config },
    () => ({
      has_ffmpeg: false,
      has_ffprobe: false,
      ffmpeg_version: null,
      ffprobe_version: null,
      ffmpeg_path: null,
      ffprobe_path: null,
      hwaccels: [],
      encoders: [],
      decoders: [],
      demuxers: [],
      muxers: [],
      has_nvenc: false,
      has_qsv: false,
      has_amf: false,
      has_videotoolbox: false,
      has_cuda: false,
      has_d3d11va: false,
      has_dxva2: false,
    })
  );
}

/**
 * Retorna as capacidades detectadas do FFmpeg e aceleração de hardware.
 */
export async function mediaGetCapabilities(): Promise<FfmpegCapabilities> {
  return safeInvoke<FfmpegCapabilities>(
    'media_get_capabilities',
    undefined,
    () => ({
      has_ffmpeg: false,
      has_ffprobe: false,
      ffmpeg_version: null,
      ffprobe_version: null,
      ffmpeg_path: null,
      ffprobe_path: null,
      hwaccels: [],
      encoders: [],
      decoders: [],
      demuxers: [],
      muxers: [],
      has_nvenc: false,
      has_qsv: false,
      has_amf: false,
      has_videotoolbox: false,
      has_cuda: false,
      has_d3d11va: false,
      has_dxva2: false,
    })
  );
}

/**
 * Executa inspeção aprofundada de um arquivo de mídia usando o motor nativo.
 */
export async function mediaProbe(path: string): Promise<MediaMetadata> {
  return safeInvoke<MediaMetadata>(
    'media_probe',
    { path },
    () => {
      const fileName = path.split(/[/\\]/).pop() || 'media.mp4';
      return {
        file_path: path,
        media_type: 'video',
        format_name: 'mov,mp4',
        duration_secs: 10.0,
        duration_ms: 10000,
        file_size_bytes: 1024 * 1024 * 15,
        video: {
          codec: 'h264',
          profile: 'High',
          width: 1920,
          height: 1080,
          fps: 60.0,
          is_hdr: false,
        },
        video_streams: [],
        audio_streams: [],
        subtitle_streams: [],
        tags: { filename: fileName },
      };
    }
  );
}

/**
 * Varre um diretório em busca de arquivos de mídia usando o motor de inventário nativo.
 */
export async function mediaScanDirectory(
    path: string,
    options?: InventoryOptions
): Promise<DiscoveredFile[]> {
  return safeInvoke<DiscoveredFile[]>(
    'media_scan_directory',
    { path, options },
    () => []
  );
}

/**
 * Gera uma miniatura de um arquivo de mídia no tempo especificado.
 */
export async function mediaGenerateThumbnail(
    path: string,
    options?: ThumbnailOptions
): Promise<string> {
  return safeInvoke<string>(
    'media_generate_thumbnail',
    { path, options },
    () => ''
  );
}

/**
 * Gera uma sequência de miniaturas (filmstrip) para o scrubber da timeline.
 */
export async function mediaGenerateFilmstrip(
    path: string,
    options?: FilmstripOptions
): Promise<string[]> {
  return safeInvoke<string[]>(
    'media_generate_filmstrip',
    { path, options },
    () => []
  );
}

/**
 * Extrai a forma de onda (waveform) do áudio para exibição na timeline.
 */
export async function mediaExtractWaveform(
    path: string,
    options?: WaveformOptions
): Promise<WaveformData> {
  return safeInvoke<WaveformData>(
    'media_extract_waveform',
    { path, options },
    () => ({
      duration_secs: 0,
      channels: 2,
      peaks: [],
      points_per_second: 50,
      overall_rms: 0,
      peak_max: 0,
    })
  );
}

/**
 * Converte ou gera proxy para um arquivo de mídia.
 */
export async function mediaTranscode(
    options: TranscodeOptions
): Promise<TranscodeResult> {
  return safeInvoke<TranscodeResult>(
    'media_transcode',
    { options },
    () => ({
      output_path: options.output_path,
      file_size_bytes: 0,
      duration_secs: 0,
      elapsed_secs: 0,
      encoder_used: 'mock_encoder',
      hardware_accelerated: false,
      average_fps: 0,
    })
  );
}

/**
 * Cancela cooperativamente um job de processamento de mídia em andamento.
 */
export async function mediaCancelJob(jobId: string): Promise<boolean> {
  return safeInvoke<boolean>(
    'media_cancel_job',
    { jobId },
    () => true
  );
}

/**
 * Cancela todos os jobs de processamento de mídia em andamento.
 */
export async function mediaCancelAllJobs(): Promise<void> {
  return safeInvoke<void>(
    'media_cancel_all_jobs',
    undefined,
    () => {}
  );
}

/**
 * Consulta o status atual de um job de processamento de mídia.
 */
export async function mediaGetJobStatus(jobId: string): Promise<JobStatus | null> {
  return safeInvoke<JobStatus | null>(
    'media_get_job_status',
    { jobId },
    () => null
  );
}

// ============================================================================
// Model Mapping Utilities (Rust Engine <-> Frontend Domain)
// ============================================================================

/**
 * Converte um arquivo descoberto ou metadados do motor nativo para o modelo MediaAsset da UI.
 */
export function convertMetadataToMediaAsset(meta: MediaMetadata, thumbnailBase64OrUrl?: string): MediaAsset {
  const fileName = meta.file_path.split(/[/\\]/).pop() || 'media';
  const durationMs = meta.duration_ms || Math.round((meta.duration_secs || 0) * 1000);
  const width = meta.video?.width || meta.video_stream?.width;
  const height = meta.video?.height || meta.video_stream?.height;
  const resolution = width && height ? `${width}x${height}` : 'N/A';
  const frameRate = meta.video?.fps || meta.video_stream?.fps || 0;
  const recordedAt = meta.recorded_at || meta.modified_at || new Date().toISOString();
  const device = [meta.camera_make, meta.camera_model].filter(Boolean).join(' ') || 'Dispositivo Padrão';
  const mediaType = meta.media_type === 'audio' ? 'audio' : meta.media_type === 'image' ? 'image' : 'video';

  return {
    id: `asset_${Math.random().toString(36).slice(2, 9)}_${Date.now()}`,
    fileName,
    displayName: fileName.replace(/\.[^/.]+$/, ''),
    mediaType,
    sourcePath: meta.file_path,
    duration: durationMs,
    resolution,
    frameRate,
    recordedAt,
    device,
    thumbnail: thumbnailBase64OrUrl || '',
    processingState: 'ready',
    category: 'Conversa',
    codec: meta.video?.codec || meta.video_stream?.codec_name || meta.audio?.codec,
    bitrateMb: meta.overall_bitrate_bps ? Math.round(meta.overall_bitrate_bps / (1024 * 1024)) : undefined,
    colorProfile: meta.video?.color_space || undefined,
  };
}

/**
 * Converte um DiscoveredFile superficial para um MediaAsset preliminar enquanto aguarda análise profunda.
 */
export function convertDiscoveredFileToMediaAsset(file: DiscoveredFile): MediaAsset {
  const fileName = file.path.split(/[/\\]/).pop() || 'arquivo';
  const mediaType = file.media_type === 'audio' ? 'audio' : file.media_type === 'image' ? 'image' : 'video';

  return {
    id: `discovered_${Math.random().toString(36).slice(2, 9)}_${Date.now()}`,
    fileName,
    displayName: fileName.replace(/\.[^/.]+$/, ''),
    mediaType,
    sourcePath: file.path,
    duration: 0,
    resolution: 'Detectando...',
    frameRate: 0,
    recordedAt: file.modified_at || new Date().toISOString(),
    device: file.camera_profile ? file.camera_profile.toUpperCase() : 'Detectando...',
    thumbnail: '',
    processingState: 'raw',
    category: 'Outros',
  };
}

// ============================================================================
// Transcription Engine Commands (Whisper + Rust Native + Fallbacks)
// ============================================================================

export interface TranscriptionProgressPayload {
  percent: number;
  stage: string;
  message: string;
}

export interface TranscriptionWordResult {
  id: string;
  text: string;
  startSec: number;
  endSec: number;
  confidence: number;
  isCutCandidate?: boolean;
  cutReason?: string;
}

export interface TranscriptionSegmentResult {
  id: string;
  startSec: number;
  endSec: number;
  text: string;
  speaker?: string;
  words: TranscriptionWordResult[];
  isCutCandidate?: boolean;
  cutReason?: string;
}

export interface TranscriptionCutSuggestion {
  id: string;
  startSec: number;
  endSec: number;
  durationSec: number;
  reasonLabel: string;
  reasonCode: string;
  decisionState: 'pending' | 'accepted' | 'rejected';
}

export interface TranscriptionResult {
  mediaId: string;
  durationMs: number;
  durationSec: number;
  language: string;
  fullText: string;
  text?: string;
  segments?: TranscriptionSegmentResult[];
  transcription?: TranscriptionSegmentResult[];
  words?: TranscriptionWordResult[];
  silenceIntervals?: Array<{
    id: string;
    startSec: number;
    endSec: number;
    durationSec: number;
    category?: string;
  }>;
  cutSuggestions?: TranscriptionCutSuggestion[];
}

/**
 * Extrai o áudio de um vídeo padronizado para WAV 16kHz Mono 16-bit PCM para Whisper/VAD.
 */
export async function extractAudioForTranscription(
  videoPath: string,
  outputWavPath?: string
): Promise<string> {
  return safeInvoke<string>(
    'extract_audio_for_transcription',
    { videoPath, outputWavPath },
    () => {
      return videoPath.replace(/\.[^/.]+$/, '') + '_16k.wav';
    }
  );
}

/**
 * Executa a transcrição do áudio com o motor Whisper (via CLI nativo ou fallback inteligente).
 */
export async function runTranscription(
  audioPath: string,
  language = 'pt',
  modelSize = 'small'
): Promise<TranscriptionResult> {
  return safeInvoke<TranscriptionResult>(
    'run_transcription',
    { audioPath, language, modelSize },
    () => {
      const fileName = audioPath.split(/[/\\]/).pop() || 'audio';
      return {
        mediaId: fileName,
        durationMs: 12000,
        durationSec: 12.0,
        language,
        fullText: 'Olá pessoal, bem-vindos ao TravelCut Studio! Hoje vamos explorar as melhores cenas de viagem.',
        segments: [
          {
            id: 'seg_0001',
            startSec: 0.4,
            endSec: 4.2,
            text: 'Olá pessoal, bem-vindos ao TravelCut Studio!',
            speaker: 'Apresentador',
            words: [
              { id: 'w_0001', text: 'Olá', startSec: 0.4, endSec: 0.8, confidence: 0.98, isCutCandidate: false },
              { id: 'w_0002', text: 'pessoal,', startSec: 0.85, endSec: 1.35, confidence: 0.99, isCutCandidate: false },
              { id: 'w_0003', text: 'bem-vindos', startSec: 1.4, endSec: 2.1, confidence: 0.95, isCutCandidate: false },
              { id: 'w_0004', text: 'ao', startSec: 2.15, endSec: 2.35, confidence: 0.99, isCutCandidate: false },
              { id: 'w_0005', text: 'TravelCut', startSec: 2.4, endSec: 3.1, confidence: 0.97, isCutCandidate: false },
              { id: 'w_0006', text: 'Studio!', startSec: 3.15, endSec: 3.8, confidence: 0.96, isCutCandidate: false }
            ]
          },
          {
            id: 'seg_0002',
            startSec: 4.6,
            endSec: 9.4,
            text: 'Hoje vamos explorar as melhores cenas de viagem.',
            speaker: 'Apresentador',
            words: [
              { id: 'w_0007', text: 'Hoje', startSec: 4.6, endSec: 5.0, confidence: 0.99, isCutCandidate: false },
              { id: 'w_0008', text: 'vamos', startSec: 5.05, endSec: 5.4, confidence: 0.98, isCutCandidate: false },
              { id: 'w_0009', text: 'explorar', startSec: 5.45, endSec: 6.1, confidence: 0.99, isCutCandidate: false },
              { id: 'w_0010', text: 'as', startSec: 6.15, endSec: 6.3, confidence: 0.99, isCutCandidate: false },
              { id: 'w_0011', text: 'melhores', startSec: 6.35, endSec: 7.1, confidence: 0.97, isCutCandidate: false },
              { id: 'w_0012', text: 'cenas', startSec: 7.15, endSec: 7.8, confidence: 0.99, isCutCandidate: false },
              { id: 'w_0013', text: 'de', startSec: 7.85, endSec: 8.1, confidence: 0.99, isCutCandidate: false },
              { id: 'w_0014', text: 'viagem.', startSec: 8.15, endSec: 9.2, confidence: 0.98, isCutCandidate: false }
            ]
          }
        ],
        words: [
          { id: 'w_0001', text: 'Olá', startSec: 0.4, endSec: 0.8, confidence: 0.98, isCutCandidate: false },
          { id: 'w_0002', text: 'pessoal,', startSec: 0.85, endSec: 1.35, confidence: 0.99, isCutCandidate: false },
          { id: 'w_0003', text: 'bem-vindos', startSec: 1.4, endSec: 2.1, confidence: 0.95, isCutCandidate: false },
          { id: 'w_0004', text: 'ao', startSec: 2.15, endSec: 2.35, confidence: 0.99, isCutCandidate: false },
          { id: 'w_0005', text: 'TravelCut', startSec: 2.4, endSec: 3.1, confidence: 0.97, isCutCandidate: false },
          { id: 'w_0006', text: 'Studio!', startSec: 3.15, endSec: 3.8, confidence: 0.96, isCutCandidate: false },
          { id: 'w_0007', text: 'Hoje', startSec: 4.6, endSec: 5.0, confidence: 0.99, isCutCandidate: false },
          { id: 'w_0008', text: 'vamos', startSec: 5.05, endSec: 5.4, confidence: 0.98, isCutCandidate: false },
          { id: 'w_0009', text: 'explorar', startSec: 5.45, endSec: 6.1, confidence: 0.99, isCutCandidate: false },
          { id: 'w_0010', text: 'as', startSec: 6.15, endSec: 6.3, confidence: 0.99, isCutCandidate: false },
          { id: 'w_0011', text: 'melhores', startSec: 6.35, endSec: 7.1, confidence: 0.97, isCutCandidate: false },
          { id: 'w_0012', text: 'cenas', startSec: 7.15, endSec: 7.8, confidence: 0.99, isCutCandidate: false },
          { id: 'w_0013', text: 'de', startSec: 7.85, endSec: 8.1, confidence: 0.99, isCutCandidate: false },
          { id: 'w_0014', text: 'viagem.', startSec: 8.15, endSec: 9.2, confidence: 0.98, isCutCandidate: false }
        ]
      };
    }
  );
}

/**
 * Salva a transcrição gerada no projeto local (.travelcut/transcripts/{asset_id}.json).
 */
export async function saveTranscription(
  projectPath: string,
  assetId: string,
  transcript: unknown
): Promise<void> {
  return safeInvoke<void>(
    'save_transcription',
    { projectPath, assetId, transcript },
    () => {
      if (typeof localStorage !== 'undefined') {
        localStorage.setItem(`travelcut_transcript_${assetId}`, JSON.stringify(transcript));
      }
    }
  );
}

/**
 * Carrega a transcrição salva do projeto local.
 */
export async function loadTranscription(
  projectPath: string,
  assetId: string
): Promise<TranscriptionResult | null> {
  return safeInvoke<TranscriptionResult | null>(
    'load_transcription',
    { projectPath, assetId },
    () => {
      if (typeof localStorage !== 'undefined') {
        const raw = localStorage.getItem(`travelcut_transcript_${assetId}`);
        if (raw) {
          try {
            return JSON.parse(raw);
          } catch {
            return null;
          }
        }
      }
      return null;
    }
  );
}

/**
 * Escuta eventos em tempo real do progresso de transcrição do backend.
 */
export function onTranscriptionProgress(
  callback: (payload: TranscriptionProgressPayload) => void
): () => void {
  if (isTauriDesktop()) {
    let unlistenFn: (() => void) | null = null;
    listen<TranscriptionProgressPayload>('transcription-progress', (event) => {
      callback(event.payload);
    }).then((unlisten) => {
      unlistenFn = unlisten;
    }).catch((err) => {
      console.warn('[DesktopBridge] Falha ao registrar listener transcription-progress:', err);
    });

    return () => {
      if (unlistenFn) unlistenFn();
    };
  }

  return () => {};
}

/**
 * Detecta cortes de cena no vídeo usando mudança de quadros via FFmpeg.
 */
export async function detectSceneCuts(
  videoPath: string,
  threshold: number = 0.35
): Promise<number[]> {
  return safeInvoke<number[]>(
    'detect_scene_cuts',
    { videoPath, threshold },
    () => {
      // Fallback em ambiente browser / sandbox: simulação de cortes baseada em hash determinístico
      const hash = videoPath.split('').reduce((acc, char) => acc + char.charCodeAt(0), 0);
      const fakeCuts: number[] = [];
      const interval = 8 + (hash % 10);
      for (let t = interval; t < 60; t += interval) {
        fakeCuts.push(Number(t.toFixed(2)));
      }
      return fakeCuts;
    }
  );
}

/**
 * Motor de classificação audiovisual híbrida que cruza:
 * 1. Metadados de câmera (DJI -> drone, GoPro -> action/broll)
 * 2. Atividade de voz (VAD > 60% -> speech)
 * 3. Cortes de cena e ritmo visual (broll vs landscape)
 * 4. Assinatura sonora ou termos de deslocamento (road_trip)
 * 5. Preservação de sobrescrita manual do usuário (isUserOverridden)
 */
export function classifyMediaSegments(
  assetId: string,
  metadata: Partial<MediaMetadata>,
  vadSegments?: { startSec: number; endSec: number; hasSpeech: boolean }[],
  sceneCuts?: number[],
  existingSegments?: MediaSegment[]
): MediaSegment[] {
  const duration = metadata.duration_secs && metadata.duration_secs > 0
    ? metadata.duration_secs
    : (vadSegments && vadSegments.length > 0 ? vadSegments[vadSegments.length - 1].endSec : 30.0);

  const make = (metadata.camera?.make || '').toUpperCase();
  const model = (metadata.camera?.model || '').toUpperCase();
  const filename = (metadata.file_path || assetId).toLowerCase();

  const isDroneCamera =
    make.includes('DJI') ||
    model.includes('DJI') ||
    model.includes('MAVIC') ||
    model.includes('MINI') ||
    model.includes('AVATA') ||
    model.startsWith('FC');

  const isRoadTripContext =
    filename.includes('road') ||
    filename.includes('drive') ||
    filename.includes('estrada') ||
    filename.includes('carro') ||
    filename.includes('viagem') ||
    filename.includes('deslocamento');

  // Determinar pontos de corte temporais para os segmentos
  const splitPoints = new Set<number>([0, duration]);
  if (sceneCuts && sceneCuts.length > 0) {
    sceneCuts.forEach((cut) => {
      if (cut > 0.5 && cut < duration - 0.5) {
        splitPoints.add(Number(cut.toFixed(2)));
      }
    });
  }

  if (vadSegments && vadSegments.length > 0) {
    vadSegments.forEach((vad) => {
      if (vad.startSec > 0.5 && vad.startSec < duration - 0.5) {
        splitPoints.add(Number(vad.startSec.toFixed(2)));
      }
      if (vad.endSec > 0.5 && vad.endSec < duration - 0.5) {
        splitPoints.add(Number(vad.endSec.toFixed(2)));
      }
    });
  }

  // Se não houver cortes nem VAD, cria segmentação default
  if (splitPoints.size <= 2 && duration > 15) {
    const step = Math.min(15, duration / 2);
    for (let t = step; t < duration; t += step) {
      splitPoints.add(Number(t.toFixed(2)));
    }
  }

  const sortedPoints = Array.from(splitPoints).sort((a, b) => a - b);
  const segments: MediaSegment[] = [];

  for (let i = 0; i < sortedPoints.length - 1; i++) {
    const startSec = sortedPoints[i];
    const endSec = sortedPoints[i + 1];
    const segDuration = endSec - startSec;
    if (segDuration < 0.2) continue;

    const segId = `seg_${assetId}_${i + 1}`;

    // Verificar se existe sobrescrita prévia do usuário
    const prev = existingSegments?.find((s) =>
      Math.abs(s.startSec - startSec) < 0.5 && Math.abs(s.endSec - endSec) < 0.5
    );

    if (prev && prev.isUserOverridden) {
      segments.push({
        ...prev,
        id: segId,
        assetId,
        startSec,
        endSec,
      });
      continue;
    }

    // Calcular densidade de fala pelo VAD
    let speechDuration = 0;
    if (vadSegments && vadSegments.length > 0) {
      vadSegments.forEach((vad) => {
        if (!vad.hasSpeech) return;
        const overlapStart = Math.max(startSec, vad.startSec);
        const overlapEnd = Math.min(endSec, vad.endSec);
        if (overlapEnd > overlapStart) {
          speechDuration += (overlapEnd - overlapStart);
        }
      });
    }

    const speechDensity = segDuration > 0 ? speechDuration / segDuration : 0;

    let category: SegmentCategory = 'landscape';
    let confidence = 0.8;

    if (speechDensity >= 0.5) {
      category = 'speech';
      confidence = Math.min(0.99, 0.7 + speechDensity * 0.3);
    } else if (isDroneCamera) {
      category = 'drone';
      confidence = 0.95;
    } else if (isRoadTripContext) {
      category = 'road_trip';
      confidence = 0.88;
    } else if (segDuration <= 5.0) {
      category = 'broll';
      confidence = 0.82;
    } else {
      category = 'landscape';
      confidence = 0.85;
    }

    segments.push({
      id: segId,
      assetId,
      startSec,
      endSec,
      category,
      confidence: Number(confidence.toFixed(2)),
      isUserOverridden: false,
    });
  }

  return segments;
}

/**
 * Salva a segmentação classificada no disco (.travelcut/segments/{assetId}.json).
 */
export async function saveMediaSegments(
  projectPath: string,
  assetId: string,
  segments: MediaSegment[]
): Promise<void> {
  return safeInvoke<void>(
    'save_media_segments',
    { projectPath, assetId, segments },
    () => {
      if (typeof localStorage !== 'undefined') {
        localStorage.setItem(`travelcut_segments_${assetId}`, JSON.stringify(segments));
      }
    }
  );
}

/**
 * Carrega a segmentação classificada do disco (.travelcut/segments/{assetId}.json).
 */
export async function loadMediaSegments(
  projectPath: string,
  assetId: string
): Promise<MediaSegment[] | null> {
  return safeInvoke<MediaSegment[] | null>(
    'load_media_segments',
    { projectPath, assetId },
    () => {
      if (typeof localStorage !== 'undefined') {
        const raw = localStorage.getItem(`travelcut_segments_${assetId}`);
        if (raw) {
          try {
            return JSON.parse(raw);
          } catch {
            return null;
          }
        }
      }
      return null;
    }
  );
}


