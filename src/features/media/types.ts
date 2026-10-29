/**
 * Phase 3 & 4 Media and Transcription Types and Contracts
 * Spec: TravelCut Studio Phase 3 (Biblioteca de Mídias e Transcrição)
 * Integration Target: Phase 4 (integration/phase-04-1)
 */

// ==========================================
// Phase 4 Integration Contracts
// ==========================================

export type MediaType = 'video' | 'audio' | 'image';

export type MediaProcessingState = 'raw' | 'analyzing' | 'transcribing' | 'ready' | 'error';

export interface MediaAsset {
  id: string;
  fileName: string;
  displayName: string;
  mediaType: MediaType;
  sourcePath: string;
  duration: number; // Integer milliseconds (visual contract: [start, end) in ms)
  resolution: string; // e.g. '3840x2160', '1920x1080', '4K 60fps'
  frameRate: number; // e.g. 24, 30, 60, 120
  recordedAt: string; // ISO 8601 string
  device: string; // e.g. 'iPhone 15 Pro Max', 'Sony FX3', 'GoPro Hero 12 Black'
  thumbnail: string;
  processingState: MediaProcessingState;

  // Rich metadata for Phase 3 UI & analysis
  category: MediaCategory;
  location?: string;
  aiScore?: number;
  waveformPoints?: number[];
  videoUrl?: string;
  aspectRatio?: '16:9' | '9:16' | '4:3' | '1:1';
  codec?: string;
  bitrateMb?: number;
  audioChannels?: string;
  colorProfile?: string;
  facesCount?: number;
  stability?: 'Gimbal Suave' | 'Estabilizado IA' | 'Tremido Leve' | 'Tripé Fixo';
  lightingQuality?: 'Perfeita' | 'Boa' | 'Subexposta' | 'Contraste Alto';
}

export interface TranscriptWord {
  id: string;
  text: string;
  startTime: number; // Integer milliseconds (inclusive)
  endTime: number; // Integer milliseconds (exclusive)
  segmentId: string;
  mediaId: string;
  confidence?: number; // 0.0 to 1.0
  isCutCandidate?: boolean;
  cutReason?: string;
}

export interface TranscriptSegment {
  id: string;
  mediaId: string;
  startTime: number; // Integer milliseconds (inclusive)
  endTime: number; // Integer milliseconds (exclusive)
  text: string;
  speakerId?: string;
  words: TranscriptWord[];
  isAudioEvent?: boolean;
  cutSuggestionId?: string;
  emotion?: 'entusiasmo' | 'contemplativo' | 'curiosidade' | 'divertido' | 'neutro';
}

export type CutReason =
  | 'hesitation'
  | 'silence'
  | 'repetition'
  | 'unstable'
  | 'filler_word'
  | 'low_quality';

export type CutDecisionState = 'pending' | 'accepted' | 'rejected';

export interface CutSuggestion {
  id: string;
  mediaId: string;
  sourceRange: {
    start: number; // Integer ms, inclusive
    end: number; // Integer ms, exclusive
  };
  reason: CutReason;
  confidence: number; // 0.0 to 1.0
  decisionState: CutDecisionState;
  displayText?: string;
  originalText?: string;
}

// ==========================================
// Typed Event Contracts (Phase 4 Communication)
// ==========================================

export interface MediaSelectEvent {
  type: 'media.select';
  payload: {
    mediaId: string;
    asset: MediaAsset;
  };
}

export interface MediaSeekEvent {
  type: 'media.seek';
  payload: {
    mediaId: string;
    timeMs: number;
    formattedTime: string;
  };
}

export interface TranscriptWordSelectEvent {
  type: 'transcript.word.select';
  payload: {
    wordId: string;
    word: TranscriptWord;
    mediaId: string;
    seekTimeMs: number;
  };
}

export interface TranscriptRangeSelectEvent {
  type: 'transcript.range.select';
  payload: {
    mediaId: string;
    startMs: number;
    endMs: number;
    text: string;
    words: TranscriptWord[];
  };
}

export interface TranscriptCutRequestedEvent {
  type: 'transcript.cut.requested';
  payload: {
    mediaId: string;
    sourceRange: {
      start: number;
      end: number;
    };
    reason: string;
    cutSuggestionId?: string;
    suggestedText?: string;
  };
}

export type MediaFeatureEvent =
  | MediaSelectEvent
  | MediaSeekEvent
  | TranscriptWordSelectEvent
  | TranscriptRangeSelectEvent
  | TranscriptCutRequestedEvent;

export interface MediaEventHandlers {
  onMediaSelect?: (event: MediaSelectEvent) => void;
  onMediaSeek?: (event: MediaSeekEvent) => void;
  onWordSelect?: (event: TranscriptWordSelectEvent) => void;
  onRangeSelect?: (event: TranscriptRangeSelectEvent) => void;
  onCutRequested?: (event: TranscriptCutRequestedEvent) => void;
}

// ==========================================
// UI & Workspace State Types
// ==========================================

export type MediaCategory =
  | 'Conversa'
  | 'Paisagem'
  | 'Deslocamento'
  | 'Drone'
  | 'B-roll'
  | 'Outros';

export type MediaFilterTab =
  | 'Todos'
  | 'Conversas'
  | 'Paisagens'
  | 'Deslocamentos'
  | 'Drone'
  | 'Outros';

export type MediaViewMode = 'grid' | 'list';

export type InspectorTab = 'transcription' | 'analysis' | 'metadata' | 'thumbnails';

export interface ProcessingQueueTask {
  id: string;
  step: number;
  name: string;
  description: string;
  progress: number; // 0-100
  status: 'completed' | 'in_progress' | 'pending' | 'failed';
  statusText: string;
}

export interface ProjectSummaryStats {
  estimatedDuration: string;
  selectedClipsCount: number;
  totalClipsCount: number;
  autoCutsCount: number;
  reviewCutsCount: number;
  suggestedAssets: {
    music: number;
    videos: number;
    images: number;
  };
  daVinciProjectStatus: string;
  lastUpdate: string;
}

export interface WorkflowStep {
  number: number;
  title: string;
  status: 'completed' | 'in_progress' | 'pending';
}

export interface ProjectHeaderConfig {
  title: string;
  path: string;
  activeStep: number;
  steps: WorkflowStep[];
}

export interface KPICardData {
  id: string;
  value: string | number;
  label: string;
  subtitle: string;
  iconName: 'video' | 'image' | 'messageSquare' | 'scissors';
  themeColor: 'blue' | 'green' | 'cyan' | 'purple';
}

// ==========================================
// Time & Millisecond Utilities
// ==========================================

/**
 * Format milliseconds into standard timecode format (HH:MM:SS or MM:SS)
 */
export function formatMillisecondsToTimecode(ms: number, includeHours = false): string {
  const totalSeconds = Math.max(0, Math.floor(ms / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  if (includeHours || hours > 0) {
    return `${hours.toString().padStart(2, '0')}:${minutes
      .toString()
      .padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
  }
  return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
}

/**
 * Format seconds into HH:MM:SS timecode
 */
export function formatSecondsToTimecode(sec: number): string {
  return formatMillisecondsToTimecode(sec * 1000, true);
}

/**
 * Parse timecode string "HH:MM:SS" or "MM:SS" to milliseconds
 */
export function parseTimecodeToMilliseconds(timecode: string): number {
  const parts = timecode.split(':').map((p) => parseInt(p, 10));
  if (parts.length === 3) {
    const [h, m, s] = parts;
    return ((h * 3600) + (m * 60) + s) * 1000;
  }
  if (parts.length === 2) {
    const [m, s] = parts;
    return ((m * 60) + s) * 1000;
  }
  return 0;
}
