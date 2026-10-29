//! Tauri IPC command bindings and event bridging for TravelCut Studio desktop frontend.
//!
//! Exposes asynchronous invoke commands callable from React via `@tauri-apps/api/core`
//! or `@tauri-apps/api/tauri`. Bridges job events and progress telemetry to the frontend.

use crate::config::MediaEngineConfig;
use crate::engine::MediaEngine;
use crate::ffmpeg::capabilities::FfmpegCapabilities;
use crate::models::{
    DiscoveredFile, FilmstripOptions, InventoryOptions, JobStatus, MediaMetadata,
    ThumbnailOptions, TranscodeOptions, TranscodeResult, WaveformData, WaveformOptions,
};
use std::path::PathBuf;
use std::sync::Arc;
use tokio::sync::RwLock;

/// Global thread-safe state container for the active MediaEngine instance in desktop apps.
#[derive(Clone, Default)]
pub struct MediaEngineState {
    pub engine: Arc<RwLock<Option<Arc<MediaEngine>>>>,
}

impl MediaEngineState {
    pub fn new() -> Self {
        Self {
            engine: Arc::new(RwLock::new(None)),
        }
    }

    /// Retrieve the currently initialized engine or error if not ready.
    pub async fn get_engine(&self) -> Result<Arc<MediaEngine>, String> {
        let guard = self.engine.read().await;
        guard
            .as_ref()
            .cloned()
            .ok_or_else(|| "MediaEngine não foi inicializado. Chame media_initialize primeiro.".to_string())
    }

    /// Set or replace the active MediaEngine instance.
    pub async fn set_engine(&self, engine: Arc<MediaEngine>) {
        let mut guard = self.engine.write().await;
        *guard = Some(engine);
    }
}

/// Initialize the media engine with configuration and return hardware/encoder capabilities.
pub async fn media_initialize(
    state: &MediaEngineState,
    config: MediaEngineConfig,
) -> Result<FfmpegCapabilities, String> {
    let engine = MediaEngine::initialize(config)
        .await
        .map_err(|e| e.to_string())?;
    let caps = engine.capabilities().clone();
    state.set_engine(engine).await;
    Ok(caps)
}

/// Query currently detected FFmpeg/FFprobe capabilities and hardware accelerators.
pub async fn media_get_capabilities(state: &MediaEngineState) -> Result<FfmpegCapabilities, String> {
    let engine = state.get_engine().await?;
    Ok(engine.capabilities().clone())
}

/// Extract comprehensive metadata for a single media file.
pub async fn media_probe(state: &MediaEngineState, path: String) -> Result<MediaMetadata, String> {
    let engine = state.get_engine().await?;
    let path_buf = PathBuf::from(path);
    engine.probe(&path_buf).await.map_err(|e| e.to_string())
}

/// Scan a directory for travel media files.
pub async fn media_scan_directory(
    state: &MediaEngineState,
    path: String,
    options: InventoryOptions,
) -> Result<Vec<DiscoveredFile>, String> {
    let engine = state.get_engine().await?;
    let path_buf = PathBuf::from(path);
    engine
        .scan_directory(&path_buf, &options, None)
        .await
        .map_err(|e| e.to_string())
}

/// Generate a thumbnail for a media file, returning the absolute path to the cached image.
pub async fn media_generate_thumbnail(
    state: &MediaEngineState,
    path: String,
    options: ThumbnailOptions,
) -> Result<String, String> {
    let engine = state.get_engine().await?;
    let path_buf = PathBuf::from(path);
    let thumb_path = engine
        .generate_thumbnail(&path_buf, &options, None)
        .await
        .map_err(|e| e.to_string())?;
    Ok(thumb_path.to_string_lossy().to_string())
}

/// Generate a filmstrip thumbnail series for timeline scrubbing.
pub async fn media_generate_filmstrip(
    state: &MediaEngineState,
    path: String,
    options: FilmstripOptions,
) -> Result<Vec<String>, String> {
    let engine = state.get_engine().await?;
    let path_buf = PathBuf::from(path);
    let frames = engine
        .generate_filmstrip(&path_buf, &options, None, |_, _, _| {})
        .await
        .map_err(|e| e.to_string())?;

    let strings = frames
        .into_iter()
        .map(|p| p.to_string_lossy().to_string())
        .collect();
    Ok(strings)
}

/// Extract multi-resolution audio peak data for timeline waveform rendering.
pub async fn media_extract_waveform(
    state: &MediaEngineState,
    path: String,
    options: WaveformOptions,
) -> Result<WaveformData, String> {
    let engine = state.get_engine().await?;
    let path_buf = PathBuf::from(path);
    engine
        .extract_waveform(&path_buf, &options, None)
        .await
        .map_err(|e| e.to_string())
}

/// Execute or queue a media transcoding / proxy generation task.
pub async fn media_transcode(
    state: &MediaEngineState,
    options: TranscodeOptions,
) -> Result<TranscodeResult, String> {
    let engine = state.get_engine().await?;
    engine
        .transcode(&options, None, None, |_| {})
        .await
        .map_err(|e| e.to_string())
}

/// Cooperatively cancel a media processing job by ID.
pub async fn media_cancel_job(state: &MediaEngineState, job_id: String) -> Result<bool, String> {
    let engine = state.get_engine().await?;
    Ok(engine.cancel_job(&job_id).await)
}

/// Cooperatively cancel all running and queued jobs.
pub async fn media_cancel_all_jobs(state: &MediaEngineState) -> Result<(), String> {
    let engine = state.get_engine().await?;
    engine.cancel_all_jobs().await;
    Ok(())
}

/// Query the execution status of a specific job.
pub async fn media_get_job_status(
    state: &MediaEngineState,
    job_id: String,
) -> Result<Option<JobStatus>, String> {
    let engine = state.get_engine().await?;
    Ok(engine.get_job_status(&job_id).await)
}
