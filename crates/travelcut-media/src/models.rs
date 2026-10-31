//! Domain models and data structures for the TravelCut media engine.
//!
//! Designed to be completely decoupled from Tauri and frontend frameworks,
//! while providing structured data compatible with TravelCut Studio's contracts.

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use std::collections::{HashMap, HashSet};
use std::path::PathBuf;
use std::sync::atomic::AtomicBool;
use std::sync::Arc;

/// Unique identifier for media processing jobs.
pub type JobId = String;

/// High-level category of media assets.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize, Default)]
#[serde(rename_all = "lowercase")]
pub enum MediaType {
    Video,
    Audio,
    Image,
    #[default]
    Unknown,
}

/// Category of discovered file for travel workflows.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize, Default)]
#[serde(rename_all = "lowercase")]
pub enum FileCategory {
    Video,
    Audio,
    Image,
    Raw,
    Sidecar,
    #[default]
    Unknown,
}

/// Detected camera ecosystem / maker.
#[derive(Debug, Clone, PartialEq, Eq, Hash, Serialize, Deserialize, Default)]
#[serde(rename_all = "lowercase")]
pub enum CameraProfile {
    Sony,
    Dji,
    GoPro,
    Apple,
    Canon,
    Fujifilm,
    Blackmagic,
    #[default]
    Generic,
}

/// Processing state of an asset in the engine workflow.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
#[serde(rename_all = "lowercase")]
pub enum MediaProcessingState {
    #[default]
    Raw,
    Analyzing,
    Transcribing,
    Ready,
    Error,
}

/// Stream kind found inside media containers.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum StreamKind {
    Video,
    Audio,
    Subtitle,
    Data,
    Attachment,
    Unknown,
}

/// Color primaries and transfer curve (HDR/SDR characteristics).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize, Default)]
pub struct ColorProfile {
    pub color_space: Option<String>,
    pub color_transfer: Option<String>,
    pub color_primaries: Option<String>,
    pub is_hdr: bool,
}

/// Video summary view for UI and CLI.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
pub struct VideoSummary {
    pub codec: String,
    pub profile: String,
    pub width: u32,
    pub height: u32,
    pub fps: f64,
    pub bitrate_kbps: Option<u32>,
    pub color_space: Option<String>,
    pub color_transfer: Option<String>,
    pub color_primaries: Option<String>,
    pub is_hdr: bool,
}

/// Audio summary view for UI and CLI.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
pub struct AudioSummary {
    pub codec: String,
    pub channels: u16,
    pub sample_rate: u32,
    pub bitrate_kbps: Option<u32>,
}

/// Camera metadata extracted from tags or EXIF.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
pub struct CameraMetadata {
    pub make: Option<String>,
    pub model: Option<String>,
    pub software: Option<String>,
    pub lens_model: Option<String>,
}

/// GPS coordinates extracted from media metadata.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
pub struct GpsMetadata {
    pub latitude: f64,
    pub longitude: f64,
    pub altitude: Option<f64>,
}

/// Video stream technical specifications.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct VideoStreamInfo {
    pub index: usize,
    pub codec_name: String,
    pub codec_long_name: Option<String>,
    pub profile: Option<String>,
    pub width: u32,
    pub height: u32,
    pub aspect_ratio: String,
    pub pixel_format: String,
    pub fps: f64,
    pub frame_count: Option<u64>,
    pub bitrate_bps: Option<u64>,
    pub rotation: i32,
    pub color_profile: ColorProfile,
}

/// Audio stream technical specifications.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AudioStreamInfo {
    pub index: usize,
    pub codec_name: String,
    pub codec_long_name: Option<String>,
    pub sample_rate: u32,
    pub channels: u16,
    pub channel_layout: Option<String>,
    pub bitrate_bps: Option<u64>,
    pub duration_secs: Option<f64>,
}

/// Subtitle stream technical specifications.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SubtitleStreamInfo {
    pub index: usize,
    pub codec_name: String,
    pub language: Option<String>,
    pub title: Option<String>,
}

/// Comprehensive technical metadata extracted from a media file.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
pub struct MediaMetadata {
    /// File path on disk.
    pub file_path: PathBuf,
    /// Detected high-level media type.
    pub media_type: MediaType,
    /// Container format name (e.g. "mov,mp4,m4a,3gp,3g2,mj2").
    pub format_name: String,
    /// Container format human-readable name.
    pub format_long_name: Option<String>,
    /// Total duration in seconds.
    pub duration_secs: f64,
    /// Total duration in integer milliseconds (convenient for timeline).
    pub duration_ms: u64,
    /// File size in bytes.
    pub file_size_bytes: u64,
    /// Total stream bitrate in bits per second.
    pub overall_bitrate_bps: Option<u64>,
    /// Primary video stream info if video/image.
    pub video_stream: Option<VideoStreamInfo>,
    /// Video summary.
    pub video: Option<VideoSummary>,
    /// All video streams present in container.
    pub video_streams: Vec<VideoStreamInfo>,
    /// Primary audio stream info if audio present.
    pub audio_stream: Option<AudioStreamInfo>,
    /// Audio summary.
    pub audio: Option<AudioSummary>,
    /// All audio streams present in container.
    pub audio_streams: Vec<AudioStreamInfo>,
    /// Subtitle streams.
    pub subtitle_streams: Vec<SubtitleStreamInfo>,
    /// Camera make if found in metadata (e.g. "Sony", "DJI", "GoPro", "Apple").
    pub camera_make: Option<String>,
    /// Camera model if found in metadata (e.g. "ILCE-7SM3", "FC3582", "HERO12 Black").
    pub camera_model: Option<String>,
    /// Camera metadata summary.
    pub camera: Option<CameraMetadata>,
    /// GPS metadata if found in container.
    pub gps: Option<GpsMetadata>,
    /// Recording timestamp from EXIF/QuickTime tags if available.
    pub recorded_at: Option<DateTime<Utc>>,
    /// File last modified time.
    pub modified_at: Option<DateTime<Utc>>,
    /// Raw metadata tags extracted from container format and streams.
    pub tags: HashMap<String, String>,
}

impl MediaMetadata {
    /// Helper to format duration as standard timecode HH:MM:SS.mmm
    pub fn formatted_timecode(&self) -> String {
        let total_seconds = self.duration_secs.max(0.0);
        let hours = (total_seconds / 3600.0).floor() as u64;
        let minutes = ((total_seconds % 3600.0) / 60.0).floor() as u64;
        let seconds = (total_seconds % 60.0).floor() as u64;
        let millis = ((total_seconds.fract()) * 1000.0).round() as u64;
        format!("{:02}:{:02}:{:02}.{:03}", hours, minutes, seconds, millis)
    }

    /// Return primary resolution string e.g. "3840x2160" or "N/A".
    pub fn resolution_display(&self) -> String {
        if let Some(ref v) = self.video_stream {
            format!("{}x{}", v.width, v.height)
        } else if let Some(ref v) = self.video {
            format!("{}x{}", v.width, v.height)
        } else {
            "N/A".to_string()
        }
    }

    /// Return primary framerate or 0.0.
    pub fn primary_fps(&self) -> f64 {
        if let Some(ref v) = self.video_stream {
            v.fps
        } else if let Some(ref v) = self.video {
            v.fps
        } else {
            0.0
        }
    }

    /// Return primary video codec name or audio codec name.
    pub fn primary_codec(&self) -> String {
        if let Some(ref v) = self.video_stream {
            v.codec_name.clone()
        } else if let Some(ref v) = self.video {
            v.codec.clone()
        } else if let Some(ref a) = self.audio_stream {
            a.codec_name.clone()
        } else if let Some(ref a) = self.audio {
            a.codec.clone()
        } else {
            "unknown".to_string()
        }
    }
}

/// Preliminary representation of a media asset discovered on disk.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DiscoveredFile {
    pub path: PathBuf,
    pub file_size_bytes: u64,
    pub size_bytes: u64,
    pub modified_at: Option<DateTime<Utc>>,
    pub media_type: MediaType,
    pub category: FileCategory,
    pub camera_profile: Option<CameraProfile>,
    pub extension: String,
}

/// Options controlling directory traversal.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(default)]
pub struct InventoryOptions {
    /// Whether to scan recursively into subdirectories.
    pub recursive: bool,
    /// Maximum recursive directory depth (None = unlimited).
    pub max_depth: Option<usize>,
    /// Maximum number of media files to collect before terminating early.
    pub max_files: Option<usize>,
    /// Filter to only specific media types (Video, Audio, Image).
    pub include_types: Option<HashSet<MediaType>>,
    /// Extract deep metadata during scanning using ffprobe.
    pub extract_deep_metadata: bool,
    /// Cooperative cancellation flag.
    #[serde(skip)]
    pub cancel_flag: Option<Arc<AtomicBool>>,
    /// Whether to sort the resulting list by modification time (chronological).
    pub sort_by_date: bool,
}

impl Default for InventoryOptions {
    fn default() -> Self {
        Self {
            recursive: true,
            max_depth: None,
            max_files: None,
            include_types: None,
            extract_deep_metadata: false,
            cancel_flag: None,
            sort_by_date: true,
        }
    }
}

/// Progress emitted during directory inventory scanning.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct InventoryProgress {
    pub files_scanned: u64,
    pub media_files_found: u64,
    pub current_path: PathBuf,
    pub is_finished: bool,
}

/// Image formats for thumbnail generation.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
#[serde(rename_all = "lowercase")]
pub enum ImageFormat {
    #[default]
    Jpeg,
    #[serde(alias = "webp", alias = "WebP")]
    Webp,
    Png,
}

impl ImageFormat {
    #[allow(non_upper_case_globals)]
    pub const WebP: ImageFormat = ImageFormat::Webp;

    pub fn extension(&self) -> &'static str {
        match self {
            Self::Jpeg => "jpg",
            Self::Webp => "webp",
            Self::Png => "png",
        }
    }
}

/// Configuration options for thumbnail extraction.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(default)]
pub struct ThumbnailOptions {
    /// Timestamp position in seconds.
    pub timestamp_secs: f64,
    /// Target width in pixels.
    pub width: u32,
    /// Target height in pixels.
    pub height: u32,
    /// Use fast input seeking.
    pub fast_seek: bool,
    /// Optional explicit output path.
    pub output_path: Option<PathBuf>,
    /// Time offset in seconds (alternative to timestamp_secs).
    pub time_offset_secs: Option<f64>,
    /// Percentage offset (0.0 to 100.0).
    pub percentage: Option<f32>,
    /// Image format for thumbnail.
    pub format: ImageFormat,
    /// Smart offset to avoid black frames.
    pub smart_offset: bool,
    /// Known video duration in seconds.
    pub duration_secs: Option<f64>,
}

impl Default for ThumbnailOptions {
    fn default() -> Self {
        Self {
            timestamp_secs: 1.0,
            width: 320,
            height: 180,
            fast_seek: true,
            output_path: None,
            time_offset_secs: None,
            percentage: None,
            format: ImageFormat::Jpeg,
            smart_offset: false,
            duration_secs: None,
        }
    }
}

/// Configuration options for generating filmstrip scrubber sequences.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(default)]
pub struct FilmstripOptions {
    /// Number of thumbnail frames to sample across the video duration.
    pub count: usize,
    /// Width of each frame thumbnail.
    pub width: u32,
    /// Height of each frame thumbnail.
    pub height: u32,
    /// Directory where generated frame images are stored.
    pub output_dir: PathBuf,
    /// Image format for filmstrip frames.
    pub format: ImageFormat,
    /// Known video duration in seconds.
    pub duration_secs: Option<f64>,
}

impl Default for FilmstripOptions {
    fn default() -> Self {
        Self {
            count: 10,
            width: 160,
            height: 90,
            output_dir: PathBuf::from("./filmstrip"),
            format: ImageFormat::Jpeg,
            duration_secs: None,
        }
    }
}

/// Single min/max/RMS audio peak point for waveform rendering.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize, Default)]
pub struct WaveformPeak {
    pub min: f32,
    pub max: f32,
    pub rms: f32,
}

/// Extracted waveform dataset for a media file.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
pub struct WaveformData {
    pub duration_secs: f64,
    pub channels: u16,
    pub peaks: Vec<WaveformPeak>,
    pub points_per_second: f64,
    pub overall_rms: f32,
    pub peak_max: f32,
}

/// Options for audio waveform extraction.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(default)]
pub struct WaveformOptions {
    /// Desired number of data points across the file duration.
    pub target_points: usize,
    /// Number of audio channels to mix/downsample.
    pub channels: u16,
    /// Desired points per second (default: 50).
    pub points_per_second: u32,
}

impl Default for WaveformOptions {
    fn default() -> Self {
        Self {
            target_points: 1000,
            channels: 2,
            points_per_second: 50,
        }
    }
}

/// Hardware acceleration choices for video transcoding.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
#[serde(rename_all = "lowercase")]
pub enum HwAccelType {
    #[default]
    Auto,
    Cuda,
    Nvenc,
    Qsv,
    Amf,
    D3d11va,
    Dxva2,
    VideoToolbox,
    None,
}

pub type HardwareAccel = HwAccelType;

/// Supported proxy video codecs.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum ProxyCodec {
    H264,
    Hevc,
    ProResProxy,
}

/// Profile specifying parameters for proxy video generation.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ProxyProfile {
    pub name: String,
    pub max_width: u32,
    pub max_height: u32,
    pub codec: ProxyCodec,
    pub crf: u8,
    pub preset: String,
    pub audio_bitrate_kbps: u32,
    pub hardware_accel: HwAccelType,
}

impl ProxyProfile {
    pub fn standard_720p() -> Self {
        Self {
            name: "standard_720p".to_string(),
            max_width: 1280,
            max_height: 720,
            codec: ProxyCodec::H264,
            crf: 23,
            preset: "veryfast".to_string(),
            audio_bitrate_kbps: 128,
            hardware_accel: HwAccelType::Auto,
        }
    }

    pub fn light_540p() -> Self {
        Self {
            name: "light_540p".to_string(),
            max_width: 960,
            max_height: 540,
            codec: ProxyCodec::H264,
            crf: 26,
            preset: "ultrafast".to_string(),
            audio_bitrate_kbps: 96,
            hardware_accel: HwAccelType::Auto,
        }
    }

    pub fn high_1080p() -> Self {
        Self {
            name: "high_1080p".to_string(),
            max_width: 1920,
            max_height: 1080,
            codec: ProxyCodec::H264,
            crf: 21,
            preset: "fast".to_string(),
            audio_bitrate_kbps: 160,
            hardware_accel: HwAccelType::Auto,
        }
    }
}

/// Options controlling media transcoding or proxy generation.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TranscodeOptions {
    pub source_path: PathBuf,
    pub output_path: PathBuf,
    pub target_resolution: Option<(u32, u32)>,
    pub video_codec: Option<String>,
    pub hw_accel: HwAccelType,
    pub crf: Option<u8>,
    pub video_bitrate_kbps: Option<u32>,
    pub audio_bitrate_kbps: Option<u32>,
    pub force_cfr: bool,
    pub target_fps: Option<f64>,
    pub profile: Option<ProxyProfile>,
}

/// Result metadata produced upon transcode completion.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TranscodeResult {
    pub output_path: PathBuf,
    pub file_size_bytes: u64,
    pub duration_secs: f64,
    pub elapsed_secs: f64,
    pub encoder_used: String,
    pub hardware_accelerated: bool,
    pub average_fps: f64,
}

/// Output audio format options for extraction.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum AudioOutputFormat {
    Wav,
    Aac,
    Mp3,
    Flac,
}

impl AudioOutputFormat {
    pub fn extension(&self) -> &'static str {
        match self {
            Self::Wav => "wav",
            Self::Aac => "aac",
            Self::Mp3 => "mp3",
            Self::Flac => "flac",
        }
    }
}

/// Configuration for audio extraction.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AudioExtractionOptions {
    pub format: AudioOutputFormat,
    pub sample_rate: u32,
    pub channels: u16,
    pub stream_index: Option<usize>,
    pub normalize_loudness: bool,
}

impl AudioExtractionOptions {
    pub fn whisper_compatible() -> Self {
        Self {
            format: AudioOutputFormat::Wav,
            sample_rate: 16000,
            channels: 1,
            stream_index: None,
            normalize_loudness: false,
        }
    }

    pub fn high_fidelity() -> Self {
        Self {
            format: AudioOutputFormat::Wav,
            sample_rate: 48000,
            channels: 2,
            stream_index: None,
            normalize_loudness: false,
        }
    }
}

/// Priority tier for queued jobs.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash, Serialize, Deserialize, Default)]
#[serde(rename_all = "lowercase")]
pub enum JobPriority {
    Low = 0,
    #[default]
    Normal = 1,
    High = 2,
    Critical = 3,
}

/// Job status lifecycle.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum JobStatus {
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// Type of media engine task.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum JobType {
    Probe,
    Thumbnail,
    Filmstrip,
    Proxy,
    Transcode,
    Waveform,
    AudioExtraction,
    InventoryScan,
}

impl std::fmt::Display for JobType {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            JobType::Probe => write!(f, "probe"),
            JobType::Thumbnail => write!(f, "thumbnail"),
            JobType::Filmstrip => write!(f, "filmstrip"),
            JobType::Proxy => write!(f, "proxy"),
            JobType::Transcode => write!(f, "transcode"),
            JobType::Waveform => write!(f, "waveform"),
            JobType::AudioExtraction => write!(f, "audio_extraction"),
            JobType::InventoryScan => write!(f, "inventory_scan"),
        }
    }
}

/// Real-time progress update emitted by running jobs.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
pub struct JobProgress {
    pub job_id: String,
    pub job_type: Option<JobType>,
    pub status: Option<JobStatus>,
    /// Progress percentage from 0.0 to 100.0.
    pub percentage: f32,
    /// Descriptive activity message.
    pub message: String,
    /// Current processed frame count if video encoding.
    pub current_frame: Option<u64>,
    pub frames_processed: Option<u64>,
    /// Total frames if known.
    pub total_frames: Option<u64>,
    /// Current media time processed in seconds.
    pub current_time_secs: Option<f64>,
    /// Total duration in seconds.
    pub total_duration_secs: Option<f64>,
    /// Encoding speed or FPS.
    pub current_fps: Option<f64>,
    pub speed_fps: Option<f64>,
    /// Elapsed seconds.
    pub elapsed_secs: f64,
    /// Estimated time remaining in seconds.
    pub eta_secs: Option<f64>,
}

/// Lifecycle events emitted by the job manager.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "event", content = "data", rename_all = "snake_case")]
pub enum JobEvent {
    Queued {
        job_id: JobId,
        job_type: JobType,
        priority: JobPriority,
    },
    Started {
        job_id: JobId,
        job_type: JobType,
    },
    Progress(JobProgress),
    Completed {
        job_id: JobId,
        job_type: JobType,
        duration_secs: f64,
    },
    Failed {
        job_id: JobId,
        job_type: JobType,
        error: String,
    },
    Cancelled {
        job_id: JobId,
    },
}
