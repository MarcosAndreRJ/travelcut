# Especificacao de Contratos de Dados e Integracao

Este documento define formalmente os contratos de dados compartilhados entre a interface web desktop (**TypeScript**), o motor de processamento nativo (**Rust**) e o subsistema de reconhecimento vocal e IA (**Python**).

A padronizacao rigorosa desses modelos assegura interoperabilidade entre processos, seguranca de tipos e desacoplamento entre os modulos.

---

## 1. Modelo de Midia e Metadados (`MediaAsset` / `MediaMetadata`)

Representa o ativo audiovisual inspecionado e disponibilizado para edicao.

### 1.1 Contrato TypeScript (`src/features/media/types.ts`)
```typescript
export interface MediaAsset {
  id: string;
  name: string;
  path: string;
  durationSec: number;
  durationFormatted: string; // Ex: "02:45"
  fileSizeBytes: number;
  fileSizeFormatted: string; // Ex: "1.4 GB"
  codec: string;             // Ex: "h264", "hevc", "prores"
  resolution: string;        // Ex: "3840x2160", "1920x1080"
  fps: number;               // Ex: 23.976, 29.97, 59.94
  aspectRatio: '16:9' | '9:16' | '4:3' | '1:1';
  cameraModel?: string;      // Ex: "Sony FX3", "DJI Mini 4 Pro"
  cameraMake?: string;       // Ex: "Sony", "DJI", "GoPro"
  recordedAt?: string;       // ISO 8601
  thumbnailUrl?: string;     // Caminho local para imagem gerada
  audioChannels: number;     // Ex: 2
  sampleRateHz: number;      // Ex: 48000
  status: 'ready' | 'processing' | 'error' | 'unprocessed';
  processingProgress?: number; // 0 a 100
  hasAudio: boolean;
  transcriptionStatus: 'none' | 'transcribing' | 'completed' | 'failed';
  language?: string;         // Ex: "pt", "en"
  confidenceScore?: number;  // 0.0 a 1.0
}
```

### 1.2 Contrato Rust (`crates/travelcut-media/src/models.rs`)
```rust
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MediaMetadata {
    pub file_path: String,
    pub format_name: String,
    pub duration_seconds: f64,
    pub size_bytes: u64,
    pub bit_rate: Option<u64>,
    pub video_streams: Vec<VideoStreamInfo>,
    pub audio_streams: Vec<AudioStreamInfo>,
    pub creation_time: Option<String>,
    pub rotation: Option<i32>,
    pub camera_make: Option<String>,
    pub camera_model: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct VideoStreamInfo {
    pub index: u32,
    pub codec_name: String,
    pub width: u32,
    pub height: u32,
    pub frame_rate: f64,
    pub aspect_ratio: String,
    pub color_space: Option<String>,
    pub is_hdr: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AudioStreamInfo {
    pub index: u32,
    pub codec_name: String,
    pub channels: u32,
    pub sample_rate: u32,
    pub bit_rate: Option<u64>,
}
```

---

## 2. Modelo de Transcricao e Alinhamento de Palavras

Permite a representacao temporal de fala em niveis de granularidade progressivos: documento, segmentos (frases) e palavras individuais.

### 2.1 Contrato Python (`research/transcription/contracts.py`)
```python
@dataclass
class TranscriptWord:
    id: str
    word: str
    start_sec: float
    end_sec: float
    confidence: float
    speaker_tag: Optional[str] = None
    is_hesitation: bool = False
    is_silence: bool = False

@dataclass
class TranscriptSegment:
    id: str
    text: str
    start_sec: float
    end_sec: float
    confidence: float
    words: List[TranscriptWord]
    speaker_tag: Optional[str] = None

@dataclass
class TranscriptionDocument:
    media_id: str
    language: str
    duration_sec: float
    segments: List[TranscriptSegment]
    words: List[TranscriptWord]
    engine: str
    model_name: str
    processing_time_sec: float
    real_time_factor: float
```

### 2.2 Contrato TypeScript (`src/features/media/types.ts`)
```typescript
export interface TranscriptWord {
  id: string;
  word: string;
  startSec: number;
  endSec: number;
  confidence: number;
  speakerTag?: string;
  isHesitation?: boolean;
  isSilence?: boolean;
}

export interface TranscriptSegment {
  id: string;
  text: string;
  startSec: number;
  endSec: number;
  confidence: number;
  words: TranscriptWord[];
  speakerTag?: string;
}
```

---

## 3. Modelo de Sugestoes de Corte IA (`CutSuggestion`)

Representa as oportunidades de corte identificadas automaticamente pelos algoritmos de VAD, classificacao de pausas e analise de hesitacoes.

### 3.1 Contrato TypeScript (`src/features/media/types.ts` & `src/features/editing/types/index.ts`)
```typescript
export type CutType =
  | 'hesitation'         // Sons vocais involuntarios ("eh", "hum", "tipo assim")
  | 'silence'            // Silencios mortos removiveis (>= 1200ms)
  | 'redundancy'         // Ideia repetida ou take reiniciado
  | 'bad_take'           // Falsa largada ou erro de pronunciacao
  | 'technical_glitch';  // Ruido estourado de microfone ou tremor excessivo

export type CutStatus =
  | 'suggested'          // Proposta gerada pela IA, aguardando decisao
  | 'accepted'           // Corte aprovado pelo editor (sera removido na exportacao)
  | 'rejected'           // Corte descartado pelo editor (trecho sera mantido)
  | 'applied';           // Corte consolidado na timeline final

export interface CutSuggestion {
  id: string;
  assetId: string;
  type: CutType;
  label: string;
  reason: string;
  startSec: number;
  endSec: number;
  durationSec: number;
  confidence: number;    // 0.0 a 1.0
  status: CutStatus;
}
```

---

## 4. Modelos Auxiliares do Engine Rust

### 4.1 Waveform de Audio (`WaveformData`)
```rust
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct WaveformData {
    pub media_path: String,
    pub sample_rate: u32,
    pub duration_seconds: f64,
    pub points_per_second: u32,
    pub min_peaks: Vec<f32>, // Valores normalizados entre -1.0 e 0.0
    pub max_peaks: Vec<f32>, // Valores normalizados entre 0.0 e 1.0
}
```

### 4.2 Requisicao de Thumbnails e Filmstrips (`ThumbnailOptions`)
```rust
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ThumbnailOptions {
    pub time_offset_seconds: f64,
    pub target_width: u32,
    pub target_height: u32,
    pub quality: u32,        // 1 a 100
    pub is_filmstrip: bool,
    pub filmstrip_interval_seconds: Option<f64>,
}
```

### 4.3 Perfil de Transcodificacao Proxy (`TranscodeProfile`)
```rust
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TranscodeProfile {
    pub name: String,        // Ex: "Proxy 720p H.264", "Proxy 1080p ProRes"
    pub target_width: u32,
    pub target_height: u32,
    pub video_codec: String, // Ex: "h264_nvenc", "h264_videotoolbox", "libx264"
    pub audio_codec: String, // Ex: "aac", "pcm_s16le"
    pub crf: Option<u32>,
    pub bitrate_kbps: Option<u32>,
}
```
