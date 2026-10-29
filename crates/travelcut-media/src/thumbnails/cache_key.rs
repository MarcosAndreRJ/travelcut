//! Deterministic cache key generation for thumbnails and filmstrips.
//!
//! Generates unique, stable hash keys incorporating file path, size,
//! modification timestamp, requested resolution, timestamp offset, and image format.
//! When a source file is modified or overwritten on disk, its cache key
//! changes automatically, avoiding stale cached images.

use sha2::{Digest, Sha256};
use std::path::Path;

/// Generate a deterministic cache key for a single thumbnail.
pub fn compute_thumbnail_cache_key(
    source_path: &Path,
    source_size_bytes: u64,
    source_modified_ms: i64,
    width: u32,
    height: u32,
    time_offset_secs: f64,
    format_ext: &str,
) -> String {
    let mut hasher = Sha256::new();
    hasher.update(source_path.to_string_lossy().as_bytes());
    hasher.update(source_size_bytes.to_le_bytes());
    hasher.update(source_modified_ms.to_le_bytes());
    hasher.update(width.to_le_bytes());
    hasher.update(height.to_le_bytes());
    hasher.update(format!("{:.3}", time_offset_secs).as_bytes());
    hasher.update(format_ext.as_bytes());

    let result = hasher.finalize();
    format!("{:x}", result)
}

/// Generate a deterministic cache key for a filmstrip series.
pub fn compute_filmstrip_cache_key(
    source_path: &Path,
    source_size_bytes: u64,
    source_modified_ms: i64,
    width: u32,
    height: u32,
    frame_count: usize,
    format_ext: &str,
) -> String {
    let mut hasher = Sha256::new();
    hasher.update(source_path.to_string_lossy().as_bytes());
    hasher.update(source_size_bytes.to_le_bytes());
    hasher.update(source_modified_ms.to_le_bytes());
    hasher.update(width.to_le_bytes());
    hasher.update(height.to_le_bytes());
    hasher.update(frame_count.to_le_bytes());
    hasher.update(format_ext.as_bytes());

    let result = hasher.finalize();
    format!("{:x}", result)
}
