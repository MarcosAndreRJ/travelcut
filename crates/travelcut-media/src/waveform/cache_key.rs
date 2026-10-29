//! Deterministic cache key generation for audio waveforms.

use sha2::{Digest, Sha256};
use std::path::Path;

/// Generate a deterministic cache key for waveform data.
pub fn compute_waveform_cache_key(
    source_path: &Path,
    source_size_bytes: u64,
    source_modified_ms: i64,
    points_per_second: u32,
) -> String {
    let mut hasher = Sha256::new();
    hasher.update(source_path.to_string_lossy().as_bytes());
    hasher.update(source_size_bytes.to_le_bytes());
    hasher.update(source_modified_ms.to_le_bytes());
    hasher.update(points_per_second.to_le_bytes());

    let result = hasher.finalize();
    format!("{:x}", result)
}

/// Generate a deterministic cache key for a visual waveform image (PNG).
pub fn compute_waveform_image_cache_key(
    source_path: &Path,
    source_size_bytes: u64,
    source_modified_ms: i64,
    width: u32,
    height: u32,
) -> String {
    let mut hasher = Sha256::new();
    hasher.update(source_path.to_string_lossy().as_bytes());
    hasher.update(source_size_bytes.to_le_bytes());
    hasher.update(source_modified_ms.to_le_bytes());
    hasher.update(width.to_le_bytes());
    hasher.update(height.to_le_bytes());
    hasher.update(b"showwavespic_png");

    let result = hasher.finalize();
    format!("{:x}", result)
}
