//! Recursive directory scanner for media asset discovery.
//!
//! Provides cooperative cancellation, non-blocking progress updates,
//! graceful permission-denied tolerance, and stable sorting.

use crate::error::{MediaEngineError, Result};
use crate::inventory::detector::{detect_media_type_from_path, is_system_or_temporary_file};
pub use crate::models::{
    CameraProfile, DiscoveredFile, FileCategory, InventoryOptions, InventoryProgress, MediaType,
};
use chrono::{DateTime, Utc};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use tracing::{debug, warn};
use walkdir::WalkDir;

/// Detect camera profile heuristics from filename / path structure
pub fn detect_camera_profile_from_path(path: &Path) -> Option<CameraProfile> {
    let name = path.file_name()?.to_str()?.to_uppercase();
    if name.starts_with("DJI_") {
        Some(CameraProfile::Dji)
    } else if name.starts_with("GOPR") || name.starts_with("GH") || name.starts_with("GX") {
        Some(CameraProfile::GoPro)
    } else if name.starts_with("DSC") || name.starts_with("C0") {
        Some(CameraProfile::Sony)
    } else if name.starts_with("IMG_") {
        Some(CameraProfile::Apple)
    } else {
        Some(CameraProfile::Generic)
    }
}

/// Classify media file category based on media type and extension
pub fn detect_file_category(media_type: MediaType, ext: &str) -> FileCategory {
    match media_type {
        MediaType::Video => FileCategory::Video,
        MediaType::Audio => FileCategory::Audio,
        MediaType::Image => {
            let raw_exts = ["dng", "cr2", "cr3", "nef", "arw", "raw", "rw2", "orf"];
            if raw_exts.contains(&ext) {
                FileCategory::Raw
            } else {
                FileCategory::Image
            }
        }
        MediaType::Unknown => {
            if ext == "xmp" || ext == "srt" || ext == "vtt" || ext == "thm" || ext == "lrf" {
                FileCategory::Sidecar
            } else {
                FileCategory::Unknown
            }
        }
    }
}

/// High-performance recursive media scanner.
#[derive(Debug, Clone)]
pub struct InventoryScanner {
    pub ffprobe_path: PathBuf,
    pub cache_dir: PathBuf,
    pub timeout_secs: u64,
}

impl InventoryScanner {
    pub fn new(ffprobe_path: PathBuf, cache_dir: PathBuf, timeout_secs: u64) -> Self {
        Self {
            ffprobe_path,
            cache_dir,
            timeout_secs,
        }
    }

    /// Scan a root directory asynchronously, reporting discovery progress.
    pub async fn scan_directory<F>(
        &self,
        dir: &Path,
        options: &InventoryOptions,
        cancel_flag: Option<Arc<AtomicBool>>,
        mut progress_cb: F,
    ) -> Result<Vec<DiscoveredFile>>
    where
        F: FnMut(&DiscoveredFile) + Send + 'static,
    {
        if !dir.exists() {
            return Err(MediaEngineError::InvalidPath {
                path: dir.to_path_buf(),
                reason: "Diretório para varredura não existe".to_string(),
            });
        }

        if !dir.is_dir() {
            return Err(MediaEngineError::InvalidPath {
                path: dir.to_path_buf(),
                reason: "Caminho fornecido não é um diretório".to_string(),
            });
        }

        let dir_owned = dir.to_path_buf();
        let options_clone = options.clone();
        let effective_cancel = cancel_flag.or_else(|| options.cancel_flag.clone());

        let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel::<DiscoveredFile>();

        // Run CPU/blocking filesystem traversal in a background worker thread
        let traversal_task = tokio::task::spawn_blocking(move || {
            let mut builder = WalkDir::new(&dir_owned).follow_links(false);
            if !options_clone.recursive {
                builder = builder.max_depth(1);
            } else if let Some(depth) = options_clone.max_depth {
                builder = builder.max_depth(depth);
            }

            let mut _files_scanned = 0u64;
            let mut results = Vec::new();

            for entry_res in builder {
                // Check cancellation flag
                if let Some(ref flag) = effective_cancel {
                    if flag.load(Ordering::Relaxed) {
                        debug!("Inventory scan cancelled cooperatively");
                        return Err(MediaEngineError::TaskCancelled {
                            job_id: "inventory_scan".to_string(),
                        });
                    }
                }

                let entry = match entry_res {
                    Ok(e) => e,
                    Err(err) => {
                        warn!(error = %err, "Permission denied or unreadable path skipped during scan");
                        continue;
                    }
                };

                let path = entry.path();
                if is_system_or_temporary_file(path) {
                    continue;
                }

                if entry.file_type().is_file() {
                    _files_scanned += 1;

                    if let Some(media_type) = detect_media_type_from_path(path) {
                        let allowed = match &options_clone.include_types {
                            Some(types) => types.contains(&media_type),
                            None => true,
                        };

                        if allowed {
                            let metadata = entry.metadata().ok();
                            let file_size_bytes = metadata.as_ref().map(|m| m.len()).unwrap_or(0);
                            let modified_at = metadata
                                .and_then(|m| m.modified().ok())
                                .map(DateTime::<Utc>::from);

                            let extension = path
                                .extension()
                                .and_then(|s| s.to_str())
                                .unwrap_or("")
                                .to_lowercase();

                            let category = detect_file_category(media_type, &extension);
                            let camera_profile = detect_camera_profile_from_path(path);

                            let discovered = DiscoveredFile {
                                path: path.to_path_buf(),
                                file_size_bytes,
                                size_bytes: file_size_bytes,
                                modified_at,
                                media_type,
                                category,
                                camera_profile,
                                extension,
                            };

                            let _ = tx.send(discovered.clone());
                            results.push(discovered);

                            // Check max files limit
                            if let Some(max) = options_clone.max_files {
                                if results.len() >= max {
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            Ok(results)
        });

        while let Some(item) = rx.recv().await {
            progress_cb(&item);
        }

        let mut final_results = traversal_task
            .await
            .map_err(|e| MediaEngineError::Io {
                context: "Falha na thread de varredura de diretório".to_string(),
                source: std::io::Error::other(e),
            })??;

        // Sort results
        if options.sort_by_date {
            final_results.sort_by(|a, b| match (&a.modified_at, &b.modified_at) {
                (Some(d_a), Some(d_b)) => d_a.cmp(d_b),
                (Some(_), None) => std::cmp::Ordering::Less,
                (None, Some(_)) => std::cmp::Ordering::Greater,
                (None, None) => a.path.cmp(&b.path),
            });
        } else {
            final_results.sort_by(|a, b| a.path.cmp(&b.path));
        }

        Ok(final_results)
    }
}
