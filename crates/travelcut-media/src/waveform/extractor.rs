//! Audio waveform peak extraction and visualization engine.
//!
//! Streams raw 16-bit PCM from FFmpeg, computes windowed min/max/RMS values
//! for timeline zoom levels, persists to disk cache, and renders visual PNGs.

use crate::error::{MediaEngineError, Result};
use crate::ffmpeg::process::{normalize_windows_path, SafeProcessRunner};
use crate::models::{WaveformData, WaveformOptions, WaveformPeak};
use crate::waveform::cache_key::{compute_waveform_cache_key, compute_waveform_image_cache_key};
use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use tokio::io::AsyncReadExt;
use tokio::process::Command;
use tracing::{debug, info, warn};
use uuid::Uuid;

const SAMPLE_RATE: u32 = 8000;

/// Engine for extracting audio peaks and rendering visual waveform images.
#[derive(Debug, Clone)]
pub struct WaveformEngine {
    ffmpeg_path: PathBuf,
    cache_dir: PathBuf,
    temp_dir: PathBuf,
    timeout_secs: u64,
}

impl WaveformEngine {
    /// Create a new waveform engine.
    pub fn new(
        ffmpeg_path: PathBuf,
        cache_dir: PathBuf,
        temp_dir: PathBuf,
        timeout_secs: u64,
    ) -> Self {
        Self {
            ffmpeg_path,
            cache_dir: cache_dir.join("waveforms"),
            temp_dir,
            timeout_secs: timeout_secs.max(15),
        }
    }

    /// Extract audio peaks for timeline display with multi-resolution sampling.
    pub async fn extract_waveform(
        &self,
        source_path: &Path,
        options: &WaveformOptions,
        cancel_flag: Option<Arc<AtomicBool>>,
    ) -> Result<WaveformData> {
        if !source_path.exists() {
            return Err(MediaEngineError::InvalidPath {
                path: source_path.to_path_buf(),
                reason: "Arquivo de mídia para extração de waveform não existe".to_string(),
            });
        }

        tokio::fs::create_dir_all(&self.cache_dir).await.map_err(|e| {
            MediaEngineError::Io {
                context: format!("Falha ao criar diretório de waveform: {}", self.cache_dir.display()),
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

        let pps = options.points_per_second.clamp(5, 500);
        let cache_key = compute_waveform_cache_key(source_path, size_bytes, modified_ms, pps);
        let cache_file = self.cache_dir.join(format!("{}.json", cache_key));

        // Check if cached waveform JSON exists
        if cache_file.exists() {
            if let Ok(content) = tokio::fs::read_to_string(&cache_file).await {
                if let Ok(cached_data) = serde_json::from_str::<WaveformData>(&content) {
                    debug!(path = %cache_file.display(), "Waveform cache hit");
                    return Ok(cached_data);
                }
            }
        }

        // Run FFmpeg to stream raw PCM (8kHz, 16-bit mono)
        let normalized_source = normalize_windows_path(source_path);
        let args = vec![
            "-v".to_string(),
            "error".to_string(),
            "-i".to_string(),
            normalized_source.to_string_lossy().to_string(),
            "-vn".to_string(),
            "-ac".to_string(),
            "1".to_string(),
            "-ar".to_string(),
            SAMPLE_RATE.to_string(),
            "-f".to_string(),
            "s16le".to_string(),
            "-".to_string(),
        ];

        let mut cmd = Command::new(&self.ffmpeg_path);
        cmd.args(&args);
        cmd.stdout(Stdio::piped());
        cmd.stderr(Stdio::piped());

        #[cfg(windows)]
        {
            cmd.creation_flags(0x08000000);
        }

        let mut child = cmd.spawn().map_err(|e| MediaEngineError::ProcessSpawnError {
            command: self.ffmpeg_path.display().to_string(),
            source: e,
        })?;

        let mut stdout = child.stdout.take().ok_or_else(|| MediaEngineError::Io {
            context: "Falha ao obter stream stdout do FFmpeg para waveform".to_string(),
            source: std::io::Error::other("stdout missing"),
        })?;

        let mut raw_pcm = Vec::new();
        let mut buffer = [0u8; 16384];

        let timeout_duration = std::time::Duration::from_secs(self.timeout_secs);
        let timeout_instant = tokio::time::Instant::now() + timeout_duration;

        loop {
            if let Some(ref flag) = cancel_flag {
                if flag.load(Ordering::Relaxed) {
                    let _ = child.kill().await;
                    return Err(MediaEngineError::TaskCancelled {
                        job_id: format!("waveform_{}", cache_key),
                    });
                }
            }

            tokio::select! {
                _ = tokio::time::sleep_until(timeout_instant) => {
                    let _ = child.kill().await;
                    return Err(MediaEngineError::ToolTimeout {
                        tool_name: "ffmpeg_waveform".to_string(),
                        path: source_path.to_path_buf(),
                        timeout_secs: self.timeout_secs,
                    });
                }
                read_res = stdout.read(&mut buffer) => {
                    match read_res {
                        Ok(0) => break, // EOF
                        Ok(n) => {
                            raw_pcm.extend_from_slice(&buffer[..n]);
                        }
                        Err(e) => {
                            let _ = child.kill().await;
                            return Err(MediaEngineError::Io {
                                context: "Erro lendo fluxo de áudio PCM".to_string(),
                                source: e,
                            });
                        }
                    }
                }
            }
        }

        let status = child.wait().await.map_err(|e| MediaEngineError::Io {
            context: "Erro aguardando processo FFmpeg de áudio".to_string(),
            source: e,
        })?;

        // If the media has no audio track, FFmpeg exits with non-zero or 0 bytes
        if raw_pcm.is_empty() || !status.success() {
            warn!(
                path = %source_path.display(),
                exit_code = ?status.code(),
                "No audio track found or file has silent/inaccessible audio"
            );
            return Ok(WaveformData {
                points_per_second: pps as f64,
                duration_secs: 0.0,
                channels: 0,
                peaks: Vec::new(),
                overall_rms: 0.0,
                peak_max: 0.0,
            });
        }

        // Process PCM samples (i16) into windowed peaks
        let total_samples = raw_pcm.len() / 2;
        let duration_secs = total_samples as f64 / SAMPLE_RATE as f64;
        let window_size = ((SAMPLE_RATE as f64) / (pps as f64)).round().max(1.0) as usize;

        let mut peaks = Vec::new();
        let mut overall_sum_sq = 0.0f64;
        let mut overall_peak_max = 0.0f32;

        let mut sample_idx = 0;
        while sample_idx < total_samples {
            let chunk_end = (sample_idx + window_size).min(total_samples);
            let mut min_val = 0.0f32;
            let mut max_val = 0.0f32;
            let mut sum_sq = 0.0f64;
            let count = chunk_end - sample_idx;

            for i in sample_idx..chunk_end {
                let byte_offset = i * 2;
                let sample_i16 = i16::from_le_bytes([raw_pcm[byte_offset], raw_pcm[byte_offset + 1]]);
                let norm = (sample_i16 as f32) / 32768.0;

                if norm < min_val {
                    min_val = norm;
                }
                if norm > max_val {
                    max_val = norm;
                }
                let norm64 = norm as f64;
                sum_sq += norm64 * norm64;
                overall_sum_sq += norm64 * norm64;

                let abs_norm = norm.abs();
                if abs_norm > overall_peak_max {
                    overall_peak_max = abs_norm;
                }
            }

            let rms = if count > 0 {
                (sum_sq / count as f64).sqrt() as f32
            } else {
                0.0
            };

            peaks.push(WaveformPeak {
                min: min_val,
                max: max_val,
                rms,
            });

            sample_idx = chunk_end;
        }

        let overall_rms = if total_samples > 0 {
            (overall_sum_sq / total_samples as f64).sqrt() as f32
        } else {
            0.0
        };

        let waveform_data = WaveformData {
            points_per_second: pps as f64,
            duration_secs,
            channels: 1,
            peaks,
            overall_rms,
            peak_max: overall_peak_max,
        };

        // Cache result atomically
        if let Ok(json_str) = serde_json::to_string(&waveform_data) {
            let temp_cache_file = self.temp_dir.join(format!("wf_{}_{}.tmp", Uuid::new_v4(), cache_key));
            if tokio::fs::write(&temp_cache_file, json_str).await.is_ok() {
                let _ = tokio::fs::rename(&temp_cache_file, &cache_file).await;
            }
        }

        info!(
            path = %source_path.display(),
            peaks_count = waveform_data.peaks.len(),
            duration_s = duration_secs,
            "Waveform successfully extracted"
        );

        Ok(waveform_data)
    }

    /// Generate a visual waveform PNG representation using FFmpeg's `showwavespic`.
    pub async fn render_waveform_image(
        &self,
        source_path: &Path,
        width: u32,
        height: u32,
        color_hex: &str,
        cancel_flag: Option<Arc<AtomicBool>>,
    ) -> Result<PathBuf> {
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

        let cache_key = compute_waveform_image_cache_key(source_path, size_bytes, modified_ms, width, height);
        let final_cache_path = self.cache_dir.join(format!("{}.png", cache_key));

        if final_cache_path.exists() {
            if let Ok(meta) = tokio::fs::metadata(&final_cache_path).await {
                if meta.len() > 0 {
                    debug!(path = %final_cache_path.display(), "Waveform image cache hit");
                    return Ok(final_cache_path);
                }
            }
        }

        let temp_output_path = self.temp_dir.join(format!("wf_img_{}_{}.png", Uuid::new_v4(), cache_key));

        let filter = format!(
            "aformat=channel_layouts=mono,showwavespic=s={}x{}:colors={}",
            width, height, color_hex
        );

        let normalized_source = normalize_windows_path(source_path);
        let normalized_temp = normalize_windows_path(&temp_output_path);

        let args = vec![
            "-i".to_string(),
            normalized_source.to_string_lossy().to_string(),
            "-filter_complex".to_string(),
            filter,
            "-frames:v".to_string(),
            "1".to_string(),
            "-y".to_string(),
            normalized_temp.to_string_lossy().to_string(),
        ];

        let job_id = format!("wf_img_{}", cache_key);
        SafeProcessRunner::run_ffmpeg_with_progress(
            &self.ffmpeg_path,
            &args,
            Some(&temp_output_path),
            &job_id,
            crate::models::JobType::Waveform,
            None,
            self.timeout_secs,
            cancel_flag,
            |_| {},
        )
        .await?;

        if tokio::fs::rename(&temp_output_path, &final_cache_path).await.is_err() {
            tokio::fs::copy(&temp_output_path, &final_cache_path)
                .await
                .map_err(|copy_err| MediaEngineError::Io {
                    context: format!("Falha ao persistir imagem da waveform: {}", final_cache_path.display()),
                    source: copy_err,
                })?;
            let _ = tokio::fs::remove_file(&temp_output_path).await;
        }

        Ok(final_cache_path)
    }
}
