/**
 * TravelCut Studio - Media Engine Contracts & Types
 * Direct TypeScript bindings matching `crates/travelcut-media` and Tauri 2 commands.
 */

export type MediaType = 'video' | 'audio' | 'image' | 'unknown';
export type FileCategory = 'video' | 'audio' | 'image' | 'raw' | 'sidecar' | 'unknown';
export type CameraProfile = 'sony' | 'dji' | 'gopro' | 'apple' | 'canon' | 'fujifilm' | 'blackmagic' | 'generic';
export type MediaProcessingState = 'raw' | 'analyzing' | 'transcribing' | 'ready' | 'error';
export type StreamKind = 'video' | 'audio' | 'subtitle' | 'data' | 'attachment' | 'unknown';
export type ImageFormat = 'jpeg' | 'webp' | 'png';
export type HwAccelType = 'auto' | 'cuda' | 'nvenc' | 'qsv' | 'amf' | 'd3d11va' | 'dxva2' | 'videotoolbox' | 'none';
export type ProxyCodec = 'h264' | 'hevc' | 'proresproxy';
export type JobStatus = 'pending' | 'running' | 'completed' | 'failed' | 'cancelled';
export type JobType = 'probe' | 'thumbnail' | 'filmstrip' | 'proxy' | 'waveform' | 'transcode';

export interface FfmpegCapabilities {
  has_ffmpeg: boolean;
  has_ffprobe: boolean;
  ffmpeg_version?: string | null;
  ffprobe_version?: string | null;
  ffmpeg_path?: string | null;
  ffprobe_path?: string | null;
  hwaccels: string[];
  encoders: string[];
  decoders: string[];
  demuxers: string[];
  muxers: string[];
  has_nvenc: boolean;
  has_qsv: boolean;
  has_amf: boolean;
  has_videotoolbox: boolean;
  has_cuda: boolean;
  has_d3d11va: boolean;
  has_dxva2: boolean;
}

export interface ColorProfile {
  color_space?: string | null;
  color_transfer?: string | null;
  color_primaries?: string | null;
  is_hdr: boolean;
}

export interface VideoSummary {
  codec: string;
  profile: string;
  width: number;
  height: number;
  fps: number;
  bitrate_kbps?: number | null;
  color_space?: string | null;
  color_transfer?: string | null;
  color_primaries?: string | null;
  is_hdr: boolean;
}

export interface AudioSummary {
  codec: string;
  channels: number;
  sample_rate: number;
  bitrate_kbps?: number | null;
}

export interface CameraMetadata {
  make?: string | null;
  model?: string | null;
  software?: string | null;
  lens_model?: string | null;
}

export interface GpsMetadata {
  latitude: number;
  longitude: number;
  altitude?: number | null;
}

export interface VideoStreamInfo {
  index: number;
  codec_name: string;
  codec_long_name?: string | null;
  profile?: string | null;
  width: number;
  height: number;
  aspect_ratio: string;
  pixel_format: string;
  fps: number;
  frame_count?: number | null;
  bitrate_bps?: number | null;
  rotation: number;
  color_profile: ColorProfile;
}

export interface AudioStreamInfo {
  index: number;
  codec_name: string;
  codec_long_name?: string | null;
  sample_rate: number;
  channels: number;
  channel_layout?: string | null;
  bitrate_bps?: number | null;
  duration_secs?: number | null;
}

export interface SubtitleStreamInfo {
  index: number;
  codec_name: string;
  language?: string | null;
  title?: string | null;
}

export interface MediaMetadata {
  file_path: string;
  media_type: MediaType;
  format_name: string;
  format_long_name?: string | null;
  duration_secs: number;
  duration_ms: number;
  file_size_bytes: number;
  overall_bitrate_bps?: number | null;
  video_stream?: VideoStreamInfo | null;
  video?: VideoSummary | null;
  video_streams: VideoStreamInfo[];
  audio_stream?: AudioStreamInfo | null;
  audio?: AudioSummary | null;
  audio_streams: AudioStreamInfo[];
  subtitle_streams: SubtitleStreamInfo[];
  camera_make?: string | null;
  camera_model?: string | null;
  camera?: CameraMetadata | null;
  gps?: GpsMetadata | null;
  recorded_at?: string | null;
  modified_at?: string | null;
  tags: Record<string, string>;
}

export interface DiscoveredFile {
  path: string;
  file_size_bytes: number;
  size_bytes: number;
  modified_at?: string | null;
  media_type: MediaType;
  category: FileCategory;
  camera_profile?: CameraProfile | null;
  extension: string;
}

export interface InventoryOptions {
  recursive?: boolean;
  max_depth?: number | null;
  max_files?: number | null;
  include_types?: MediaType[] | null;
  extract_deep_metadata?: boolean;
  sort_by_date?: boolean;
}

export interface ThumbnailOptions {
  timestamp_secs?: number;
  width?: number;
  height?: number;
  fast_seek?: boolean;
  output_path?: string | null;
  time_offset_secs?: number | null;
  percentage?: number | null;
  format?: ImageFormat;
  smart_offset?: boolean;
  duration_secs?: number | null;
}

export interface FilmstripOptions {
  count?: number;
  width?: number;
  height?: number;
  output_dir?: string;
  format?: ImageFormat;
  duration_secs?: number | null;
}

export interface WaveformPeak {
  min: number;
  max: number;
  rms: number;
}

export interface WaveformData {
  duration_secs: number;
  channels: number;
  peaks: WaveformPeak[];
  points_per_second: number;
  overall_rms: number;
  peak_max: number;
}

export interface WaveformOptions {
  target_points?: number;
  channels?: number;
  points_per_second?: number;
}

export interface ProxyProfile {
  name: string;
  max_width: number;
  max_height: number;
  codec: ProxyCodec;
  crf: number;
  preset: string;
  audio_bitrate_kbps: number;
  hardware_accel: HwAccelType;
}

export interface TranscodeOptions {
  source_path: string;
  output_path: string;
  target_resolution?: [number, number] | null;
  video_codec?: string | null;
  hw_accel?: HwAccelType;
  crf?: number | null;
  video_bitrate_kbps?: number | null;
  audio_bitrate_kbps?: number | null;
  force_cfr?: boolean;
  target_fps?: number | null;
  profile?: ProxyProfile | null;
}

export interface TranscodeResult {
  output_path: string;
  file_size_bytes: number;
  duration_secs: number;
  elapsed_secs: number;
  encoder_used: string;
  hardware_accelerated: boolean;
  average_fps: number;
}

export interface MediaEngineConfig {
  ffmpeg_path?: string | null;
  ffprobe_path?: string | null;
  tools_dir?: string | null;
  cache_dir?: string;
  temp_dir?: string;
  max_concurrent_jobs?: number;
  process_timeout_secs?: number;
  timeout_secs?: number;
  hardware_accel?: HwAccelType;
  min_free_disk_space_bytes?: number;
  protect_originals?: boolean;
}

export type SegmentCategory = 'speech' | 'drone' | 'landscape' | 'road_trip' | 'broll';

export interface MediaSegment {
  id: string;
  assetId: string;
  startSec: number;
  endSec: number;
  category: SegmentCategory;
  confidence: number;
  isUserOverridden?: boolean;
  label?: string;
}

