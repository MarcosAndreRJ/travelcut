//! Unified high-level media engine facade for TravelCut Studio.
//!
//! Encapsulates FFmpeg/FFprobe binary discovery, capability negotiation,
//! metadata probing, directory scanning, thumbnail/filmstrip generation,
//! audio peak extraction, and transcode queue management.

use crate::config::MediaEngineConfig;
use crate::error::{MediaEngineError, Result};
use crate::ffmpeg::capabilities::FfmpegCapabilities;
use crate::ffmpeg::discovery::FfmpegDiscovery;
use crate::inventory::scanner::InventoryScanner;
use crate::models::{
    DiscoveredFile, FilmstripOptions, InventoryOptions, JobEvent, JobId, JobPriority, JobProgress,
    JobStatus, JobType, MediaMetadata, ThumbnailOptions, TranscodeOptions, TranscodeResult,
    WaveformData, WaveformOptions,
};
use crate::probe::metadata::MetadataExtractor;
use crate::queue::manager::JobQueueManager;
use crate::thumbnails::generator::ThumbnailEngine;
use crate::transcode::engine::TranscodeEngine;
use crate::waveform::extractor::WaveformEngine;
use std::path::{Path, PathBuf};
use std::sync::atomic::AtomicBool;
use std::sync::Arc;
use tokio::sync::broadcast;
use tracing::info;

/// Unified MediaEngine facade providing thread-safe, high-performance access
/// to all audio/video capabilities for desktop and CLI workflows.
pub struct MediaEngine {
    config: MediaEngineConfig,
    capabilities: FfmpegCapabilities,
    metadata_extractor: MetadataExtractor,
    inventory_scanner: InventoryScanner,
    thumbnail_engine: ThumbnailEngine,
    waveform_engine: WaveformEngine,
    transcode_engine: TranscodeEngine,
    queue_manager: JobQueueManager,
}

impl MediaEngine {
    /// Initialize the MediaEngine with user configuration.
    ///
    /// Automatically locates FFmpeg/FFprobe binaries, queries encoder and hardware
    /// acceleration capabilities, and spins up the asynchronous job manager.
    pub async fn initialize(config: MediaEngineConfig) -> Result<Arc<Self>> {
        info!("Initializing TravelCut Studio Media Engine");

        // Validate or discover binaries
        let ffmpeg_path = match config.ffmpeg_path {
            Some(ref p) => p.clone(),
            None => FfmpegDiscovery::find_ffmpeg()?,
        };

        let ffprobe_path = match config.ffprobe_path {
            Some(ref p) => p.clone(),
            None => FfmpegDiscovery::find_ffprobe()?,
        };

        // Query capabilities
        let capabilities = FfmpegDiscovery::detect_capabilities(&ffmpeg_path).await?;
        info!(
            version = %capabilities.version,
            nvenc = capabilities.has_encoder("h264_nvenc"),
            qsv = capabilities.has_encoder("h264_qsv"),
            amf = capabilities.has_encoder("h264_amf"),
            "FFmpeg capabilities initialized"
        );

        // Ensure directories exist
        tokio::fs::create_dir_all(&config.cache_dir).await.map_err(|e| {
            MediaEngineError::Io {
                context: format!("Falha ao criar diretório de cache: {}", config.cache_dir.display()),
                source: e,
            }
        })?;

        tokio::fs::create_dir_all(&config.temp_dir).await.map_err(|e| {
            MediaEngineError::Io {
                context: format!("Falha ao criar diretório temporário: {}", config.temp_dir.display()),
                source: e,
            }
        })?;

        let metadata_extractor = MetadataExtractor::new(
            ffprobe_path.clone(),
            config.cache_dir.clone(),
            config.timeout_secs,
        );

        let inventory_scanner = InventoryScanner::new(
            ffprobe_path.clone(),
            config.cache_dir.clone(),
            config.timeout_secs,
        );

        let thumbnail_engine = ThumbnailEngine::new(
            ffmpeg_path.clone(),
            config.cache_dir.clone(),
            config.temp_dir.clone(),
            config.timeout_secs,
        );

        let waveform_engine = WaveformEngine::new(
            ffmpeg_path.clone(),
            config.cache_dir.clone(),
            config.temp_dir.clone(),
            config.timeout_secs,
        );

        let transcode_engine = TranscodeEngine::new(ffmpeg_path.clone(), config.timeout_secs);

        let queue_manager = JobQueueManager::new(config.max_concurrent_jobs);

        let engine = Arc::new(Self {
            config,
            capabilities,
            metadata_extractor,
            inventory_scanner,
            thumbnail_engine,
            waveform_engine,
            transcode_engine,
            queue_manager,
        });

        Ok(engine)
    }

    /// Access active configuration.
    pub fn config(&self) -> &MediaEngineConfig {
        &self.config
    }

    /// Access detected FFmpeg/FFprobe capabilities and hardware accelerators.
    pub fn capabilities(&self) -> &FfmpegCapabilities {
        &self.capabilities
    }

    /// Extract comprehensive metadata for a video, audio, or image file.
    pub async fn probe(&self, path: &Path) -> Result<MediaMetadata> {
        self.metadata_extractor.probe(path).await
    }

    /// Scan a directory recursively or shallowly for travel media files.
    pub async fn scan_directory(
        &self,
        path: &Path,
        options: &InventoryOptions,
        cancel_flag: Option<Arc<AtomicBool>>,
    ) -> Result<Vec<DiscoveredFile>> {
        self.inventory_scanner
            .scan_directory(path, options, cancel_flag, |_| {})
            .await
    }

    /// Scan directory with real-time file discovery callback.
    pub async fn scan_directory_with_progress<F>(
        &self,
        path: &Path,
        options: &InventoryOptions,
        cancel_flag: Option<Arc<AtomicBool>>,
        progress_cb: F,
    ) -> Result<Vec<DiscoveredFile>>
    where
        F: FnMut(&DiscoveredFile) + Send + 'static,
    {
        self.inventory_scanner
            .scan_directory(path, options, cancel_flag, progress_cb)
            .await
    }

    /// Generate or fetch from cache a single thumbnail.
    pub async fn generate_thumbnail(
        &self,
        source_path: &Path,
        options: &ThumbnailOptions,
        cancel_flag: Option<Arc<AtomicBool>>,
    ) -> Result<PathBuf> {
        self.thumbnail_engine
            .generate_thumbnail(source_path, options, cancel_flag)
            .await
    }

    /// Generate or fetch from cache a filmstrip thumbnail series.
    pub async fn generate_filmstrip<F>(
        &self,
        source_path: &Path,
        options: &FilmstripOptions,
        cancel_flag: Option<Arc<AtomicBool>>,
        progress_cb: F,
    ) -> Result<Vec<PathBuf>>
    where
        F: FnMut(usize, usize, &Path) + Send + 'static,
    {
        self.thumbnail_engine
            .generate_filmstrip(source_path, options, cancel_flag, progress_cb)
            .await
    }

    /// Extract audio peak data for timeline rendering.
    pub async fn extract_waveform(
        &self,
        source_path: &Path,
        options: &WaveformOptions,
        cancel_flag: Option<Arc<AtomicBool>>,
    ) -> Result<WaveformData> {
        self.waveform_engine
            .extract_waveform(source_path, options, cancel_flag)
            .await
    }

    /// Render a visual waveform PNG image using `showwavespic`.
    pub async fn render_waveform_image(
        &self,
        source_path: &Path,
        width: u32,
        height: u32,
        color_hex: &str,
        cancel_flag: Option<Arc<AtomicBool>>,
    ) -> Result<PathBuf> {
        self.waveform_engine
            .render_waveform_image(source_path, width, height, color_hex, cancel_flag)
            .await
    }

    /// Transcode media directly with progress callback.
    pub async fn transcode<F>(
        &self,
        options: &TranscodeOptions,
        source_duration_secs: Option<f64>,
        cancel_flag: Option<Arc<AtomicBool>>,
        progress_cb: F,
    ) -> Result<TranscodeResult>
    where
        F: FnMut(JobProgress) + Send + 'static,
    {
        self.transcode_engine
            .transcode(
                options,
                &self.capabilities,
                source_duration_secs,
                cancel_flag,
                progress_cb,
            )
            .await
    }

    /// Submit a transcode job to the prioritized asynchronous queue.
    pub async fn submit_transcode_job(
        self: &Arc<Self>,
        options: TranscodeOptions,
        priority: JobPriority,
        source_duration_secs: Option<f64>,
    ) -> JobId {
        let engine = Arc::clone(self);
        self.queue_manager
            .submit(JobType::Transcode, priority, move |cancel_flag, prog_tx| {
                async move {
                    engine
                        .transcode(
                            &options,
                            source_duration_secs,
                            Some(cancel_flag),
                            move |prog| {
                                let _ = prog_tx.try_send(prog);
                            },
                        )
                        .await
                        .map(|_| ())
                }
            })
            .await
    }

    /// Subscribe to real-time events from the job queue.
    pub fn subscribe_events(&self) -> broadcast::Receiver<JobEvent> {
        self.queue_manager.subscribe()
    }

    /// Cooperatively cancel a job by ID.
    pub async fn cancel_job(&self, job_id: &str) -> bool {
        self.queue_manager.cancel_job(job_id).await
    }

    /// Cancel all pending and running jobs.
    pub async fn cancel_all_jobs(&self) {
        self.queue_manager.cancel_all().await;
    }

    /// Query the status of a queued or running job.
    pub async fn get_job_status(&self, job_id: &str) -> Option<JobStatus> {
        self.queue_manager.get_job_status(job_id).await
    }

    /// Query latest progress of a job.
    pub async fn get_job_progress(&self, job_id: &str) -> Option<JobProgress> {
        self.queue_manager.get_job_progress(job_id).await
    }
}
