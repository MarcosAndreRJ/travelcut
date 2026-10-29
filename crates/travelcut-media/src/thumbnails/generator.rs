//! High-performance thumbnail and filmstrip generator using FFmpeg.
//!
//! Provides fast seek, black-frame avoidance, deterministic caching,
//! atomic file persistence, and cooperative cancellation.

use crate::error::{MediaEngineError, Result};
use crate::ffmpeg::process::{normalize_windows_path, SafeProcessRunner};
use crate::models::{FilmstripOptions, ImageFormat, JobType, ThumbnailOptions};
use crate::thumbnails::cache_key::compute_thumbnail_cache_key;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use tracing::{debug, info};
use uuid::Uuid;

/// Engine responsible for generating and caching thumbnails and filmstrips.
#[derive(Debug, Clone)]
pub struct ThumbnailEngine {
    ffmpeg_path: PathBuf,
    cache_dir: PathBuf,
    temp_dir: PathBuf,
    timeout_secs: u64,
}

impl ThumbnailEngine {
    /// Create a new thumbnail engine.
    pub fn new(
        ffmpeg_path: PathBuf,
        cache_dir: PathBuf,
        temp_dir: PathBuf,
        timeout_secs: u64,
    ) -> Self {
        Self {
            ffmpeg_path,
            cache_dir: cache_dir.join("thumbnails"),
            temp_dir,
            timeout_secs: timeout_secs.max(10),
        }
    }

    /// Generate or retrieve from cache a single video/image thumbnail.
    pub async fn generate_thumbnail(
        &self,
        source_path: &Path,
        options: &ThumbnailOptions,
        cancel_flag: Option<Arc<AtomicBool>>,
    ) -> Result<PathBuf> {
        if !source_path.exists() {
            return Err(MediaEngineError::InvalidPath {
                path: source_path.to_path_buf(),
                reason: "Arquivo de mídia para miniatura não encontrado".to_string(),
            });
        }

        tokio::fs::create_dir_all(&self.cache_dir).await.map_err(|e| {
            MediaEngineError::Io {
                context: format!("Falha ao criar diretório de miniaturas: {}", self.cache_dir.display()),
                source: e,
            }
        })?;

        tokio::fs::create_dir_all(&self.temp_dir).await.map_err(|e| {
            MediaEngineError::Io {
                context: format!("Falha ao criar diretório temporário: {}", self.temp_dir.display()),
                source: e,
            }
        })?;

        let metadata = std::fs::metadata(source_path).map_err(|e| MediaEngineError::Io {
            context: format!("Falha ao ler atributos de {}", source_path.display()),
            source: e,
        })?;
        let size_bytes = metadata.len();
        let modified_ms = metadata
            .modified()
            .ok()
            .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
            .map(|d| d.as_millis() as i64)
            .unwrap_or(0);

        // Determine timestamp to extract
        let width = options.width;
        let height = options.height;
        let ext = options.format.extension();

        let mut target_time = options.timestamp_secs;
        if let Some(pct) = options.percentage {
            let dur = options.duration_secs.unwrap_or(10.0);
            target_time = (dur * (pct.clamp(0.0, 100.0) as f64 / 100.0)).max(0.0);
        } else if let Some(secs) = options.time_offset_secs {
            target_time = secs.max(0.0);
        }

        // Smart offset: avoid black initial frames when timestamp is 0
        if options.smart_offset && target_time < 0.1 {
            if let Some(dur) = options.duration_secs {
                if dur > 2.0 {
                    target_time = 1.0;
                } else if dur > 0.5 {
                    target_time = dur * 0.2;
                }
            } else {
                target_time = 1.0;
            }
        }

        // Compute deterministic cache key
        let cache_key = compute_thumbnail_cache_key(
            source_path,
            size_bytes,
            modified_ms,
            width,
            height,
            target_time,
            ext,
        );

        let final_cache_path = self.cache_dir.join(format!("{}.{}", cache_key, ext));

        // Check if cache entry already exists and is non-empty
        if final_cache_path.exists() {
            if let Ok(meta) = tokio::fs::metadata(&final_cache_path).await {
                if meta.len() > 0 {
                    debug!(path = %final_cache_path.display(), "Thumbnail cache hit");
                    return Ok(final_cache_path);
                }
            }
        }

        // Generate thumbnail into temporary file
        let temp_filename = format!("thumb_{}_{}.{}", Uuid::new_v4(), cache_key, ext);
        let temp_output_path = self.temp_dir.join(temp_filename);

        let vf_filter = format!(
            "scale={}:{}:force_original_aspect_ratio=decrease",
            width, height
        );

        let normalized_source = normalize_windows_path(source_path);
        let normalized_temp = normalize_windows_path(&temp_output_path);

        let mut args = vec![
            "-ss".to_string(),
            format!("{:.3}", target_time),
            "-i".to_string(),
            normalized_source.to_string_lossy().to_string(),
            "-vframes".to_string(),
            "1".to_string(),
            "-vf".to_string(),
            vf_filter,
            "-threads".to_string(),
            "1".to_string(),
        ];

        match options.format {
            ImageFormat::Jpeg => {
                args.push("-q:v".to_string());
                args.push("2".to_string());
            }
            ImageFormat::Webp => {
                args.push("-quality".to_string());
                args.push("85".to_string());
            }
            ImageFormat::Png => {
                // PNG default lossless
            }
        }

        args.push("-y".to_string());
        args.push(normalized_temp.to_string_lossy().to_string());

        let job_id = format!("thumb_{}", cache_key);
        SafeProcessRunner::run_ffmpeg_with_progress(
            &self.ffmpeg_path,
            &args,
            Some(&temp_output_path),
            &job_id,
            JobType::Thumbnail,
            Some(1.0),
            self.timeout_secs,
            cancel_flag,
            |_| {},
        )
        .await?;

        // Atomic commit (rename/move) to final cache destination
        if tokio::fs::rename(&temp_output_path, &final_cache_path).await.is_err() {
            // Fallback to copy and remove in case source and target reside on different mount points
            tokio::fs::copy(&temp_output_path, &final_cache_path)
                .await
                .map_err(|copy_err| MediaEngineError::Io {
                    context: format!(
                        "Falha ao persistir miniatura no cache: {}",
                        final_cache_path.display()
                    ),
                    source: copy_err,
                })?;
            let _ = tokio::fs::remove_file(&temp_output_path).await;
        }

        info!(
            source = %source_path.display(),
            output = %final_cache_path.display(),
            target_time,
            "Thumbnail generated and cached"
        );

        Ok(final_cache_path)
    }

    /// Generate an evenly spaced filmstrip series of thumbnails for timeline scrubbing.
    pub async fn generate_filmstrip<F>(
        &self,
        source_path: &Path,
        options: &FilmstripOptions,
        cancel_flag: Option<Arc<AtomicBool>>,
        mut progress_cb: F,
    ) -> Result<Vec<PathBuf>>
    where
        F: FnMut(usize, usize, &Path) + Send + 'static,
    {
        let count = options.count.max(1);
        let duration = options.duration_secs.unwrap_or(10.0).max(0.1);

        let mut timestamps = Vec::with_capacity(count);
        if count == 1 {
            timestamps.push(duration * 0.5);
        } else {
            let step = duration / (count as f64);
            for i in 0..count {
                let t = (i as f64 * step + step * 0.5).min(duration);
                timestamps.push(t);
            }
        }

        let mut results = Vec::with_capacity(count);

        for (idx, &ts) in timestamps.iter().enumerate() {
            if let Some(ref flag) = cancel_flag {
                if flag.load(Ordering::Relaxed) {
                    return Err(MediaEngineError::TaskCancelled {
                        job_id: "filmstrip_generation".to_string(),
                    });
                }
            }

            let thumb_opt = ThumbnailOptions {
                timestamp_secs: ts,
                width: options.width,
                height: options.height,
                fast_seek: true,
                output_path: None,
                time_offset_secs: Some(ts),
                percentage: None,
                format: options.format,
                smart_offset: false,
                duration_secs: Some(duration),
            };

            let path = self
                .generate_thumbnail(source_path, &thumb_opt, cancel_flag.clone())
                .await?;

            progress_cb(idx + 1, count, &path);
            results.push(path);
        }

        Ok(results)
    }
}
