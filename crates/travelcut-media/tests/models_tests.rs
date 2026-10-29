use std::path::PathBuf;
use travelcut_media::config::EngineConfig;
use travelcut_media::models::{
    AudioStreamInfo, CameraProfile, ColorProfile, DiscoveredFile, FileCategory, HardwareAccel,
    ImageFormat, MediaMetadata, MediaType, ThumbnailOptions, VideoStreamInfo, WaveformData,
    WaveformPeak,
};

#[test]
fn test_config_default_and_serialization() {
    let config = EngineConfig::default();
    assert!(config.max_concurrent_jobs >= 1);
    assert!(config.protect_originals);
    assert_eq!(config.hardware_accel, HardwareAccel::Auto);

    let json = serde_json::to_string(&config).expect("Must serialize EngineConfig");
    let deserialized: EngineConfig =
        serde_json::from_str(&json).expect("Must deserialize EngineConfig");
    assert_eq!(config, deserialized);
}

#[test]
fn test_media_metadata_roundtrip() {
    let metadata = MediaMetadata {
        file_path: PathBuf::from("K:/Media/sample.mp4"),
        media_type: MediaType::Video,
        file_size_bytes: 1048576,
        format_name: "mov,mp4,m4a,3gp,3g2,mj2".to_string(),
        format_long_name: Some("QuickTime / MOV".to_string()),
        duration_secs: 42.5,
        duration_ms: 42500,
        overall_bitrate_bps: Some(2500000),
        video_stream: None,
        video: None,
        video_streams: vec![VideoStreamInfo {
            index: 0,
            codec_name: "h264".to_string(),
            codec_long_name: Some("H.264 / AVC".to_string()),
            profile: Some("High".to_string()),
            width: 1920,
            height: 1080,
            aspect_ratio: "16:9".to_string(),
            pixel_format: "yuv420p".to_string(),
            fps: 29.97,
            frame_count: Some(1274),
            bitrate_bps: Some(2300000),
            rotation: 0,
            color_profile: ColorProfile {
                color_space: Some("bt709".to_string()),
                color_transfer: Some("bt709".to_string()),
                color_primaries: Some("bt709".to_string()),
                is_hdr: false,
            },
        }],
        audio_stream: None,
        audio: None,
        audio_streams: vec![AudioStreamInfo {
            index: 1,
            codec_name: "aac".to_string(),
            codec_long_name: Some("AAC".to_string()),
            sample_rate: 48000,
            channels: 2,
            channel_layout: Some("stereo".to_string()),
            bitrate_bps: Some(192000),
            duration_secs: Some(42.5),
        }],
        subtitle_streams: vec![],
        camera_make: Some("Sony".to_string()),
        camera_model: Some("A7IV".to_string()),
        camera: None,
        gps: None,
        recorded_at: None,
        modified_at: None,
        tags: Default::default(),
    };

    let json = serde_json::to_string_pretty(&metadata).expect("Serialize MediaMetadata");
    let recovered: MediaMetadata =
        serde_json::from_str(&json).expect("Deserialize MediaMetadata");

    assert_eq!(recovered.file_path, metadata.file_path);
    assert_eq!(recovered.video_streams.len(), 1);
    assert_eq!(recovered.video_streams[0].width, 1920);
    assert_eq!(recovered.audio_streams.len(), 1);
    assert_eq!(recovered.audio_streams[0].channels, 2);
    assert_eq!(recovered.camera_make.as_deref(), Some("Sony"));
    assert_eq!(recovered.formatted_timecode(), "00:00:42.500");
}

#[test]
fn test_waveform_data_roundtrip() {
    let waveform = WaveformData {
        duration_secs: 10.0,
        channels: 2,
        peaks: vec![
            WaveformPeak {
                min: -0.5,
                max: 0.5,
                rms: 0.35,
            },
            WaveformPeak {
                min: -0.8,
                max: 0.9,
                rms: 0.65,
            },
        ],
        points_per_second: 50.0,
        overall_rms: 0.5,
        peak_max: 0.9,
    };

    let json = serde_json::to_string(&waveform).expect("Serialize WaveformData");
    let recovered: WaveformData = serde_json::from_str(&json).expect("Deserialize WaveformData");
    assert_eq!(recovered.peaks.len(), 2);
    assert_eq!(recovered.peaks[0].min, -0.5);
    assert_eq!(recovered.peaks[1].max, 0.9);
    assert_eq!(recovered.points_per_second, 50.0);
}

#[test]
fn test_thumbnail_options_default() {
    let opts = ThumbnailOptions::default();
    assert_eq!(opts.timestamp_secs, 1.0);
    assert_eq!(opts.width, 320);
    assert_eq!(opts.height, 180);
    assert_eq!(opts.format, ImageFormat::Jpeg);
    assert!(!opts.smart_offset);
}

#[test]
fn test_discovered_file() {
    let disc = DiscoveredFile {
        path: PathBuf::from("K:/Footage/DJI_001.MP4"),
        file_size_bytes: 52428800,
        size_bytes: 52428800,
        modified_at: None,
        media_type: MediaType::Video,
        category: FileCategory::Video,
        camera_profile: Some(CameraProfile::Dji),
        extension: "mp4".to_string(),
    };

    let json = serde_json::to_string(&disc).expect("Serialize DiscoveredFile");
    let recovered: DiscoveredFile =
        serde_json::from_str(&json).expect("Deserialize DiscoveredFile");
    assert_eq!(recovered.camera_profile, Some(CameraProfile::Dji));
    assert_eq!(recovered.category, FileCategory::Video);
}
