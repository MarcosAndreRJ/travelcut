import type { MediaSegment } from '../../types/mediaEngine';

export type MediaFilterCategory =
  | 'Todos'
  | 'Conversas'
  | 'Paisagens'
  | 'Deslocamentos'
  | 'Drone'
  | 'Outros';

export type ViewMode = 'grid' | 'list';

export type InspectorTab = 'transcription' | 'analysis' | 'metadata' | 'thumbnails';

export type NavTab = 'library' | 'transcription' | 'narrative' | 'review' | 'export';

export interface TranscriptWord {
  id: string;
  text: string;
  startSec: number;
  endSec: number;
  confidence?: number;
  isCutCandidate?: boolean;
  cutReason?: string;
}

export interface TranscriptLine {
  id: string;
  timecode: string;
  seconds: number;
  endSeconds?: number;
  text: string;
  speaker?: string;
  isCutCandidate?: boolean;
  cutReason?: string;
  isAudioEvent?: boolean;
  accepted?: boolean;
  words?: TranscriptWord[];
}

export interface LibraryClip {
  id: string;
  filename: string;
  title: string;
  category: 'Conversa' | 'Deslocamento' | 'Drone' | 'Paisagem' | 'B-roll' | 'Outros';
  duration: string;
  durationSec: number;
  thumbnail: string;
  waveform: number[];
  resolution: string;
  camera: string;
  location: string;
  date: string;
  aiScore: number;
  transcript: TranscriptLine[];
  hasHesitationCut?: boolean;
  fileSize?: number;
  filePath?: string;
  fps?: number;
  segments?: MediaSegment[];
  isUserOverridden?: boolean;
}

export type QueueTaskStatus = 'completed' | 'in_progress' | 'pending' | 'failed';

export interface ProcessingQueueTask {
  id: string;
  step: number;
  name: string;
  description: string;
  progress: number; // 0-100
  status: QueueTaskStatus;
  statusText: string;
}

export interface ProjectSummaryData {
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

export interface ProjectHeaderData {
  title: string;
  path: string;
  activeStep: number;
  steps: {
    number: number;
    title: string;
    status: 'completed' | 'in_progress' | 'pending';
  }[];
}
