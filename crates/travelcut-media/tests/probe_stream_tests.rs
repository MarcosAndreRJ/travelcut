use std::collections::HashMap;
use travelcut_media::probe::raw_types::{RawSideData, RawStream};
use travelcut_media::probe::stream::{
    compute_aspect_ratio, extract_color_profile, extract_rotation, parse_audio_stream,
    parse_rational, parse_subtitle_stream, parse_video_stream,
};

#[test]
fn test_parse_rational_framerate() {
    // Standard fractional rates
    let fps_24 = parse_rational("24000/1001").unwrap();
    assert!((fps_24 - 23.976).abs() < 0.001);

    let fps_30 = parse_rational("30000/1001").unwrap();
    assert!((fps_30 - 29.97).abs() < 0.01);

    let fps_60 = parse_rational("60000/1001").unwrap();
    assert!((fps_60 - 59.94).abs() < 0.01);

    // Integer rates expressed as fractions
    assert_eq!(parse_rational("24/1"), Some(24.0));
    assert_eq!(parse_rational("25/1"), Some(25.0));
    assert_eq!(parse_rational("60/1"), Some(60.0));

    // Decimal float
    assert_eq!(parse_rational("29.97"), Some(29.97));

    // Invalid / zero denominators
    assert_eq!(parse_rational("25/0"), None);
    assert_eq!(parse_rational("invalid"), None);
    assert_eq!(parse_rational(""), None);
}

#[test]
fn test_compute_aspect_ratio() {
    // Standard aspect ratios
    assert_eq!(compute_aspect_ratio(1920, 1080, None), "16:9");
    assert_eq!(compute_aspect_ratio(3840, 2160, None), "16:9");
    assert_eq!(compute_aspect_ratio(1080, 1920, None), "9:16");
    assert_eq!(compute_aspect_ratio(1280, 720, None), "16:9");
    assert_eq!(compute_aspect_ratio(640, 480, None), "4:3");
    assert_eq!(compute_aspect_ratio(1080, 1080, None), "1:1");

    // Display aspect ratio override
    assert_eq!(
        compute_aspect_ratio(1440, 1080, Some("16:9")),
        "16:9"
    );
    assert_eq!(
        compute_aspect_ratio(1440, 1080, Some("16/9")),
        "16:9"
    );

    // Degenerate values
    assert_eq!(compute_aspect_ratio(0, 0, None), "N/A");
}

#[test]
fn test_extract_rotation() {
    // 1. Rotation in side data
    let stream_with_side = RawStream {
        index: 0,
        codec_name: Some("h264".into()),
        codec_type: Some("video".into()),
        width: Some(1920),
        height: Some(1080),
        pix_fmt: Some("yuv420p".into()),
        r_frame_rate: Some("30/1".into()),
        avg_frame_rate: Some("30/1".into()),
        side_data_list: vec![RawSideData {
            side_data_type: Some("Display Matrix".into()),
            rotation: Some(-90),
        }],
        ..Default::default()
    };
    assert_eq!(extract_rotation(&stream_with_side), -90);

    // 2. Rotation in tags
    let mut tags = HashMap::new();
    tags.insert("rotate".into(), "180".into());
    let stream_with_tag = RawStream {
        side_data_list: vec![],
        tags,
        ..stream_with_side
    };
    assert_eq!(extract_rotation(&stream_with_tag), 180);
}

#[test]
fn test_extract_color_profile_hdr() {
    let hdr_stream = RawStream {
        index: 0,
        codec_name: Some("hevc".into()),
        codec_type: Some("video".into()),
        profile: Some("Main 10".into()),
        width: Some(3840),
        height: Some(2160),
        pix_fmt: Some("yuv420p10le".into()),
        color_space: Some("bt2020nc".into()),
        color_transfer: Some("smpte2084".into()), // PQ / HDR10
        color_primaries: Some("bt2020".into()),
        ..Default::default()
    };

    let profile = extract_color_profile(&hdr_stream);
    assert!(profile.is_hdr);
    assert_eq!(profile.color_transfer.as_deref(), Some("smpte2084"));
}

#[test]
fn test_parse_streams() {
    let mut tags = HashMap::new();
    tags.insert("language".into(), "por".into());
    tags.insert("title".into(), "Stereo Audio".into());

    let audio_raw = RawStream {
        index: 1,
        codec_name: Some("aac".into()),
        codec_long_name: Some("AAC (Advanced Audio Coding)".into()),
        codec_type: Some("audio".into()),
        profile: Some("LC".into()),
        bit_rate: Some("192000".into()),
        channels: Some(2),
        channel_layout: Some("stereo".into()),
        sample_rate: Some("48000".into()),
        sample_fmt: Some("fltp".into()),
        tags,
        ..Default::default()
    };

    let parsed_audio = parse_audio_stream(&audio_raw).expect("Should parse audio");
    assert_eq!(parsed_audio.index, 1);
    assert_eq!(parsed_audio.codec_name, "aac");
    assert_eq!(parsed_audio.channels, 2);
    assert_eq!(parsed_audio.channel_layout.as_deref(), Some("stereo"));
    assert_eq!(parsed_audio.sample_rate, 48000);
    assert_eq!(parsed_audio.bitrate_bps, Some(192000));

    // Ensure video parser returns None for audio stream
    assert!(parse_video_stream(&audio_raw).is_none());
    assert!(parse_subtitle_stream(&audio_raw).is_none());
}
