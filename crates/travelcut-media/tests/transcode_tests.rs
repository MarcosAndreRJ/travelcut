use std::collections::HashSet;
use std::path::PathBuf;
use travelcut_media::ffmpeg::capabilities::FfmpegCapabilities;
use travelcut_media::models::{HwAccelType, TranscodeOptions};
use travelcut_media::transcode::command_builder::build_transcode_arguments;
use travelcut_media::transcode::profile::{EncoderSelector, StandardProxyProfile};

#[test]
fn test_standard_proxy_profiles() {
    assert_eq!(
        StandardProxyProfile::H264_720pFast.target_resolution(),
        (1280, 720)
    );
    assert_eq!(
        StandardProxyProfile::H264_1080p.target_resolution(),
        (1920, 1080)
    );
    assert_eq!(
        StandardProxyProfile::ProResProxy.target_resolution(),
        (1280, 720)
    );
}

#[test]
fn test_encoder_selection_fallback_to_software() {
    let empty_caps = FfmpegCapabilities {
        version: "6.1".into(),
        video_encoders: ["libx264".into(), "libx265".into()]
            .into_iter()
            .collect(),
        audio_encoders: ["aac".into()].into_iter().collect(),
        video_decoders: HashSet::new(),
        hwaccels: HashSet::new(),
    };

    // When Auto requested but no HW encoders exist, fall back to libx264
    let enc = EncoderSelector::select_video_encoder(Some("h264"), HwAccelType::Auto, &empty_caps);
    assert_eq!(enc.encoder_name, "libx264");
    assert_eq!(enc.hw_type, HwAccelType::None);

    // ProRes always maps to prores_ks
    let prores =
        EncoderSelector::select_video_encoder(Some("prores"), HwAccelType::None, &empty_caps);
    assert_eq!(prores.encoder_name, "prores_ks");
    assert_eq!(prores.hw_type, HwAccelType::None);
    assert!(prores.extra_args.contains(&"-profile:v".to_string()));
}

#[test]
fn test_encoder_selection_nvenc() {
    let nvenc_caps = FfmpegCapabilities {
        version: "6.1".into(),
        video_encoders: ["h264_nvenc".into(), "hevc_nvenc".into(), "libx264".into()]
            .into_iter()
            .collect(),
        audio_encoders: ["aac".into()].into_iter().collect(),
        video_decoders: HashSet::new(),
        hwaccels: ["cuda".into()].into_iter().collect(),
    };

    let enc = EncoderSelector::select_video_encoder(Some("h264"), HwAccelType::Cuda, &nvenc_caps);
    assert_eq!(enc.encoder_name, "h264_nvenc");
    assert_eq!(enc.hw_type, HwAccelType::Cuda);
}

#[test]
fn test_build_transcode_arguments() {
    let caps = FfmpegCapabilities {
        version: "6.1".into(),
        video_encoders: ["libx264".into()].into_iter().collect(),
        audio_encoders: ["aac".into()].into_iter().collect(),
        video_decoders: HashSet::new(),
        hwaccels: HashSet::new(),
    };

    let options = TranscodeOptions {
        source_path: PathBuf::from("test/source.mov"),
        output_path: PathBuf::from("test/output.mp4"),
        target_resolution: Some((1280, 720)),
        target_fps: Some(30.0),
        video_bitrate_kbps: Some(3500),
        crf: None,
        video_codec: Some("h264".into()),
        audio_bitrate_kbps: Some(192),
        hw_accel: HwAccelType::None,
        force_cfr: true,
        profile: None,
    };

    let (args, encoder) = build_transcode_arguments(&options, &caps);
    assert_eq!(encoder.encoder_name, "libx264");

    // Check essential flags
    assert!(args.contains(&"-i".to_string()));
    assert!(args.contains(&"-c:v".to_string()));
    assert!(args.contains(&"libx264".to_string()));
    assert!(args.contains(&"-b:v".to_string()));
    assert!(args.contains(&"3500k".to_string()));
    assert!(args.contains(&"-c:a".to_string()));
    assert!(args.contains(&"aac".to_string()));
    assert!(args.contains(&"-movflags".to_string()));
    assert!(args.contains(&"+faststart".to_string()));
}
