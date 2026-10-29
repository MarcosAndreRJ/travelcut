use std::path::Path;
use travelcut_media::thumbnails::cache_key::{
    compute_filmstrip_cache_key, compute_thumbnail_cache_key,
};
use travelcut_media::waveform::cache_key::{
    compute_waveform_cache_key, compute_waveform_image_cache_key,
};

#[test]
fn test_thumbnail_cache_key_determinism() {
    let path = Path::new("test/video.mp4");
    let key1 = compute_thumbnail_cache_key(path, 1024, 1600000000, 320, 180, 5.0, "jpg");
    let key2 = compute_thumbnail_cache_key(path, 1024, 1600000000, 320, 180, 5.0, "jpg");
    assert_eq!(key1, key2);
    assert_eq!(key1.len(), 64); // SHA-256 hex string is 64 characters
}

#[test]
fn test_thumbnail_cache_key_uniqueness() {
    let path = Path::new("test/video.mp4");
    let base = compute_thumbnail_cache_key(path, 1024, 1600000000, 320, 180, 5.0, "jpg");

    // Different path
    let diff_path = compute_thumbnail_cache_key(
        Path::new("test/other.mp4"),
        1024,
        1600000000,
        320,
        180,
        5.0,
        "jpg",
    );
    assert_ne!(base, diff_path);

    // Different size
    let diff_size = compute_thumbnail_cache_key(path, 2048, 1600000000, 320, 180, 5.0, "jpg");
    assert_ne!(base, diff_size);

    // Different mtime
    let diff_mtime = compute_thumbnail_cache_key(path, 1024, 1700000000, 320, 180, 5.0, "jpg");
    assert_ne!(base, diff_mtime);

    // Different dimensions
    let diff_dim = compute_thumbnail_cache_key(path, 1024, 1600000000, 640, 360, 5.0, "jpg");
    assert_ne!(base, diff_dim);

    // Different timestamp offset
    let diff_offset = compute_thumbnail_cache_key(path, 1024, 1600000000, 320, 180, 10.0, "jpg");
    assert_ne!(base, diff_offset);

    // Different format
    let diff_format = compute_thumbnail_cache_key(path, 1024, 1600000000, 320, 180, 5.0, "png");
    assert_ne!(base, diff_format);
}

#[test]
fn test_filmstrip_cache_key() {
    let path = Path::new("project/travel.mov");
    let key1 = compute_filmstrip_cache_key(path, 5000000, 1650000000, 160, 90, 20, "jpg");
    let key2 = compute_filmstrip_cache_key(path, 5000000, 1650000000, 160, 90, 20, "jpg");
    assert_eq!(key1, key2);

    let diff_count = compute_filmstrip_cache_key(path, 5000000, 1650000000, 160, 90, 10, "jpg");
    assert_ne!(key1, diff_count);
}

#[test]
fn test_waveform_cache_keys() {
    let path = Path::new("audio/track.wav");
    let data_key1 = compute_waveform_cache_key(path, 80000, 1660000000, 50);
    let data_key2 = compute_waveform_cache_key(path, 80000, 1660000000, 50);
    assert_eq!(data_key1, data_key2);

    let diff_rate = compute_waveform_cache_key(path, 80000, 1660000000, 100);
    assert_ne!(data_key1, diff_rate);

    let img_key1 = compute_waveform_image_cache_key(path, 80000, 1660000000, 800, 120);
    let img_key2 = compute_waveform_image_cache_key(path, 80000, 1660000000, 800, 120);
    assert_eq!(img_key1, img_key2);
    assert_ne!(data_key1, img_key1);
}
