//! Capabilities and hardware acceleration inspection for FFmpeg.
//!
//! Queries the actual runtime binary to discover supported codecs,
//! hardware acceleration methods, and filters before attempting to use them.

use crate::models::HardwareAccel;
use serde::{Deserialize, Serialize};
use std::collections::HashSet;

/// Verified capabilities discovered from an executable FFmpeg binary.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize, Default)]
pub struct FfmpegCapabilities {
    /// Parsed version string (e.g. "ffmpeg version 7.0-full_build").
    pub version: String,
    /// Available hardware acceleration devices (e.g. "cuda", "d3d11va", "qsv", "amf").
    pub hwaccels: HashSet<String>,
    /// Available video encoders (e.g. "libx264", "h264_nvenc", "h264_qsv", "prores_ks").
    pub video_encoders: HashSet<String>,
    /// Available audio encoders (e.g. "aac", "pcm_s16le", "libmp3lame").
    pub audio_encoders: HashSet<String>,
    /// Available video decoders.
    pub video_decoders: HashSet<String>,
}

impl FfmpegCapabilities {
    /// Check whether a specific hardware acceleration method is supported by the binary.
    pub fn supports_hwaccel(&self, method: HardwareAccel) -> bool {
        match method {
            HardwareAccel::Auto | HardwareAccel::None => true,
            HardwareAccel::Cuda | HardwareAccel::Nvenc => {
                self.hwaccels.contains("cuda") || self.video_encoders.contains("h264_nvenc")
            }
            HardwareAccel::Qsv => {
                self.hwaccels.contains("qsv") || self.video_encoders.contains("h264_qsv")
            }
            HardwareAccel::Amf => {
                self.hwaccels.contains("amf") || self.video_encoders.contains("h264_amf")
            }
            HardwareAccel::D3d11va => self.hwaccels.contains("d3d11va"),
            HardwareAccel::Dxva2 => self.hwaccels.contains("dxva2"),
            HardwareAccel::VideoToolbox => {
                self.hwaccels.contains("videotoolbox")
                    || self.video_encoders.contains("h264_videotoolbox")
            }
        }
    }

    /// Check if a specific hardware accelerator device name is available.
    pub fn has_hw_accel(&self, name: &str) -> bool {
        self.hwaccels.contains(name)
    }

    /// Check if a specific encoder is present.
    pub fn has_encoder(&self, name: &str) -> bool {
        self.video_encoders.contains(name) || self.audio_encoders.contains(name)
    }

    /// Check if a specific decoder is present.
    pub fn has_decoder(&self, name: &str) -> bool {
        self.video_decoders.contains(name)
    }

    /// Select best available video encoder according to requested hardware acceleration.
    /// Falls back safely to software encoder (e.g. "libx264") if hardware is absent.
    pub fn select_h264_encoder(&self, preference: HardwareAccel) -> (&'static str, Option<&'static str>) {
        match preference {
            HardwareAccel::Nvenc if self.supports_hwaccel(HardwareAccel::Nvenc) => {
                ("h264_nvenc", Some("cuda"))
            }
            HardwareAccel::Qsv if self.supports_hwaccel(HardwareAccel::Qsv) => {
                ("h264_qsv", Some("qsv"))
            }
            HardwareAccel::Amf if self.supports_hwaccel(HardwareAccel::Amf) => {
                ("h264_amf", None)
            }
            HardwareAccel::D3d11va if self.supports_hwaccel(HardwareAccel::D3d11va) => {
                if self.video_encoders.contains("h264_nvenc") {
                    ("h264_nvenc", Some("d3d11va"))
                } else if self.video_encoders.contains("h264_qsv") {
                    ("h264_qsv", Some("d3d11va"))
                } else {
                    ("libx264", None)
                }
            }
            HardwareAccel::Auto => {
                if self.supports_hwaccel(HardwareAccel::Nvenc) {
                    ("h264_nvenc", Some("cuda"))
                } else if self.supports_hwaccel(HardwareAccel::Qsv) {
                    ("h264_qsv", Some("qsv"))
                } else if self.supports_hwaccel(HardwareAccel::Amf) {
                    ("h264_amf", None)
                } else {
                    ("libx264", None)
                }
            }
            _ => ("libx264", None),
        }
    }

    /// Check if ProRes encoding is supported.
    pub fn supports_prores(&self) -> bool {
        self.video_encoders.contains("prores_ks") || self.video_encoders.contains("prores")
    }
}
