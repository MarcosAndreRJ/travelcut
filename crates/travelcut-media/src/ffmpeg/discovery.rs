//! Discovery and validation of FFmpeg and FFprobe binaries.
//!
//! Searches for tools across configuration, environment variables, relative directories,
//! and system PATH. Validates each candidate by executing it and parsing output.
//!
//! # Licensing & Redistribution Notice
//! FFmpeg is licensed under the LGPL v2.1+ or GPL v2+ depending on build options.
//! - Enabling non-free libraries (e.g. `libfdk_aac`) makes binaries unredistributable.
//! - Enabling GPL components (e.g. `libx264`, `libx265`) places the entire binary under GPL.
//! - For commercial distribution of TravelCut Studio, binaries should be compiled
//!   under LGPL without GPL/non-free extensions, or provided via user-managed installations,
//!   or dynamic linking must respect license boundaries. TravelCut Studio does not bundle
//!   unverified non-free codecs by default.

use crate::config::EngineConfig;
use crate::error::{MediaEngineError, Result};
use crate::ffmpeg::capabilities::FfmpegCapabilities;
use std::collections::HashSet;
use std::path::{Path, PathBuf};
use tracing::{debug, info, warn};

/// Information about a successfully validated binary.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DiscoveredTool {
    /// Canonical path to executable binary.
    pub path: PathBuf,
    /// Parsed version string.
    pub version: String,
    /// Capabilities for FFmpeg (None for FFprobe).
    pub capabilities: Option<FfmpegCapabilities>,
}

/// Result of probing the system for both FFmpeg and FFprobe.
#[derive(Debug, Clone)]
pub struct ToolsetDiscovery {
    pub ffmpeg: DiscoveredTool,
    pub ffprobe: DiscoveredTool,
}

/// Manager responsible for locating and verifying FFmpeg toolset.
#[derive(Debug, Clone)]
pub struct ToolDiscoveryManager {
    config: EngineConfig,
}

impl ToolDiscoveryManager {
    pub fn new(config: EngineConfig) -> Self {
        Self { config }
    }

    /// Discover and validate both FFmpeg and FFprobe.
    pub async fn discover_all(&self) -> Result<ToolsetDiscovery> {
        let ffmpeg = self.discover_ffmpeg().await?;
        let ffprobe = self.discover_ffprobe().await?;
        info!(
            ffmpeg_path = %ffmpeg.path.display(),
            ffmpeg_version = %ffmpeg.version,
            ffprobe_path = %ffprobe.path.display(),
            ffprobe_version = %ffprobe.version,
            "FFmpeg toolset successfully discovered and verified"
        );
        Ok(ToolsetDiscovery { ffmpeg, ffprobe })
    }

    /// Discover and validate FFmpeg executable.
    pub async fn discover_ffmpeg(&self) -> Result<DiscoveredTool> {
        let mut candidates = Vec::new();

        // 1. Explicit configuration
        if let Some(ref path) = self.config.ffmpeg_path {
            candidates.push(path.clone());
        }

        // 2. Explicit tools directory
        if let Some(ref dir) = self.config.tools_dir {
            candidates.push(dir.join(binary_filename("ffmpeg")));
        }

        // 3. Environment variables
        if let Ok(path) = std::env::var("TRAVELCUT_FFMPEG_PATH") {
            candidates.push(PathBuf::from(path));
        }
        if let Ok(dir) = std::env::var("TRAVELCUT_TOOLS_DIR") {
            candidates.push(PathBuf::from(dir).join(binary_filename("ffmpeg")));
        }
        if let Ok(path) = std::env::var("FFMPEG_PATH") {
            candidates.push(PathBuf::from(path));
        }

        // 4. Relative paths / bundled locations
        if let Ok(current_exe) = std::env::current_exe() {
            if let Some(exe_dir) = current_exe.parent() {
                candidates.push(exe_dir.join(binary_filename("ffmpeg")));
                candidates.push(exe_dir.join("tools").join(binary_filename("ffmpeg")));
            }
        }
        candidates.push(PathBuf::from(format!("./tools/{}", binary_filename("ffmpeg"))));
        candidates.push(PathBuf::from(format!("../tools/{}", binary_filename("ffmpeg"))));

        // 5. System PATH lookup
        if let Ok(path) = which::which(binary_filename("ffmpeg")) {
            candidates.push(path);
        }
        if let Ok(path) = which::which("ffmpeg") {
            candidates.push(path);
        }

        let mut searched = Vec::new();
        for candidate in candidates {
            searched.push(candidate.clone());
            if candidate.is_file() {
                debug!(path = %candidate.display(), "Testing FFmpeg candidate");
                match self.validate_ffmpeg(&candidate).await {
                    Ok(tool) => return Ok(tool),
                    Err(e) => {
                        warn!(path = %candidate.display(), error = %e, "FFmpeg candidate validation failed");
                    }
                }
            }
        }

        Err(MediaEngineError::ToolNotFound {
            tool_name: "ffmpeg".to_string(),
            searched_paths: searched,
        })
    }

    /// Discover and validate FFprobe executable.
    pub async fn discover_ffprobe(&self) -> Result<DiscoveredTool> {
        let mut candidates = Vec::new();

        // 1. Explicit configuration
        if let Some(ref path) = self.config.ffprobe_path {
            candidates.push(path.clone());
        }

        // 2. Explicit tools directory
        if let Some(ref dir) = self.config.tools_dir {
            candidates.push(dir.join(binary_filename("ffprobe")));
        }

        // 3. Environment variables
        if let Ok(path) = std::env::var("TRAVELCUT_FFPROBE_PATH") {
            candidates.push(PathBuf::from(path));
        }
        if let Ok(dir) = std::env::var("TRAVELCUT_TOOLS_DIR") {
            candidates.push(PathBuf::from(dir).join(binary_filename("ffprobe")));
        }
        if let Ok(path) = std::env::var("FFPROBE_PATH") {
            candidates.push(PathBuf::from(path));
        }

        // 4. Relative paths
        if let Ok(current_exe) = std::env::current_exe() {
            if let Some(exe_dir) = current_exe.parent() {
                candidates.push(exe_dir.join(binary_filename("ffprobe")));
                candidates.push(exe_dir.join("tools").join(binary_filename("ffprobe")));
            }
        }
        candidates.push(PathBuf::from(format!("./tools/{}", binary_filename("ffprobe"))));
        candidates.push(PathBuf::from(format!("../tools/{}", binary_filename("ffprobe"))));

        // 5. System PATH
        if let Ok(path) = which::which(binary_filename("ffprobe")) {
            candidates.push(path);
        }
        if let Ok(path) = which::which("ffprobe") {
            candidates.push(path);
        }

        let mut searched = Vec::new();
        for candidate in candidates {
            searched.push(candidate.clone());
            if candidate.is_file() {
                debug!(path = %candidate.display(), "Testing FFprobe candidate");
                match self.validate_ffprobe(&candidate).await {
                    Ok(tool) => return Ok(tool),
                    Err(e) => {
                        warn!(path = %candidate.display(), error = %e, "FFprobe candidate validation failed");
                    }
                }
            }
        }

        Err(MediaEngineError::ToolNotFound {
            tool_name: "ffprobe".to_string(),
            searched_paths: searched,
        })
    }

    /// Validate FFmpeg by executing `-version`, `-hwaccels`, and `-encoders`.
    async fn validate_ffmpeg(&self, path: &Path) -> Result<DiscoveredTool> {
        let output = run_command_with_timeout(path, &["-version"], 5).await?;
        let stdout = String::from_utf8_lossy(&output.stdout);
        let first_line = stdout.lines().next().unwrap_or("").trim().to_string();

        if !first_line.contains("ffmpeg version") {
            return Err(MediaEngineError::ToolExecutionFailed {
                tool_name: "ffmpeg".to_string(),
                exit_code: output.status.code(),
                stderr_tail: format!("Saída inesperada para -version: {}", first_line),
                command_line: format!("{} -version", path.display()),
            });
        }

        // Query hardware accelerations
        let mut hwaccels = HashSet::new();
        if let Ok(hw_output) = run_command_with_timeout(path, &["-hwaccels"], 5).await {
            let hw_text = String::from_utf8_lossy(&hw_output.stdout);
            for line in hw_text.lines().skip(1) {
                let trimmed = line.trim();
                if !trimmed.is_empty() {
                    hwaccels.insert(trimmed.to_lowercase());
                }
            }
        }

        // Query encoders
        let mut video_encoders = HashSet::new();
        let mut audio_encoders = HashSet::new();
        if let Ok(enc_output) = run_command_with_timeout(path, &["-encoders"], 5).await {
            let enc_text = String::from_utf8_lossy(&enc_output.stdout);
            for line in enc_text.lines() {
                let trimmed = line.trim();
                if trimmed.starts_with("V") || trimmed.starts_with("A") {
                    let parts: Vec<&str> = trimmed.split_whitespace().collect();
                    if parts.len() >= 2 {
                        let codec_type = parts[0];
                        let name = parts[1].to_lowercase();
                        if codec_type.contains('V') {
                            video_encoders.insert(name);
                        } else if codec_type.contains('A') {
                            audio_encoders.insert(name);
                        }
                    }
                }
            }
        }

        let capabilities = FfmpegCapabilities {
            version: first_line.clone(),
            hwaccels,
            video_encoders,
            audio_encoders,
            video_decoders: HashSet::new(),
        };

        Ok(DiscoveredTool {
            path: path.to_path_buf(),
            version: first_line,
            capabilities: Some(capabilities),
        })
    }

    /// Validate FFprobe by executing `-version`.
    async fn validate_ffprobe(&self, path: &Path) -> Result<DiscoveredTool> {
        let output = run_command_with_timeout(path, &["-version"], 5).await?;
        let stdout = String::from_utf8_lossy(&output.stdout);
        let first_line = stdout.lines().next().unwrap_or("").trim().to_string();

        if !first_line.contains("ffprobe version") {
            return Err(MediaEngineError::ToolExecutionFailed {
                tool_name: "ffprobe".to_string(),
                exit_code: output.status.code(),
                stderr_tail: format!("Saída inesperada para -version: {}", first_line),
                command_line: format!("{} -version", path.display()),
            });
        }

        Ok(DiscoveredTool {
            path: path.to_path_buf(),
            version: first_line,
            capabilities: None,
        })
    }
}

/// Helper struct for discovering and validating FFmpeg toolset binaries.
pub struct FfmpegDiscovery;

impl FfmpegDiscovery {
    /// Discover FFmpeg binary synchronously by searching common paths and system PATH.
    pub fn find_ffmpeg() -> Result<PathBuf> {
        let mut searched = Vec::new();

        if let Ok(path) = std::env::var("TRAVELCUT_FFMPEG_PATH") {
            let p = PathBuf::from(path);
            if p.is_file() {
                return Ok(p);
            }
            searched.push(p);
        }
        if let Ok(dir) = std::env::var("TRAVELCUT_TOOLS_DIR") {
            let p = PathBuf::from(dir).join(binary_filename("ffmpeg"));
            if p.is_file() {
                return Ok(p);
            }
            searched.push(p);
        }
        if let Ok(path) = std::env::var("FFMPEG_PATH") {
            let p = PathBuf::from(path);
            if p.is_file() {
                return Ok(p);
            }
            searched.push(p);
        }
        if let Ok(current_exe) = std::env::current_exe() {
            if let Some(exe_dir) = current_exe.parent() {
                let p1 = exe_dir.join(binary_filename("ffmpeg"));
                if p1.is_file() {
                    return Ok(p1);
                }
                searched.push(p1);
                let p2 = exe_dir.join("tools").join(binary_filename("ffmpeg"));
                if p2.is_file() {
                    return Ok(p2);
                }
                searched.push(p2);
            }
        }
        let local_p1 = PathBuf::from(format!("./tools/{}", binary_filename("ffmpeg")));
        if local_p1.is_file() {
            return Ok(local_p1);
        }
        searched.push(local_p1);

        if let Ok(path) = which::which(binary_filename("ffmpeg")) {
            return Ok(path);
        }
        if let Ok(path) = which::which("ffmpeg") {
            return Ok(path);
        }

        Err(MediaEngineError::ToolNotFound {
            tool_name: "ffmpeg".to_string(),
            searched_paths: searched,
        })
    }

    /// Discover FFprobe binary synchronously.
    pub fn find_ffprobe() -> Result<PathBuf> {
        let mut searched = Vec::new();

        if let Ok(path) = std::env::var("TRAVELCUT_FFPROBE_PATH") {
            let p = PathBuf::from(path);
            if p.is_file() {
                return Ok(p);
            }
            searched.push(p);
        }
        if let Ok(dir) = std::env::var("TRAVELCUT_TOOLS_DIR") {
            let p = PathBuf::from(dir).join(binary_filename("ffprobe"));
            if p.is_file() {
                return Ok(p);
            }
            searched.push(p);
        }
        if let Ok(path) = std::env::var("FFPROBE_PATH") {
            let p = PathBuf::from(path);
            if p.is_file() {
                return Ok(p);
            }
            searched.push(p);
        }
        if let Ok(current_exe) = std::env::current_exe() {
            if let Some(exe_dir) = current_exe.parent() {
                let p1 = exe_dir.join(binary_filename("ffprobe"));
                if p1.is_file() {
                    return Ok(p1);
                }
                searched.push(p1);
                let p2 = exe_dir.join("tools").join(binary_filename("ffprobe"));
                if p2.is_file() {
                    return Ok(p2);
                }
                searched.push(p2);
            }
        }
        let local_p1 = PathBuf::from(format!("./tools/{}", binary_filename("ffprobe")));
        if local_p1.is_file() {
            return Ok(local_p1);
        }
        searched.push(local_p1);

        if let Ok(path) = which::which(binary_filename("ffprobe")) {
            return Ok(path);
        }
        if let Ok(path) = which::which("ffprobe") {
            return Ok(path);
        }

        Err(MediaEngineError::ToolNotFound {
            tool_name: "ffprobe".to_string(),
            searched_paths: searched,
        })
    }

    /// Query capabilities of an FFmpeg binary.
    pub async fn detect_capabilities(path: &Path) -> Result<FfmpegCapabilities> {
        let mgr = ToolDiscoveryManager::new(EngineConfig::default());
        let tool = mgr.validate_ffmpeg(path).await?;
        tool.capabilities.ok_or_else(|| MediaEngineError::ToolExecutionFailed {
            tool_name: "ffmpeg".to_string(),
            exit_code: None,
            stderr_tail: "Falha ao obter capacidades do FFmpeg".to_string(),
            command_line: format!("{} -version", path.display()),
        })
    }
}

/// Return OS-specific executable filename.
fn binary_filename(base: &str) -> String {
    if cfg!(windows) {
        format!("{}.exe", base)
    } else {
        base.to_string()
    }
}

/// Helper function to run a command with timeout and NO WINDOW creation flags on Windows.
async fn run_command_with_timeout(
    exe: &Path,
    args: &[&str],
    timeout_secs: u64,
) -> Result<std::process::Output> {
    let mut cmd = tokio::process::Command::new(exe);
    cmd.args(args);
    cmd.stdout(std::process::Stdio::piped());
    cmd.stderr(std::process::Stdio::piped());

    #[cfg(windows)]
    {
        // 0x08000000 = CREATE_NO_WINDOW
        cmd.creation_flags(0x08000000);
    }

    let child = cmd.spawn().map_err(|e| MediaEngineError::ProcessSpawnError {
        command: exe.display().to_string(),
        source: e,
    })?;

    let timeout_duration = std::time::Duration::from_secs(timeout_secs);
    match tokio::time::timeout(timeout_duration, child.wait_with_output()).await {
        Ok(Ok(output)) => {
            if output.status.success() {
                Ok(output)
            } else {
                let stderr_tail = String::from_utf8_lossy(&output.stderr)
                    .lines()
                    .rev()
                    .take(10)
                    .collect::<Vec<_>>()
                    .into_iter()
                    .rev()
                    .collect::<Vec<_>>()
                    .join("\n");

                Err(MediaEngineError::ToolExecutionFailed {
                    tool_name: exe.display().to_string(),
                    exit_code: output.status.code(),
                    stderr_tail,
                    command_line: format!("{} {}", exe.display(), args.join(" ")),
                })
            }
        }
        Ok(Err(io_err)) => Err(MediaEngineError::Io {
            context: format!("Falha de E/S aguardando {}", exe.display()),
            source: io_err,
        }),
        Err(_) => Err(MediaEngineError::ToolTimeout {
            tool_name: exe.display().to_string(),
            path: exe.to_path_buf(),
            timeout_secs,
        }),
    }
}
