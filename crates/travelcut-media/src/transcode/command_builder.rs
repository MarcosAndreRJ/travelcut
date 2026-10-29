//! Builder for FFmpeg transcoding argument lists.

use crate::ffmpeg::capabilities::FfmpegCapabilities;
use crate::ffmpeg::process::normalize_windows_path;
use crate::models::{HwAccelType, TranscodeOptions};
use crate::transcode::profile::{EncoderSelector, SelectedEncoder};

/// Construct the complete argument vector for an FFmpeg transcode execution.
pub fn build_transcode_arguments(
    options: &TranscodeOptions,
    caps: &FfmpegCapabilities,
) -> (Vec<String>, SelectedEncoder) {
    let mut args = Vec::new();

    let selected_encoder = EncoderSelector::select_video_encoder(
        options.video_codec.as_deref(),
        options.hw_accel,
        caps,
    );

    // Hardware acceleration input decode flag
    if selected_encoder.hw_type == HwAccelType::Cuda && caps.has_hw_accel("cuda") {
        args.push("-hwaccel".to_string());
        args.push("cuda".to_string());
    } else if selected_encoder.hw_type == HwAccelType::Qsv && caps.has_hw_accel("qsv") {
        args.push("-hwaccel".to_string());
        args.push("qsv".to_string());
    }

    // Input file
    let normalized_source = normalize_windows_path(&options.source_path);
    args.push("-i".to_string());
    args.push(normalized_source.to_string_lossy().to_string());

    // Video filters: scaling and constant framerate
    let mut vf_filters = Vec::new();

    if let Some((w, h)) = options.target_resolution {
        // Scale down maintaining aspect ratio, and ensure even dimensions (divisible by 2) for YUV420p
        vf_filters.push(format!(
            "scale={}:{}:force_original_aspect_ratio=decrease,pad=ceil(iw/2)*2:ceil(ih/2)*2",
            w, h
        ));
    }

    if let Some(fps) = options.target_fps {
        vf_filters.push(format!("fps={:.3}", fps));
    }

    if !vf_filters.is_empty() {
        args.push("-vf".to_string());
        args.push(vf_filters.join(","));
    }

    // Video codec
    args.push("-c:v".to_string());
    args.push(selected_encoder.encoder_name.clone());

    // Encoder-specific quality/bitrate
    if let Some(bitrate) = options.video_bitrate_kbps {
        args.push("-b:v".to_string());
        args.push(format!("{}k", bitrate));
    } else if let Some(crf) = options.crf {
        if selected_encoder.encoder_name.contains("nvenc") {
            args.push("-cq".to_string());
            args.push(crf.to_string());
        } else if !selected_encoder.encoder_name.contains("prores") {
            args.push("-crf".to_string());
            args.push(crf.to_string());
        }
    } else if !selected_encoder.encoder_name.contains("prores") {
        // Default proxy CRF
        args.push("-crf".to_string());
        args.push("23".to_string());
    }

    // Extra encoder parameters (presets, profiles, tunes)
    for arg in &selected_encoder.extra_args {
        args.push(arg.clone());
    }

    // Audio encoding: standard AAC 128k 48kHz stereo
    args.push("-c:a".to_string());
    args.push("aac".to_string());
    args.push("-b:a".to_string());
    args.push(format!("{}k", options.audio_bitrate_kbps.unwrap_or(128)));
    args.push("-ar".to_string());
    args.push("48000".to_string());
    args.push("-ac".to_string());
    args.push("2".to_string());

    // Container optimizations for NLE scrubbing & fast web streaming
    args.push("-movflags".to_string());
    args.push("+faststart".to_string());

    if options.force_cfr {
        args.push("-fps_mode".to_string());
        args.push("cfr".to_string());
    }

    // Output progress to stdout pipe
    args.push("-progress".to_string());
    args.push("pipe:1".to_string());

    // Overwrite output
    args.push("-y".to_string());

    let normalized_output = normalize_windows_path(&options.output_path);
    args.push(normalized_output.to_string_lossy().to_string());

    (args, selected_encoder)
}
