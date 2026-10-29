use std::path::Path;
use travelcut_media::inventory::detector::{
    detect_media_type_from_path, is_system_or_temporary_file,
};
use travelcut_media::models::MediaType;

#[test]
fn test_detect_media_type() {
    assert_eq!(
        detect_media_type_from_path(Path::new("clip.mp4")),
        Some(MediaType::Video)
    );
    assert_eq!(
        detect_media_type_from_path(Path::new("folder/movie.MOV")),
        Some(MediaType::Video)
    );
    assert_eq!(
        detect_media_type_from_path(Path::new("video.mkv")),
        Some(MediaType::Video)
    );
    assert_eq!(
        detect_media_type_from_path(Path::new("video.webm")),
        Some(MediaType::Video)
    );

    assert_eq!(
        detect_media_type_from_path(Path::new("song.mp3")),
        Some(MediaType::Audio)
    );
    assert_eq!(
        detect_media_type_from_path(Path::new("voice.WAV")),
        Some(MediaType::Audio)
    );
    assert_eq!(
        detect_media_type_from_path(Path::new("score.flac")),
        Some(MediaType::Audio)
    );
    assert_eq!(
        detect_media_type_from_path(Path::new("stream.opus")),
        Some(MediaType::Audio)
    );

    assert_eq!(
        detect_media_type_from_path(Path::new("photo.jpg")),
        Some(MediaType::Image)
    );
    assert_eq!(
        detect_media_type_from_path(Path::new("image.PNG")),
        Some(MediaType::Image)
    );
    assert_eq!(
        detect_media_type_from_path(Path::new("raw.dng")),
        Some(MediaType::Image)
    );
    assert_eq!(
        detect_media_type_from_path(Path::new("sony.arw")),
        Some(MediaType::Image)
    );

    assert_eq!(detect_media_type_from_path(Path::new("document.pdf")), None);
    assert_eq!(detect_media_type_from_path(Path::new("project.prproj")), None);
    assert_eq!(detect_media_type_from_path(Path::new("script.py")), None);
}

#[test]
fn test_system_and_temp_file_filtering() {
    // Hidden files
    assert!(is_system_or_temporary_file(Path::new(".DS_Store")));
    assert!(is_system_or_temporary_file(Path::new("._clip.mp4")));
    assert!(is_system_or_temporary_file(Path::new(".git")));

    // Windows system files
    assert!(is_system_or_temporary_file(Path::new("Thumbs.db")));
    assert!(is_system_or_temporary_file(Path::new("desktop.ini")));
    assert!(is_system_or_temporary_file(Path::new("$Recycle.Bin")));

    // Temporary editor and download files
    assert!(is_system_or_temporary_file(Path::new("download.crdownload")));
    assert!(is_system_or_temporary_file(Path::new("cache.tmp")));
    assert!(is_system_or_temporary_file(Path::new("video.part")));
    assert!(is_system_or_temporary_file(Path::new("~WRL0001.tmp")));

    // Legitimate media files
    assert!(!is_system_or_temporary_file(Path::new("DSC00123.MP4")));
    assert!(!is_system_or_temporary_file(Path::new("ambient_sound.wav")));
    assert!(!is_system_or_temporary_file(Path::new("portrait.jpg")));
}
