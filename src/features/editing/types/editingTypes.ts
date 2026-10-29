/**
 * Unified Editing Workspace Types & Contracts
 * Phase 4.1 Integration - TravelCut Studio
 * Single Source of Truth for Media Library, Player, Transcription, Timeline & DaVinci Export
 */

export type MediaType = 'video' | 'audio' | 'image';

export type CutReason =
  | 'hesitation'
  | 'silence'
  | 'repetition'
  | 'unstable'
  | 'filler_word'
  | 'low_quality';

export type CutDecisionState = 'pending' | 'accepted' | 'rejected';

export interface UnifiedWord {
  id: string;
  text: string;
  startSec: number;
  endSec: number;
  confidence: number;
  isCutCandidate?: boolean;
  cutReason?: CutReason;
  cutId?: string;
  isCutAccepted?: boolean;
}

export interface UnifiedTranscriptSegment {
  id: string;
  mediaId: string;
  startSec: number;
  endSec: number;
  speaker: string;
  text: string;
  words: UnifiedWord[];
  emotion?: 'entusiasmo' | 'contemplativo' | 'curiosidade' | 'divertido' | 'neutro';
  isCutSegment?: boolean;
  cutReason?: CutReason;
}

export interface UnifiedCutSuggestion {
  id: string;
  mediaId: string;
  clipTitle: string;
  startSec: number;
  endSec: number;
  durationSec: number;
  reason: CutReason;
  reasonLabel: string;
  confidence: number; // 0.0 to 1.0
  decisionState: CutDecisionState;
  previewText: string;
  timecodeFormatted: string;
  appliedAtTimestamp?: string;
}

export interface UnifiedMediaAsset {
  id: string;
  fileName: string;
  displayName: string;
  mediaType: MediaType;
  sourcePath: string;
  durationSec: number;
  resolution: string;
  frameRate: number;
  recordedAt: string;
  device: string;
  thumbnail: string;
  category: 'A-Roll (Vlog)' | 'B-Roll (Paisagem)' | 'Ação / Trilha' | 'Drone Aéreo' | 'Depoimento';
  location: string;
  aiScore: number;
  aspectRatio: '16:9' | '9:16' | '4:3' | '1:1';
  codec: string;
  colorProfile: string;
  stability: 'Gimbal Suave' | 'Estabilizado IA' | 'Tremido Leve' | 'Tripé Fixo';
  lightingQuality: 'Perfeita' | 'Boa' | 'Subexposta' | 'Contraste Alto';
  waveformPoints: number[];
  transcription: UnifiedTranscriptSegment[];
  cutSuggestions: UnifiedCutSuggestion[];
}

export interface UnifiedTimelineClip {
  id: string;
  mediaClipId: string;
  title: string;
  trackIndex: number; // 0: V1 (Primary video), 1: V2 (B-roll overlay), 2: A1 (Main Audio), 3: A2 (Music/Ambience)
  timelineStartSec: number;
  sourceInSec: number;
  sourceOutSec: number;
  durationSec: number;
  colorGradeLut: string;
  transition: string;
  device: string;
  isCutSlice?: boolean;
  cutReason?: CutReason;
  isExcludedFromCut?: boolean;
}

export type ViewModeAB = 'after_cuts' | 'before_cuts' | 'split_ab';

export interface EditHistoryEntry {
  id: string;
  timestamp: string;
  actionType: 'accept_cut' | 'reject_cut' | 'reset_cuts' | 'seek' | 'select_clip';
  description: string;
  affectedCutId?: string;
  affectedClipId?: string;
}

export interface DaVinciExportData {
  projectName: string;
  fps: number;
  totalOriginalSec: number;
  totalEditedSec: number;
  savedSeconds: number;
  percentageSaved: number;
  cutsAppliedCount: number;
  cutsRejectedCount: number;
  clipsCount: number;
  xmlContent: string;
  edlContent: string;
  timelineClips: UnifiedTimelineClip[];
}
