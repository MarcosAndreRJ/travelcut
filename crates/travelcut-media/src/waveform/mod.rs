//! Audio waveform extraction, multi-resolution peak calculation, and rendering.

pub mod cache_key;
pub mod extractor;

pub use cache_key::{compute_waveform_cache_key, compute_waveform_image_cache_key};
pub use extractor::WaveformEngine;
