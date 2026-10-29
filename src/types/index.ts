export type StudioTab =
  | 'analysis'    // 1. Análise & Transcrição (01_Painel_Inicial)
  | 'narrative'   // 2. Roteiro & Storyboard (02_Biblioteca_e_Transcricao)
  | 'audio'       // 3. Áudio & Música (03_Planejamento_Timeline)
  | 'export'      // 4. Exportação & Entrega (04_Revisao_de_Cortes)
  | 'settings'    // 5. Configurações & Motores (05_Configuracoes_e_Integracoes)
  | 'overview';   // 6. Montagem Geral NLE (06_Visao_Geral_da_Montagem)

export type ClipCategory = 'Todas' | 'Paisagem' | 'Pessoas' | 'Gastronomia' | 'Ação' | 'Monumentos' | 'Transporte';

export type QualityGrade = 'Excelente' | 'Boa' | 'Aceitável' | 'Instável';

export interface TranscriptSegment {
  id: string;
  start: number; // in seconds
  end: number;
  timecodeStart: string;
  timecodeEnd: string;
  speaker: string;
  text: string;
  highlightWords?: string[];
  emotion?: 'entusiasmo' | 'contemplativo' | 'curiosidade' | 'divertido' | 'neutro';
}

export interface SuggestedCut {
  id: string;
  startSec: number;
  endSec: number;
  timecodeStart: string;
  timecodeEnd: string;
  durationFormatted: string;
  score: number; // 0-100
  reason: string;
  tags: string[];
  accepted: boolean;
}

export interface MediaClip {
  id: string;
  title: string;
  filename: string;
  location: string;
  date: string;
  time: string;
  durationSec: number;
  durationFormatted: string;
  resolution: '4K 60fps' | '4K 24fps' | '1080p 120fps' | '1080p 60fps';
  fps: number;
  camera: string;
  category: ClipCategory;
  thumbnail: string;
  videoUrl?: string;
  quality: QualityGrade;
  stability: 'Gimbal Suave' | 'Estabilizado IA' | 'Tremido Leve' | 'Tripé Fixo';
  facesCount: number;
  aiScore: number;
  aiAnalysisSummary: string;
  suggestedCuts: SuggestedCut[];
  transcription: TranscriptSegment[];
  waveformPoints: number[];
}

export interface StoryboardScene {
  id: string;
  order: number;
  title: string;
  location: string;
  vibe: string;
  targetDurationSec: number;
  actualDurationSec: number;
  assignedClipIds: string[];
  musicMood: string;
  soundEffect: string;
  narrativeGoal: string;
}

export interface AudioTrackConfig {
  id: string;
  name: string;
  type: 'speech' | 'ambient' | 'music' | 'foley';
  volumeDb: number;
  isMuted: boolean;
  isSolo: boolean;
  speechEnhance: number; // 0-100%
  noiseReductionDb: number; // 0 to -30dB
  autoDucking: boolean;
  duckingAmountDb: number;
  eqLow: number;
  eqMid: number;
  eqHigh: number;
}

export interface MusicTrack {
  id: string;
  title: string;
  artist: string;
  bpm: number;
  genre: string;
  durationFormatted: string;
  durationSec: number;
  mood: string;
  beatTimesSec: number[];
  audioUrl?: string;
}

export interface ExportPreset {
  id: string;
  name: string;
  platform: 'YouTube' | 'Instagram' | 'TikTok' | 'Master ProRes' | 'Twitter/X' | 'DCP Cinema';
  ratio: '16:9' | '9:16' | '1:1' | '2.39:1';
  resolution: string;
  width: number;
  height: number;
  fps: number;
  codec: 'H.264' | 'H.265 / HEVC' | 'ProRes 422 HQ' | 'AV1';
  bitrateMbps: number;
  audioBitrateKbps: number;
  estimatedSizeMb: number;
  iconName: string;
}

export interface ExportJob {
  id: string;
  presetName: string;
  resolution: string;
  codec: string;
  progress: number;
  status: 'rendering' | 'completed' | 'queued' | 'paused';
  etaSec: number;
  outputFilename: string;
  filesizeMb: number;
}

export interface CutSegment {
  id: string;
  startSec: number;
  endSec: number;
  durationSec: number;
  description: string;
  score?: number;
}

export interface TimelineClip {
  id: string;
  mediaClipId: string;
  title: string;
  trackIndex: number;
  timelineStartSec: number;
  sourceInSec: number;
  sourceOutSec: number;
  durationSec: number;
  colorGradeLut?: string;
  transition?: string;
}

export interface SystemConfig {
  aiWhisperModel: 'Whisper Large-v3 (Local GPU)' | 'Whisper Medium' | 'Whisper OpenAI Cloud (Turbo)';
  aiVisionEngine: 'YOLOv8 + CLIP ViT-L/14' | 'MediaPipe Vision' | 'Gemini 1.5 Pro Flash Vision';
  hardwareAccel: 'NVIDIA CUDA (RTX 4090 / TensorRT)' | 'DirectML (Windows)' | 'Apple Metal Engine' | 'CPU Multi-Core AVX-512';
  renderScratchDisk: string;
  proxyGeneration: 'Automático ProRes Proxy' | 'Desativado' | 'DNxHR LB';
  xmlNleExport: 'DaVinci Resolve (.xml / .edl)' | 'Premiere Pro (.xml)' | 'Final Cut Pro (.fcpxml)';
  autoDuckingLevel: number;
  elevenLabsApiKeySet: boolean;
  autoDetectSilences: boolean;
}
