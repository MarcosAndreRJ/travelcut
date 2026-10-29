//! Configuration for the TravelCut media engine.
//!
//! Controls tool discovery paths, cache directories, concurrency limits,
//! hardware acceleration preferences, timeouts, and storage safety thresholds.

use crate::models::HardwareAccel;
use serde::{Deserialize, Serialize};
use std::path::PathBuf;

/// Complete engine configuration.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct EngineConfig {
    /// Explicit path to ffmpeg executable (overrides discovery).
    pub ffmpeg_path: Option<PathBuf>,

    /// Explicit path to ffprobe executable (overrides discovery).
    pub ffprobe_path: Option<PathBuf>,

    /// Optional dedicated directory containing ffmpeg and ffprobe binaries.
    pub tools_dir: Option<PathBuf>,

    /// Root directory for caching generated thumbnails, proxies, and extracted audio.
    pub cache_dir: PathBuf,

    /// Directory for intermediate/temporary processing files before atomic commit.
    pub temp_dir: PathBuf,

    /// Maximum number of parallel FFmpeg processes.
    pub max_concurrent_jobs: usize,

    /// Global process timeout in seconds (prevents zombie processes).
    pub process_timeout_secs: u64,

    /// Process timeout alias in seconds.
    pub timeout_secs: u64,

    /// Preferred hardware acceleration strategy.
    pub hardware_accel: HardwareAccel,

    /// Minimum required free disk space in bytes before launching export/proxy tasks.
    pub min_free_disk_space_bytes: u64,

    /// Whether to enforce strict read-only access to source media files.
    /// This is strictly maintained to guarantee source media safety.
    pub protect_originals: bool,
}

/// Type alias for backward compatibility.
pub type MediaEngineConfig = EngineConfig;

impl Default for EngineConfig {
    fn default() -> Self {
        let base_cache = std::env::var("LOCALAPPDATA")
            .map(PathBuf::from)
            .unwrap_or_else(|_| std::env::temp_dir())
            .join("TravelCutStudio")
            .join("cache");

        let base_temp = std::env::temp_dir()
            .join("TravelCutStudio")
            .join("temp");

        let default_concurrency = std::thread::available_parallelism()
            .map(|p| (p.get() / 2).clamp(1, 4))
            .unwrap_or(2);

        Self {
            ffmpeg_path: None,
            ffprobe_path: None,
            tools_dir: None,
            cache_dir: base_cache,
            temp_dir: base_temp,
            max_concurrent_jobs: default_concurrency,
            process_timeout_secs: 600, // 10 minutes default safety timeout
            timeout_secs: 600,
            hardware_accel: HardwareAccel::Auto,
            min_free_disk_space_bytes: 500 * 1024 * 1024, // 500 MB minimum threshold
            protect_originals: true,
        }
    }
}

impl EngineConfig {
    /// Builder pattern helper to set custom cache directory.
    pub fn with_cache_dir(mut self, path: impl Into<PathBuf>) -> Self {
        self.cache_dir = path.into();
        self
    }

    /// Builder pattern helper to set custom temporary directory.
    pub fn with_temp_dir(mut self, path: impl Into<PathBuf>) -> Self {
        self.temp_dir = path.into();
        self
    }

    /// Builder pattern helper to set explicit ffmpeg binary path.
    pub fn with_ffmpeg_path(mut self, path: impl Into<PathBuf>) -> Self {
        self.ffmpeg_path = Some(path.into());
        self
    }

    /// Builder pattern helper to set explicit ffprobe binary path.
    pub fn with_ffprobe_path(mut self, path: impl Into<PathBuf>) -> Self {
        self.ffprobe_path = Some(path.into());
        self
    }

    /// Builder pattern helper to set tools directory.
    pub fn with_tools_dir(mut self, path: impl Into<PathBuf>) -> Self {
        self.tools_dir = Some(path.into());
        self
    }

    /// Builder pattern helper to configure concurrency.
    pub fn with_concurrency(mut self, jobs: usize) -> Self {
        self.max_concurrent_jobs = jobs.max(1);
        self
    }
}
