//! # TravelCut Media Engine
//!
//! High-performance, production-grade media processing engine for TravelCut Studio.
//! Built in Rust, completely decoupled from frontend UI frameworks, providing:
//! - FFmpeg and FFprobe binary auto-discovery and validation.
//! - Deep metadata extraction (codecs, colorimetry, HDR, audio streams, camera metadata, GPS tags).
//! - Fast directory scanning for travel media with multi-threading and categorization.
//! - Parallel thumbnail and filmstrip generation with smart disk caching.
//! - Audio peak extraction (min/max/RMS) and visual waveform generation.
//! - Transcoding and proxy generation pipeline with hardware acceleration (NVENC, QSV, AMF).
//! - Priority-based job queue with cooperative cancellation and real-time telemetry.
//! - Desktop IPC bindings for Tauri integration and standalone CLI tool.

pub mod config;
pub mod engine;
pub mod error;
pub mod ffmpeg;
pub mod inventory;
pub mod models;
pub mod probe;
pub mod queue;
pub mod tauri_commands;
pub mod thumbnails;
pub mod transcode;
pub mod waveform;

// Re-exports of primary facade and types
pub use config::MediaEngineConfig;
pub use engine::MediaEngine;
pub use error::{MediaEngineError, Result};
pub use ffmpeg::capabilities::FfmpegCapabilities;
pub use ffmpeg::discovery::FfmpegDiscovery;
pub use models::*;
pub use queue::job::TrackedJob;
pub use queue::manager::JobQueueManager;
pub use tauri_commands::MediaEngineState;
