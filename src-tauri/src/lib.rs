mod commands;

pub use commands::*;
use tauri::Manager;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .setup(|app| {
            app.manage(commands::MediaEngineState::new());
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            commands::desktop_diagnostics,
            commands::scan_project_folder,
            commands::run_ffprobe,
            commands::save_project_state,
            commands::load_project_state,
            commands::save_project_file,
            commands::load_project_file,
            commands::get_recent_projects,
            commands::add_recent_project,
            commands::remove_recent_project,
            commands::select_folder_dialog,
            commands::select_file_dialog,
            commands::media_initialize,
            commands::media_get_capabilities,
            commands::media_probe,
            commands::media_scan_directory,
            commands::media_generate_thumbnail,
            commands::media_generate_filmstrip,
            commands::media_extract_waveform,
            commands::media_transcode,
            commands::media_cancel_job,
            commands::media_cancel_all_jobs,
            commands::media_get_job_status,
            commands::extract_audio_for_transcription,
            commands::run_transcription,
            commands::save_transcription,
            commands::load_transcription,
            commands::detect_scene_cuts,
            commands::save_media_segments,
            commands::load_media_segments,
        ])
        .run(tauri::generate_context!())
        .expect("error while running travelcut studio application");
}
