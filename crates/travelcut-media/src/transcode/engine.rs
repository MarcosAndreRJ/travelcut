//! Transcoding execution engine with progress telemetry and cleanup guarantees.

use crate::error::{MediaEngineError, Result};
use crate::ffmpeg::capabilities::FfmpegCapabilities;
use crate::ffmpeg::process::SafeProcessRunner;
use crate::models::{HwAccelType, JobProgress, JobType, TranscodeOptions, TranscodeResult};
use crate::transcode::command_builder::build_transcode_arguments;
use std::path::PathBuf;
use std::sync::atomic::AtomicBool;
use std::sync::Arc;
use std::time::Instant;
use tracing::{info, warn};
use uuid::Uuid;

/// Engine for orchestrating transcoding and proxy generation jobs.
#[derive(Debug, Clone)]
pub struct TranscodeEngine {
    ffmpeg_path: PathBuf,
    timeout_secs: u64,
}

impl TranscodeEngine {
    /// Create a new transcode engine.
    pub fn new(ffmpeg_path: PathBuf, timeout_secs: u64) -> Self {
        Self {
            ffmpeg_path,
            timeout_secs: timeout_secs.max(30),
        }
    }

    /// Execute a media transcoding job asynchronously.
    pub async fn transcode<F>(
        &self,
        options: &TranscodeOptions,
        caps: &FfmpegCapabilities,
        source_duration_secs: Option<f64>,
        cancel_flag: Option<Arc<AtomicBool>>,
        mut progress_cb: F,
    ) -> Result<TranscodeResult>
    where
        F: FnMut(JobProgress) + Send + 'static,
    {
        if !options.source_path.exists() {
            return Err(MediaEngineError::InvalidPath {
                path: options.source_path.clone(),
                reason: "Arquivo fonte de transcodificação não existe".to_string(),
            });
        }

        if let Some(parent) = options.output_path.parent() {
            tokio::fs::create_dir_all(parent).await.map_err(|e| {
                MediaEngineError::Io {
                    context: format!("Falha ao criar diretório de saída: {}", parent.display()),
                    source: e,
                }
            })?;
        }

        let (args, selected_encoder) = build_transcode_arguments(options, caps);
        let job_id = format!("transcode_{}", Uuid::new_v4());

        info!(
            job_id = %job_id,
            source = %options.source_path.display(),
            output = %options.output_path.display(),
            encoder = %selected_encoder.encoder_name,
            hw_accel = ?selected_encoder.hw_type,
            "Starting media transcode execution"
        );

        let start_time = Instant::now();

        let run_res = SafeProcessRunner::run_ffmpeg_with_progress(
            &self.ffmpeg_path,
            &args,
            Some(&options.output_path),
            &job_id,
            JobType::Transcode,
            source_duration_secs,
            self.timeout_secs,
            cancel_flag,
            move |prog| {
                progress_cb(prog);
            },
        )
        .await;

        if let Err(e) = run_res {
            // Guarantee partial file deletion on error or cancellation
            if options.output_path.exists() {
                warn!(
                    output = %options.output_path.display(),
                    "Cleaning up partially generated transcode output"
                );
                let _ = tokio::fs::remove_file(&options.output_path).await;
            }
            return Err(e);
        }

        let elapsed = start_time.elapsed().as_secs_f64();
        let file_size_bytes = tokio::fs::metadata(&options.output_path)
            .await
            .map(|m| m.len())
            .unwrap_or(0);

        let duration_secs = source_duration_secs.unwrap_or(0.0);
        let average_fps = if elapsed > 0.0 && duration_secs > 0.0 {
            let total_frames = duration_secs * options.target_fps.unwrap_or(30.0);
            total_frames / elapsed
        } else {
            0.0
        };

        info!(
            output = %options.output_path.display(),
            size_mb = file_size_bytes as f64 / (1024.0 * 1024.0),
            elapsed_s = elapsed,
            "Transcoding completed successfully"
        );

        Ok(TranscodeResult {
            output_path: options.output_path.clone(),
            duration_secs,
            file_size_bytes,
            encoder_used: selected_encoder.encoder_name,
            elapsed_secs: elapsed,
            hardware_accelerated: selected_encoder.hw_type != HwAccelType::None,
            average_fps,
        })
    }
}
