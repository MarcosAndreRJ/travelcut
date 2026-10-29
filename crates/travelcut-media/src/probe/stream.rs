//! Stream parsing, framerate calculation, and color profile extraction.

use crate::models::{
    AudioStreamInfo, ColorProfile, SubtitleStreamInfo, VideoStreamInfo,
};
use crate::probe::raw_types::RawStream;

/// Parse rational framerate string (e.g. "24000/1001", "30000/1001", "25/1", "60/1").
pub fn parse_rational(rat_str: &str) -> Option<f64> {
    let parts: Vec<&str> = rat_str.split('/').collect();
    if parts.len() == 2 {
        let num = parts[0].trim().parse::<f64>().ok()?;
        let den = parts[1].trim().parse::<f64>().ok()?;
        if den > 0.0 {
            let res = num / den;
            if res.is_finite() && res > 0.0 {
                return Some(res);
            }
        }
    } else if let Ok(val) = rat_str.trim().parse::<f64>() {
        if val.is_finite() && val > 0.0 {
            return Some(val);
        }
    }
    None
}

/// Compute greatest common divisor for aspect ratio simplification.
fn gcd(mut a: u32, mut b: u32) -> u32 {
    while b != 0 {
        let t = b;
        b = a % b;
        a = t;
    }
    a
}

/// Calculate simplified aspect ratio string (e.g. "16:9", "9:16", "4:3", "1:1").
pub fn compute_aspect_ratio(width: u32, height: u32, display_aspect: Option<&str>) -> String {
    if let Some(dar) = display_aspect {
        let trimmed = dar.trim();
        if !trimmed.is_empty() && trimmed != "0:1" && trimmed != "0/1" {
            return trimmed.replace('/', ":");
        }
    }

    if width == 0 || height == 0 {
        return "N/A".to_string();
    }

    let divisor = gcd(width, height);
    let num = width / divisor;
    let den = height / divisor;

    // Recognize standard video aspect ratios with slight tolerance for non-mod16 encodings
    let ratio = width as f64 / height as f64;
    if (ratio - 16.0 / 9.0).abs() < 0.015 {
        return "16:9".to_string();
    } else if (ratio - 9.0 / 16.0).abs() < 0.015 {
        return "9:16".to_string();
    } else if (ratio - 4.0 / 3.0).abs() < 0.015 {
        return "4:3".to_string();
    } else if (ratio - 1.0).abs() < 0.005 {
        return "1:1".to_string();
    } else if (ratio - 2.39).abs() < 0.03 {
        return "21:9".to_string();
    }

    format!("{}:{}", num, den)
}

/// Extract rotation degree from stream side data or tags.
pub fn extract_rotation(stream: &RawStream) -> i32 {
    // 1. Check side_data_list
    for side in &stream.side_data_list {
        if let Some(rot) = side.rotation {
            return rot;
        }
    }

    // 2. Check tags (e.g. QuickTime "rotate" tag)
    for (k, v) in &stream.tags {
        if k.eq_ignore_ascii_case("rotate") {
            if let Ok(rot) = v.parse::<i32>() {
                return rot;
            }
        }
    }

    0
}

/// Analyze color space and detect HDR (Rec.2020, PQ / SMPTE 2084, HLG / ARIB STD-B67).
pub fn extract_color_profile(stream: &RawStream) -> ColorProfile {
    let transfer = stream.color_transfer.clone();
    let primaries = stream.color_primaries.clone();
    let space = stream.color_space.clone();

    let mut is_hdr = false;

    if let Some(ref tr) = transfer {
        let tr_lower = tr.to_lowercase();
        if tr_lower.contains("smpte2084") // HDR10 / PQ
            || tr_lower.contains("arib-std-b67") // HLG
            || tr_lower.contains("linear")
        {
            is_hdr = true;
        }
    }

    if let Some(ref pr) = primaries {
        let pr_lower = pr.to_lowercase();
        if pr_lower.contains("bt2020") {
            // Rec.2020 wide color gamut
            if transfer.is_some() {
                is_hdr = true;
            }
        }
    }

    ColorProfile {
        color_space: space,
        color_transfer: transfer,
        color_primaries: primaries,
        is_hdr,
    }
}

/// Convert raw stream to VideoStreamInfo if video stream.
pub fn parse_video_stream(stream: &RawStream) -> Option<VideoStreamInfo> {
    if stream.codec_type.as_deref() != Some("video") {
        return None;
    }

    let width = stream.width.unwrap_or(0);
    let height = stream.height.unwrap_or(0);
    let aspect_ratio = compute_aspect_ratio(width, height, stream.display_aspect_ratio.as_deref());

    // Compute FPS: prefer avg_frame_rate, fallback to r_frame_rate
    let fps = stream
        .avg_frame_rate
        .as_deref()
        .and_then(parse_rational)
        .or_else(|| stream.r_frame_rate.as_deref().and_then(parse_rational))
        .unwrap_or(0.0);

    let frame_count = stream
        .nb_frames
        .as_deref()
        .and_then(|s| s.parse::<u64>().ok());

    let bitrate_bps = stream
        .bit_rate
        .as_deref()
        .and_then(|s| s.parse::<u64>().ok());

    let rotation = extract_rotation(stream);
    let color_profile = extract_color_profile(stream);

    Some(VideoStreamInfo {
        index: stream.index,
        codec_name: stream.codec_name.clone().unwrap_or_else(|| "unknown".to_string()),
        codec_long_name: stream.codec_long_name.clone(),
        profile: stream.profile.clone(),
        width,
        height,
        aspect_ratio,
        pixel_format: stream.pix_fmt.clone().unwrap_or_else(|| "unknown".to_string()),
        fps,
        frame_count,
        bitrate_bps,
        rotation,
        color_profile,
    })
}

/// Convert raw stream to AudioStreamInfo if audio stream.
pub fn parse_audio_stream(stream: &RawStream) -> Option<AudioStreamInfo> {
    if stream.codec_type.as_deref() != Some("audio") {
        return None;
    }

    let sample_rate = stream
        .sample_rate
        .as_deref()
        .and_then(|s| s.parse::<u32>().ok())
        .unwrap_or(48000);

    let channels = stream.channels.unwrap_or(2);

    let bitrate_bps = stream
        .bit_rate
        .as_deref()
        .and_then(|s| s.parse::<u64>().ok());

    let duration_secs = stream
        .duration
        .as_deref()
        .and_then(|s| s.parse::<f64>().ok());

    Some(AudioStreamInfo {
        index: stream.index,
        codec_name: stream.codec_name.clone().unwrap_or_else(|| "unknown".to_string()),
        codec_long_name: stream.codec_long_name.clone(),
        sample_rate,
        channels,
        channel_layout: stream.channel_layout.clone(),
        bitrate_bps,
        duration_secs,
    })
}

/// Convert raw stream to SubtitleStreamInfo if subtitle stream.
pub fn parse_subtitle_stream(stream: &RawStream) -> Option<SubtitleStreamInfo> {
    if stream.codec_type.as_deref() != Some("subtitle") {
        return None;
    }

    let mut language = None;
    let mut title = None;

    for (k, v) in &stream.tags {
        if k.eq_ignore_ascii_case("language") {
            language = Some(v.clone());
        } else if k.eq_ignore_ascii_case("title") {
            title = Some(v.clone());
        }
    }

    Some(SubtitleStreamInfo {
        index: stream.index,
        codec_name: stream.codec_name.clone().unwrap_or_else(|| "unknown".to_string()),
        language,
        title,
    })
}
