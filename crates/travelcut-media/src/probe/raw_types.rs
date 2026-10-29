//! Raw JSON structures matching FFprobe `-print_format json` output.
//!
//! Uses lenient deserialization with `#[serde(default)]` and `Option` fields
//! to handle variations across video, audio, image containers and camera models.

use serde::Deserialize;
use std::collections::HashMap;

/// Top-level FFprobe JSON output.
#[derive(Debug, Clone, Default, Deserialize)]
pub struct FfprobeOutput {
    #[serde(default)]
    pub streams: Vec<RawStream>,
    pub format: Option<RawFormat>,
    pub error: Option<RawError>,
}

#[derive(Debug, Clone, Default, Deserialize)]
pub struct RawError {
    pub code: Option<i32>,
    pub string: Option<String>,
}

#[derive(Debug, Clone, Default, Deserialize)]
pub struct RawFormat {
    pub filename: Option<String>,
    pub nb_streams: Option<u32>,
    pub format_name: Option<String>,
    pub format_long_name: Option<String>,
    pub start_time: Option<String>,
    pub duration: Option<String>,
    pub size: Option<String>,
    pub bit_rate: Option<String>,
    pub probe_score: Option<i32>,
    #[serde(default)]
    pub tags: HashMap<String, String>,
}

#[derive(Debug, Clone, Default, Deserialize)]
pub struct RawStream {
    pub index: usize,
    pub codec_name: Option<String>,
    pub codec_long_name: Option<String>,
    pub profile: Option<String>,
    pub codec_type: Option<String>,
    pub codec_tag_string: Option<String>,
    pub width: Option<u32>,
    pub height: Option<u32>,
    pub coded_width: Option<u32>,
    pub coded_height: Option<u32>,
    pub has_b_frames: Option<i32>,
    pub sample_aspect_ratio: Option<String>,
    pub display_aspect_ratio: Option<String>,
    pub pix_fmt: Option<String>,
    pub level: Option<i32>,
    pub color_range: Option<String>,
    pub color_space: Option<String>,
    pub color_transfer: Option<String>,
    pub color_primaries: Option<String>,
    pub r_frame_rate: Option<String>,
    pub avg_frame_rate: Option<String>,
    pub time_base: Option<String>,
    pub start_time: Option<String>,
    pub duration: Option<String>,
    pub bit_rate: Option<String>,
    pub nb_frames: Option<String>,
    // Audio specific fields
    pub sample_fmt: Option<String>,
    pub sample_rate: Option<String>,
    pub channels: Option<u16>,
    pub channel_layout: Option<String>,
    pub bits_per_sample: Option<u32>,
    #[serde(default)]
    pub side_data_list: Vec<RawSideData>,
    #[serde(default)]
    pub tags: HashMap<String, String>,
    #[serde(default)]
    pub disposition: HashMap<String, i32>,
}

#[derive(Debug, Clone, Default, Deserialize)]
pub struct RawSideData {
    pub side_data_type: Option<String>,
    pub rotation: Option<i32>,
}
