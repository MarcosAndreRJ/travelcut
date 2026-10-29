//! Proxy profile definitions and intelligent hardware/software encoder selection.

use crate::ffmpeg::capabilities::FfmpegCapabilities;
use crate::models::HwAccelType;

/// Predefined standard proxy profiles for fluid timeline editing.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[allow(non_camel_case_types)]
pub enum StandardProxyProfile {
    /// 720p H.264 lightweight proxy (fast decode, CFR, minimal CPU overhead).
    H264_720pFast,
    /// 1080p H.264 proxy for high-resolution timeline viewing.
    H264_1080p,
    /// Apple ProRes Proxy (ProRes 422 Proxy profile 0) for zero CPU lag in NLEs.
    ProResProxy,
    /// 720p HEVC proxy when hardware encoding is preferred.
    Hevc_720p,
}

impl StandardProxyProfile {
    pub fn target_resolution(&self) -> (u32, u32) {
        match self {
            Self::H264_720pFast => (1280, 720),
            Self::H264_1080p => (1920, 1080),
            Self::ProResProxy => (1280, 720),
            Self::Hevc_720p => (1280, 720),
        }
    }
}

/// Helper for selecting the best available encoder given user preferences and hardware capabilities.
pub struct EncoderSelector;

impl EncoderSelector {
    /// Select video encoder name, hardware acceleration flags, and tuning parameters.
    pub fn select_video_encoder(
        preferred_codec: Option<&str>,
        hw_accel: HwAccelType,
        caps: &FfmpegCapabilities,
    ) -> SelectedEncoder {
        let codec = preferred_codec.unwrap_or("h264").to_lowercase();

        if codec == "prores" || codec == "prores_ks" {
            return SelectedEncoder {
                encoder_name: "prores_ks".to_string(),
                extra_args: vec![
                    "-profile:v".to_string(),
                    "0".to_string(), // ProRes 422 Proxy
                    "-vendor".to_string(),
                    "apl0".to_string(),
                    "-pix_fmt".to_string(),
                    "yuv422p10le".to_string(),
                ],
                hw_type: HwAccelType::None,
            };
        }

        let is_hevc = codec.contains("hevc") || codec.contains("h265") || codec.contains("265");

        // Try hardware encoders if requested and available
        match hw_accel {
            HwAccelType::Auto | HwAccelType::Cuda | HwAccelType::Nvenc => {
                let nvenc_name = if is_hevc { "hevc_nvenc" } else { "h264_nvenc" };
                if caps.has_encoder(nvenc_name) {
                    return SelectedEncoder {
                        encoder_name: nvenc_name.to_string(),
                        extra_args: vec![
                            "-preset".to_string(),
                            "p4".to_string(),
                            "-tune".to_string(),
                            "hq".to_string(),
                            "-rc".to_string(),
                            "vbr".to_string(),
                            "-pix_fmt".to_string(),
                            "yuv420p".to_string(),
                        ],
                        hw_type: HwAccelType::Cuda,
                    };
                }
            }
            HwAccelType::Qsv => {
                let qsv_name = if is_hevc { "hevc_qsv" } else { "h264_qsv" };
                if caps.has_encoder(qsv_name) {
                    return SelectedEncoder {
                        encoder_name: qsv_name.to_string(),
                        extra_args: vec![
                            "-preset".to_string(),
                            "veryfast".to_string(),
                            "-pix_fmt".to_string(),
                            "nv12".to_string(),
                        ],
                        hw_type: HwAccelType::Qsv,
                    };
                }
            }
            HwAccelType::Amf => {
                let amf_name = if is_hevc { "hevc_amf" } else { "h264_amf" };
                if caps.has_encoder(amf_name) {
                    return SelectedEncoder {
                        encoder_name: amf_name.to_string(),
                        extra_args: vec![
                            "-quality".to_string(),
                            "speed".to_string(),
                            "-pix_fmt".to_string(),
                            "yuv420p".to_string(),
                        ],
                        hw_type: HwAccelType::Amf,
                    };
                }
            }
            HwAccelType::VideoToolbox => {
                let vt_name = if is_hevc { "hevc_videotoolbox" } else { "h264_videotoolbox" };
                if caps.has_encoder(vt_name) {
                    return SelectedEncoder {
                        encoder_name: vt_name.to_string(),
                        extra_args: vec![
                            "-pix_fmt".to_string(),
                            "yuv420p".to_string(),
                        ],
                        hw_type: HwAccelType::VideoToolbox,
                    };
                }
            }
            HwAccelType::D3d11va | HwAccelType::Dxva2 => {
                // Windows hardware decode/encode
                let nvenc_name = if is_hevc { "hevc_nvenc" } else { "h264_nvenc" };
                if caps.has_encoder(nvenc_name) {
                    return SelectedEncoder {
                        encoder_name: nvenc_name.to_string(),
                        extra_args: vec![
                            "-preset".to_string(),
                            "p4".to_string(),
                            "-pix_fmt".to_string(),
                            "yuv420p".to_string(),
                        ],
                        hw_type: HwAccelType::Cuda,
                    };
                }
            }
            HwAccelType::None => {}
        }

        // Software fallback
        if is_hevc && caps.has_encoder("libx265") {
            SelectedEncoder {
                encoder_name: "libx265".to_string(),
                extra_args: vec![
                    "-preset".to_string(),
                    "veryfast".to_string(),
                    "-tag:v".to_string(),
                    "hvc1".to_string(),
                    "-pix_fmt".to_string(),
                    "yuv420p".to_string(),
                ],
                hw_type: HwAccelType::None,
            }
        } else {
            // Default universal libx264 software encoder
            SelectedEncoder {
                encoder_name: "libx264".to_string(),
                extra_args: vec![
                    "-preset".to_string(),
                    "veryfast".to_string(),
                    "-tune".to_string(),
                    "fastdecode".to_string(),
                    "-pix_fmt".to_string(),
                    "yuv420p".to_string(),
                ],
                hw_type: HwAccelType::None,
            }
        }
    }
}

/// Resolved encoder selection.
#[derive(Debug, Clone)]
pub struct SelectedEncoder {
    pub encoder_name: String,
    pub extra_args: Vec<String>,
    pub hw_type: HwAccelType,
}
