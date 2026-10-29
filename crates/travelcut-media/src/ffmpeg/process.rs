//! Safe, sandboxed process execution for FFmpeg and FFprobe.
//!
//! Enforces:
//! - Direct executable invocation (never through `cmd.exe` or `powershell`).
//! - Separate argument lists (preventing argument injection).
//! - Windows `CREATE_NO_WINDOW` flag.
//! - Long path prefix handling (`\\?\`).
//! - Real-time progress monitoring via `-progress pipe:1`.
//! - Cancellation monitoring with graceful/forceful process termination.
//! - Automatic cleanup of partial/temporary files on error or cancellation.

use crate::error::{MediaEngineError, Result};
use crate::models::{JobProgress, JobStatus, JobType};
use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Instant;
use tokio::io::{AsyncBufReadExt, BufReader};
use tokio::process::Command;
use tracing::{debug, error, info, warn};

/// Windows Long Path Prefix helper.
pub fn normalize_windows_path(path: &Path) -> PathBuf {
    #[cfg(windows)]
    {
        let path_str = path.to_string_lossy();
        if path_str.len() > 240 && !path_str.starts_with(r"\\?\") && !path_str.starts_with(r"\\") {
            if let Ok(canonical) = std::fs::canonicalize(path) {
                return canonical;
            }
        }
    }
    path.to_path_buf()
}

/// Runner for executing FFmpeg jobs with monitoring, progress callbacks, and cancellation.
pub struct SafeProcessRunner;

impl SafeProcessRunner {
    /// Execute an FFmpeg command with asynchronous stderr/stdout progress parsing.
    ///
    /// If cancelled or on error, the specified `output_file` is immediately deleted to
    /// ensure that partial, incomplete files are never treated as valid cache outputs.
    #[allow(clippy::too_many_arguments)]
    pub async fn run_ffmpeg_with_progress<F>(
        ffmpeg_bin: &Path,
        args: &[String],
        output_file: Option<&Path>,
        job_id: &str,
        job_type: JobType,
        total_duration_secs: Option<f64>,
        timeout_secs: u64,
        cancel_flag: Option<Arc<AtomicBool>>,
        mut progress_cb: F,
    ) -> Result<()>
    where
        F: FnMut(JobProgress) + Send + 'static,
    {
        let start_time = Instant::now();
        let cmd_string = format!("{} {}", ffmpeg_bin.display(), args.join(" "));
        debug!(job_id, command = %cmd_string, "Launching FFmpeg child process");

        let mut cmd = Command::new(ffmpeg_bin);
        cmd.args(args);
        cmd.stdout(Stdio::piped());
        cmd.stderr(Stdio::piped());

        #[cfg(windows)]
        {
            // CREATE_NO_WINDOW = 0x08000000
            cmd.creation_flags(0x08000000);
        }

        let mut child = cmd.spawn().map_err(|e| MediaEngineError::ProcessSpawnError {
            command: ffmpeg_bin.display().to_string(),
            source: e,
        })?;

        let stdout = child.stdout.take().ok_or_else(|| MediaEngineError::Io {
            context: "Falha ao obter stdout do processo FFmpeg".to_string(),
            source: std::io::Error::other("stdout piped handle missing"),
        })?;

        let stderr = child.stderr.take().ok_or_else(|| MediaEngineError::Io {
            context: "Falha ao obter stderr do processo FFmpeg".to_string(),
            source: std::io::Error::other("stderr piped handle missing"),
        })?;

        let mut stdout_reader = BufReader::new(stdout).lines();
        let mut stderr_reader = BufReader::new(stderr).lines();

        let mut stderr_lines_history: Vec<String> = Vec::with_capacity(30);

        let timeout_duration = std::time::Duration::from_secs(timeout_secs);
        let timeout_instant = tokio::time::Instant::now() + timeout_duration;

        let mut current_frame: Option<u64> = None;
        let mut current_fps: Option<f64> = None;
        let mut current_out_time_secs: Option<f64> = None;

        let cancelled = Arc::new(AtomicBool::new(false));
        let cancelled_clone = cancelled.clone();
        let cancel_flag_clone = cancel_flag.clone();

        loop {
            // Check cancellation request
            if let Some(ref flag) = cancel_flag_clone {
                if flag.load(Ordering::Relaxed) {
                    cancelled_clone.store(true, Ordering::Relaxed);
                    warn!(job_id, "Cancellation detected. Terminating FFmpeg process...");
                    let _ = child.kill().await;
                    if let Some(out) = output_file {
                        let _ = tokio::fs::remove_file(out).await;
                    }
                    return Err(MediaEngineError::TaskCancelled {
                        job_id: job_id.to_string(),
                    });
                }
            }

            tokio::select! {
                _ = tokio::time::sleep_until(timeout_instant) => {
                    error!(job_id, timeout_secs, "FFmpeg process timed out. Killing...");
                    let _ = child.kill().await;
                    if let Some(out) = output_file {
                        let _ = tokio::fs::remove_file(out).await;
                    }
                    return Err(MediaEngineError::ToolTimeout {
                        tool_name: "ffmpeg".to_string(),
                        path: output_file.map(|p| p.to_path_buf()).unwrap_or_else(|| ffmpeg_bin.to_path_buf()),
                        timeout_secs,
                    });
                }

                line_res = stdout_reader.next_line() => {
                    match line_res {
                        Ok(Some(line)) => {
                            // Parse machine-readable -progress lines e.g.:
                            // frame=123
                            // fps=24.0
                            // out_time_ms=1234567
                            // out_time=00:00:01.234567
                            // progress=continue / progress=end
                            let trimmed = line.trim();
                            if let Some(idx) = trimmed.find('=') {
                                let key = &trimmed[..idx];
                                let val = &trimmed[idx + 1..];

                                match key {
                                    "frame" => {
                                        if let Ok(f) = val.parse::<u64>() {
                                            current_frame = Some(f);
                                        }
                                    }
                                    "fps" => {
                                        if let Ok(f) = val.parse::<f64>() {
                                            current_fps = Some(f);
                                        }
                                    }
                                    "out_time_us" | "out_time_ms" => {
                                        if let Ok(us) = val.parse::<f64>() {
                                            // ffmpeg emits microseconds in out_time_us
                                            current_out_time_secs = Some(us / 1_000_000.0);
                                        }
                                    }
                                    "progress" => {
                                        let elapsed = start_time.elapsed().as_secs_f64();
                                        let mut percentage = 0.0f32;
                                        let mut eta = None;

                                        if let (Some(cur), Some(tot)) = (current_out_time_secs, total_duration_secs) {
                                            if tot > 0.0 {
                                                percentage = ((cur / tot) * 100.0).clamp(0.0, 99.9) as f32;
                                                if percentage > 1.0 {
                                                    let remaining_ratio = (100.0 - percentage) / percentage;
                                                    eta = Some(elapsed * remaining_ratio as f64);
                                                }
                                            }
                                        }

                                        let progress = JobProgress {
                                            job_id: job_id.to_string(),
                                            job_type: Some(job_type),
                                            status: Some(JobStatus::Running),
                                            percentage,
                                            message: format!("Processando mídia... ({:.1}%)", percentage),
                                            current_frame,
                                            frames_processed: current_frame,
                                            total_frames: None,
                                            current_time_secs: current_out_time_secs,
                                            total_duration_secs,
                                            current_fps,
                                            speed_fps: current_fps,
                                            elapsed_secs: elapsed,
                                            eta_secs: eta,
                                        };
                                        progress_cb(progress);
                                    }
                                    _ => {}
                                }
                            }
                        }
                        Ok(None) => {
                            // stdout reached EOF
                        }
                        Err(e) => {
                            debug!(job_id, error = %e, "Stdout read error or EOF");
                        }
                    }
                }

                stderr_line = stderr_reader.next_line() => {
                    match stderr_line {
                        Ok(Some(line)) => {
                            if stderr_lines_history.len() >= 25 {
                                stderr_lines_history.remove(0);
                            }
                            stderr_lines_history.push(line);
                        }
                        Ok(None) => {}
                        Err(_) => {}
                    }
                }

                status_res = child.wait() => {
                    match status_res {
                        Ok(status) => {
                            if status.success() {
                                info!(job_id, elapsed_s = start_time.elapsed().as_secs_f64(), "FFmpeg job completed successfully");
                                let progress = JobProgress {
                                    job_id: job_id.to_string(),
                                    job_type: Some(job_type),
                                    status: Some(JobStatus::Completed),
                                    percentage: 100.0,
                                    message: "Concluído com sucesso".to_string(),
                                    current_frame,
                                    frames_processed: current_frame,
                                    total_frames: None,
                                    current_time_secs: total_duration_secs,
                                    total_duration_secs,
                                    current_fps,
                                    speed_fps: current_fps,
                                    elapsed_secs: start_time.elapsed().as_secs_f64(),
                                    eta_secs: Some(0.0),
                                };
                                progress_cb(progress);
                                return Ok(());
                            } else {
                                let stderr_tail = stderr_lines_history.join("\n");
                                error!(job_id, exit_code = ?status.code(), stderr = %stderr_tail, "FFmpeg process failed");

                                // Clean up partial output file
                                if let Some(out) = output_file {
                                    let _ = tokio::fs::remove_file(out).await;
                                }

                                return Err(MediaEngineError::ToolExecutionFailed {
                                    tool_name: "ffmpeg".to_string(),
                                    exit_code: status.code(),
                                    stderr_tail,
                                    command_line: cmd_string,
                                });
                            }
                        }
                        Err(io_err) => {
                            if let Some(out) = output_file {
                                let _ = tokio::fs::remove_file(out).await;
                            }
                            return Err(MediaEngineError::Io {
                                context: "Erro aguardando processo FFmpeg".to_string(),
                                source: io_err,
                            });
                        }
                    }
                }
            }
        }
    }

    /// Execute a tool (e.g. ffprobe) safely to retrieve standard output.
    pub async fn run_tool_capture(
        bin: &Path,
        args: &[String],
        timeout_secs: u64,
    ) -> Result<Vec<u8>> {
        let mut cmd = Command::new(bin);
        cmd.args(args);
        cmd.stdout(Stdio::piped());
        cmd.stderr(Stdio::piped());

        #[cfg(windows)]
        {
            cmd.creation_flags(0x08000000);
        }

        let child = cmd.spawn().map_err(|e| MediaEngineError::ProcessSpawnError {
            command: bin.display().to_string(),
            source: e,
        })?;

        let timeout_duration = std::time::Duration::from_secs(timeout_secs);
        match tokio::time::timeout(timeout_duration, child.wait_with_output()).await {
            Ok(Ok(output)) => {
                if output.status.success() {
                    Ok(output.stdout)
                } else {
                    let stderr_tail = String::from_utf8_lossy(&output.stderr)
                        .lines()
                        .rev()
                        .take(15)
                        .collect::<Vec<_>>()
                        .into_iter()
                        .rev()
                        .collect::<Vec<_>>()
                        .join("\n");

                    Err(MediaEngineError::ToolExecutionFailed {
                        tool_name: bin.display().to_string(),
                        exit_code: output.status.code(),
                        stderr_tail,
                        command_line: format!("{} {}", bin.display(), args.join(" ")),
                    })
                }
            }
            Ok(Err(e)) => Err(MediaEngineError::Io {
                context: format!("Erro executando {}", bin.display()),
                source: e,
            }),
            Err(_) => Err(MediaEngineError::ToolTimeout {
                tool_name: bin.display().to_string(),
                path: bin.to_path_buf(),
                timeout_secs,
            }),
        }
    }
}
