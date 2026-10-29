//! Media type detection and temporary/system file filtering based on path and extension.

use crate::models::MediaType;
use std::path::Path;

const VIDEO_EXTENSIONS: &[&str] = &[
    "mp4", "mov", "mkv", "avi", "webm", "m4v", "ts", "mts", "m2ts", "flv", "wmv", "3gp",
];

const AUDIO_EXTENSIONS: &[&str] = &[
    "wav", "mp3", "aac", "m4a", "flac", "ogg", "wma", "aiff", "opus",
];

const IMAGE_EXTENSIONS: &[&str] = &[
    "jpg", "jpeg", "png", "webp", "heic", "dng", "raw", "cr2", "cr3", "nef", "arw", "tiff", "bmp",
];

/// Check whether a file or directory is a system-generated or temporary artifact that should be ignored.
pub fn is_system_or_temporary_file(path: &Path) -> bool {
    let file_name = match path.file_name().and_then(|n| n.to_str()) {
        Some(name) => name,
        None => return true,
    };

    // Ignore hidden files and macOS resource fork sidecars
    if file_name.starts_with('.') || file_name.starts_with("._") {
        return true;
    }

    // Windows & macOS system metadata files
    let lower = file_name.to_lowercase();
    if lower == "thumbs.db"
        || lower == "desktop.ini"
        || lower == ".ds_store"
        || lower == "$recycle.bin"
        || lower == "system volume information"
    {
        return true;
    }

    // Temporary editor or download files
    if lower.ends_with(".tmp")
        || lower.ends_with(".crdownload")
        || lower.ends_with(".part")
        || lower.starts_with('~')
    {
        return true;
    }

    false
}

/// Detect preliminary media type by file extension.
pub fn detect_media_type_from_path(path: &Path) -> Option<MediaType> {
    let ext = path.extension()?.to_str()?.to_lowercase();

    if VIDEO_EXTENSIONS.contains(&ext.as_str()) {
        Some(MediaType::Video)
    } else if AUDIO_EXTENSIONS.contains(&ext.as_str()) {
        Some(MediaType::Audio)
    } else if IMAGE_EXTENSIONS.contains(&ext.as_str()) {
        Some(MediaType::Image)
    } else {
        None
    }
}
