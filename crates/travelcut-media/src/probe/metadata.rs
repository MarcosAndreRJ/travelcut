//! High-level FFprobe metadata extraction engine.
//!
//! Executes FFprobe with JSON output, parses video, audio, and subtitle streams,
//! extracts camera maker/model, color spaces, and recording timestamps.

use crate::error::{MediaEngineError, Result};
use crate::ffmpeg::process::{normalize_windows_path, SafeProcessRunner};
use crate::models::{
    AudioSummary, CameraMetadata, GpsMetadata, MediaMetadata, MediaType, VideoSummary,
};
use crate::probe::raw_types::FfprobeOutput;
use crate::probe::stream::{
    parse_audio_stream, parse_subtitle_stream, parse_video_stream,
};
use chrono::{DateTime, Utc};
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use tracing::debug;

/// High-level FFprobe inspector.
#[derive(Debug, Clone)]
pub struct ProbeEngine {
    ffprobe_path: PathBuf,
    timeout_secs: u64,
}

impl ProbeEngine {
    /// Create a new probe engine with a verified ffprobe binary.
    pub fn new(ffprobe_path: PathBuf, timeout_secs: u64) -> Self {
        Self {
            ffprobe_path,
            timeout_secs: timeout_secs.max(5),
        }
    }

    /// Inspect a media file on disk and return rich, typed technical metadata.
    pub async fn probe_file(&self, path: &Path) -> Result<MediaMetadata> {
        if !path.exists() {
            return Err(MediaEngineError::InvalidPath {
                path: path.to_path_buf(),
                reason: "Arquivo de mídia não encontrado no disco".to_string(),
            });
        }

        let normalized_path = normalize_windows_path(path);
        let path_str = normalized_path.to_string_lossy().to_string();

        let args = vec![
            "-v".to_string(),
            "quiet".to_string(),
            "-print_format".to_string(),
            "json".to_string(),
            "-show_format".to_string(),
            "-show_streams".to_string(),
            "-show_error".to_string(),
            path_str,
        ];

        let raw_bytes = SafeProcessRunner::run_tool_capture(
            &self.ffprobe_path,
            &args,
            self.timeout_secs,
        )
        .await?;

        let output_str = String::from_utf8_lossy(&raw_bytes);
        let probe_output: FfprobeOutput = serde_json::from_str(&output_str).map_err(|e| {
            let sample = if output_str.len() > 300 {
                format!("{}...", &output_str[..300])
            } else {
                output_str.to_string()
            };
            MediaEngineError::JsonParseError {
                path: path.to_path_buf(),
                source: e,
                raw_output_sample: sample,
            }
        })?;

        if let Some(err) = probe_output.error {
            return Err(MediaEngineError::CorruptedMedia {
                path: path.to_path_buf(),
                details: format!(
                    "FFprobe reportou erro na mídia: {} (código {:?})",
                    err.string.unwrap_or_else(|| "desconhecido".to_string()),
                    err.code
                ),
            });
        }

        self.construct_metadata(path, probe_output)
    }

    fn construct_metadata(&self, path: &Path, raw: FfprobeOutput) -> Result<MediaMetadata> {
        let mut video_streams = Vec::new();
        let mut audio_streams = Vec::new();
        let mut subtitle_streams = Vec::new();

        for stream in &raw.streams {
            if let Some(v) = parse_video_stream(stream) {
                video_streams.push(v);
            } else if let Some(a) = parse_audio_stream(stream) {
                audio_streams.push(a);
            } else if let Some(s) = parse_subtitle_stream(stream) {
                subtitle_streams.push(s);
            }
        }

        let primary_video = video_streams.first().cloned();
        let primary_audio = audio_streams.first().cloned();

        let raw_format = raw.format.unwrap_or_default();
        let format_name = raw_format
            .format_name
            .clone()
            .unwrap_or_else(|| "unknown".to_string());
        let format_long_name = raw_format.format_long_name.clone();

        // Calculate duration in seconds
        let duration_secs = raw_format
            .duration
            .as_deref()
            .and_then(|s| s.parse::<f64>().ok())
            .or_else(|| primary_video.as_ref().and_then(|v| {
                if let Some(fc) = v.frame_count {
                    if v.fps > 0.0 {
                        Some(fc as f64 / v.fps)
                    } else {
                        None
                    }
                } else {
                    None
                }
            }))
            .or_else(|| primary_audio.as_ref().and_then(|a| a.duration_secs))
            .unwrap_or(0.0);

        let duration_ms = (duration_secs * 1000.0).round() as u64;

        // File size
        let file_size_bytes = raw_format
            .size
            .as_deref()
            .and_then(|s| s.parse::<u64>().ok())
            .or_else(|| std::fs::metadata(path).ok().map(|m| m.len()))
            .unwrap_or(0);

        // Overall bitrate
        let overall_bitrate_bps = raw_format
            .bit_rate
            .as_deref()
            .and_then(|s| s.parse::<u64>().ok());

        // File modification time
        let modified_at = std::fs::metadata(path)
            .ok()
            .and_then(|m| m.modified().ok())
            .map(DateTime::<Utc>::from);

        // Aggregate tags
        let mut aggregated_tags = HashMap::new();
        for (k, v) in &raw_format.tags {
            aggregated_tags.insert(k.clone(), v.clone());
        }
        for stream in &raw.streams {
            for (k, v) in &stream.tags {
                aggregated_tags.entry(k.clone()).or_insert_with(|| v.clone());
            }
        }

        // Camera make & model identification
        let (camera_make, camera_model) = identify_camera(&aggregated_tags);

        // Recording date/time
        let recorded_at = extract_recording_time(&aggregated_tags);

        // Media type classification
        let media_type = classify_media_type(&format_name, &primary_video, &primary_audio);

        let video = primary_video.as_ref().map(|v| VideoSummary {
            codec: v.codec_name.clone(),
            profile: v.profile.clone().unwrap_or_default(),
            width: v.width,
            height: v.height,
            fps: v.fps,
            bitrate_kbps: v.bitrate_bps.map(|b| (b / 1000) as u32),
            color_space: v.color_profile.color_space.clone(),
            color_transfer: v.color_profile.color_transfer.clone(),
            color_primaries: v.color_profile.color_primaries.clone(),
            is_hdr: v.color_profile.is_hdr,
        });

        let audio = primary_audio.as_ref().map(|a| AudioSummary {
            codec: a.codec_name.clone(),
            channels: a.channels,
            sample_rate: a.sample_rate,
            bitrate_kbps: a.bitrate_bps.map(|b| (b / 1000) as u32),
        });

        let camera = if camera_make.is_some() || camera_model.is_some() {
            Some(CameraMetadata {
                make: camera_make.clone(),
                model: camera_model.clone(),
                software: aggregated_tags.get("software").or_else(|| aggregated_tags.get("Software")).cloned(),
                lens_model: aggregated_tags.get("lens_model").or_else(|| aggregated_tags.get("LensModel")).cloned(),
            })
        } else {
            None
        };

        let gps = extract_gps(&aggregated_tags);

        debug!(
            path = %path.display(),
            media_type = ?media_type,
            duration_s = duration_secs,
            resolution = %primary_video.as_ref().map(|v| format!("{}x{}", v.width, v.height)).unwrap_or_else(|| "N/A".to_string()),
            "Metadata successfully extracted"
        );

        Ok(MediaMetadata {
            file_path: path.to_path_buf(),
            media_type,
            format_name,
            format_long_name,
            duration_secs,
            duration_ms,
            file_size_bytes,
            overall_bitrate_bps,
            video_stream: primary_video,
            video,
            video_streams,
            audio_stream: primary_audio,
            audio,
            audio_streams,
            subtitle_streams,
            camera_make,
            camera_model,
            camera,
            gps,
            recorded_at,
            modified_at,
            tags: aggregated_tags,
        })
    }
}

/// MetadataExtractor wrapping ProbeEngine with cache directory management.
#[derive(Debug, Clone)]
pub struct MetadataExtractor {
    probe_engine: ProbeEngine,
    cache_dir: PathBuf,
}

impl MetadataExtractor {
    pub fn new(ffprobe_path: PathBuf, cache_dir: PathBuf, timeout_secs: u64) -> Self {
        Self {
            probe_engine: ProbeEngine::new(ffprobe_path, timeout_secs),
            cache_dir,
        }
    }

    pub async fn probe(&self, path: &Path) -> Result<MediaMetadata> {
        self.probe_engine.probe_file(path).await
    }

    pub async fn probe_file(&self, path: &Path) -> Result<MediaMetadata> {
        self.probe_engine.probe_file(path).await
    }

    pub fn cache_dir(&self) -> &Path {
        &self.cache_dir
    }
}

/// Classify whether the media is a Video, Audio, or Image.
fn classify_media_type(
    format_name: &str,
    video: &Option<crate::models::VideoStreamInfo>,
    audio: &Option<crate::models::AudioStreamInfo>,
) -> MediaType {
    let lower_format = format_name.to_lowercase();

    // Check for image formats
    if lower_format.contains("image2")
        || lower_format.contains("png_pipe")
        || lower_format.contains("jpeg_pipe")
        || lower_format == "png"
        || lower_format == "jpeg"
        || lower_format == "webp"
        || lower_format == "bmp"
        || lower_format == "tiff"
    {
        return MediaType::Image;
    }

    if let Some(ref v) = video {
        // Image codecs like mjpeg or png with 1 frame or 0 duration
        let codec = v.codec_name.to_lowercase();
        if (codec == "png" || codec == "mjpeg" || codec == "webp" || codec == "bmp" || codec == "tiff")
            && audio.is_none()
            && v.frame_count == Some(1)
        {
            return MediaType::Image;
        }
        return MediaType::Video;
    }

    if audio.is_some() {
        return MediaType::Audio;
    }

    MediaType::Unknown
}

/// Extract camera manufacturer and model from metadata tags.
fn identify_camera(tags: &HashMap<String, String>) -> (Option<String>, Option<String>) {
    let mut make = None;
    let mut model = None;

    for (k, v) in tags {
        let key_lower = k.to_lowercase();
        if key_lower == "make"
            || key_lower == "com.apple.quicktime.make"
            || key_lower == "manufacturer"
        {
            make = Some(v.trim().to_string());
        } else if key_lower == "model"
            || key_lower == "com.apple.quicktime.model"
            || key_lower == "camera_model_name"
        {
            model = Some(v.trim().to_string());
        }
    }

    // Heuristics for well-known manufacturers if not explicitly set
    if make.is_none() {
        if let Some(ref m) = model {
            let m_lower = m.to_lowercase();
            if m_lower.contains("iphone") || m_lower.contains("ipad") {
                make = Some("Apple".to_string());
            } else if m_lower.contains("gopro") || m_lower.contains("hero") {
                make = Some("GoPro".to_string());
            } else if m_lower.contains("osmo") || m_lower.contains("mini ") || m_lower.contains("mavic") || m_lower.contains("air ") || m_lower.contains("dji") {
                make = Some("DJI".to_string());
            } else if m_lower.contains("ilce") || m_lower.contains("alpha") || m_lower.contains("fx3") || m_lower.contains("fx6") {
                make = Some("Sony".to_string());
            }
        }
    }

    (make, model)
}

/// Extract recording creation time from tags.
fn extract_recording_time(tags: &HashMap<String, String>) -> Option<DateTime<Utc>> {
    for (k, v) in tags {
        let key_lower = k.to_lowercase();
        if key_lower == "creation_time"
            || key_lower == "com.apple.quicktime.creationdate"
            || key_lower == "date"
        {
            if let Ok(dt) = DateTime::parse_from_rfc3339(v) {
                return Some(dt.with_timezone(&Utc));
            }
            // Try ISO without timezone or standard space format: "2024-05-18 14:32:00"
            if let Ok(naive) = chrono::NaiveDateTime::parse_from_str(v, "%Y-%m-%d %H:%M:%S") {
                return Some(DateTime::from_naive_utc_and_offset(naive, Utc));
            }
            if let Ok(naive) = chrono::NaiveDateTime::parse_from_str(v, "%Y-%m-%dT%H:%M:%S") {
                return Some(DateTime::from_naive_utc_and_offset(naive, Utc));
            }
        }
    }
    None
}

/// Extract GPS metadata from container tags if present.
fn extract_gps(tags: &HashMap<String, String>) -> Option<GpsMetadata> {
    let loc_str = tags
        .get("location")
        .or_else(|| tags.get("location-eng"))
        .or_else(|| tags.get("com.apple.quicktime.location.ISO6709"))
        .or_else(|| tags.get("GPSCoordinates"))?;

    parse_iso6709(loc_str)
}

/// Parse ISO 6709 coordinate string (e.g., "+37.7749-122.4194/" or "+37.5407-077.4360+030.000/").
fn parse_iso6709(s: &str) -> Option<GpsMetadata> {
    let trimmed = s.trim().trim_matches('/');
    let mut split_indices = Vec::new();
    for (i, c) in trimmed.char_indices() {
        if (c == '+' || c == '-') && i > 0 {
            split_indices.push(i);
        }
    }
    if split_indices.is_empty() {
        return None;
    }

    let lat_str = &trimmed[..split_indices[0]];
    let lat: f64 = lat_str.parse().ok()?;

    if split_indices.len() == 1 {
        let lon_str = &trimmed[split_indices[0]..];
        let lon: f64 = lon_str.parse().ok()?;
        Some(GpsMetadata {
            latitude: lat,
            longitude: lon,
            altitude: None,
        })
    } else {
        let lon_str = &trimmed[split_indices[0]..split_indices[1]];
        let lon: f64 = lon_str.parse().ok()?;
        let alt_str = &trimmed[split_indices[1]..];
        let alt: Option<f64> = alt_str.parse().ok();
        Some(GpsMetadata {
            latitude: lat,
            longitude: lon,
            altitude: alt,
        })
    }
}
