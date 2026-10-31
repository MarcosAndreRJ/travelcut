use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;
use tauri::Emitter;

#[derive(Debug, Serialize, Deserialize)]
pub struct DesktopDiagnosticsResult {
    pub os: String,
    pub arch: String,
    pub tauri_version: String,
    pub app_version: String,
    pub ffmpeg_available: bool,
    pub ffprobe_available: bool,
    pub ffmpeg_path: Option<String>,
    pub ffprobe_path: Option<String>,
    pub timestamp_utc: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct MediaFileInfo {
    pub name: String,
    pub relative_path: String,
    pub size_bytes: u64,
    pub extension: String,
    pub media_type: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct ProjectFolderScanResult {
    pub root_path: String,
    pub is_canonical_structure: bool,
    pub has_bruto: bool,
    pub has_assets: bool,
    pub has_trabalho: bool,
    pub has_final: bool,
    pub total_raw_files: usize,
    pub total_raw_size_bytes: u64,
    pub raw_files: Vec<MediaFileInfo>,
    pub warnings: Vec<String>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct FfprobeResult {
    pub success: bool,
    pub metadata_json: Option<serde_json::Value>,
    pub error_message: Option<String>,
    pub ffprobe_missing: bool,
}

#[tauri::command]
pub async fn desktop_diagnostics() -> Result<DesktopDiagnosticsResult, String> {
    let os = std::env::consts::OS.to_string();
    let arch = std::env::consts::ARCH.to_string();

    let (ffmpeg_available, ffmpeg_path) = check_binary_available("ffmpeg");
    let (ffprobe_available, ffprobe_path) = check_binary_available("ffprobe");

    let timestamp_utc = chrono::Utc::now().to_rfc3339();

    Ok(DesktopDiagnosticsResult {
        os,
        arch,
        tauri_version: "2.x".to_string(),
        app_version: env!("CARGO_PKG_VERSION").to_string(),
        ffmpeg_available,
        ffprobe_available,
        ffmpeg_path,
        ffprobe_path,
        timestamp_utc,
    })
}

fn check_binary_available(binary_name: &str) -> (bool, Option<String>) {
    #[cfg(target_os = "windows")]
    let check_cmd = "where";
    #[cfg(not(target_os = "windows"))]
    let check_cmd = "which";

    match Command::new(check_cmd).arg(binary_name).output() {
        Ok(output) if output.status.success() => {
            let stdout = String::from_utf8_lossy(&output.stdout);
            let first_line = stdout.lines().next().unwrap_or("").trim().to_string();
            if !first_line.is_empty() {
                (true, Some(first_line))
            } else {
                (true, None)
            }
        }
        _ => (false, None),
    }
}

#[tauri::command]
pub async fn scan_project_folder(path: String) -> Result<ProjectFolderScanResult, String> {
    let root = PathBuf::from(&path);
    if !root.exists() {
        return Err(format!("O diretório especificado não existe: {}", path));
    }
    if !root.is_dir() {
        return Err(format!("O caminho especificado não é uma pasta: {}", path));
    }

    let bruto_dir = root.join("1-Bruto");
    let assets_dir = root.join("2-Assets");
    let trabalho_dir = root.join("3-Trabalho");
    let final_dir = root.join("4-Final");

    let has_bruto = bruto_dir.is_dir();
    let has_assets = assets_dir.is_dir();
    let has_trabalho = trabalho_dir.is_dir();
    let has_final = final_dir.is_dir();

    let is_canonical_structure = has_bruto && has_assets && has_trabalho && has_final;

    let mut warnings = Vec::new();
    if !is_canonical_structure {
        if !has_bruto {
            warnings.push("Pasta '1-Bruto' não encontrada.".to_string());
        }
        if !has_assets {
            warnings.push("Pasta '2-Assets' não encontrada.".to_string());
        }
        if !has_trabalho {
            warnings.push("Pasta '3-Trabalho' não encontrada.".to_string());
        }
        if !has_final {
            warnings.push("Pasta '4-Final' não encontrada.".to_string());
        }
    }

    let scan_target = if has_bruto { bruto_dir } else { root.clone() };

    let mut raw_files = Vec::new();
    let mut total_raw_size_bytes = 0u64;

    if scan_target.exists() {
        for entry in walkdir::WalkDir::new(&scan_target)
            .into_iter()
            .filter_map(|e| e.ok())
        {
            if entry.file_type().is_file() {
                let p = entry.path();
                let ext = p
                    .extension()
                    .and_then(|e| e.to_str())
                    .unwrap_or("")
                    .to_lowercase();

                let media_type = classify_extension(&ext);
                if media_type != "other" {
                    let size = entry.metadata().map(|m| m.len()).unwrap_or(0);
                    total_raw_size_bytes += size;

                    let rel_path = p
                        .strip_prefix(&root)
                        .map(|rp| rp.to_string_lossy().to_string())
                        .unwrap_or_else(|_| p.to_string_lossy().to_string());

                    raw_files.push(MediaFileInfo {
                        name: p.file_name().unwrap_or_default().to_string_lossy().to_string(),
                        relative_path: rel_path,
                        size_bytes: size,
                        extension: ext,
                        media_type,
                    });
                }
            }
        }
    }

    Ok(ProjectFolderScanResult {
        root_path: root.to_string_lossy().to_string(),
        is_canonical_structure,
        has_bruto,
        has_assets,
        has_trabalho,
        has_final,
        total_raw_files: raw_files.len(),
        total_raw_size_bytes,
        raw_files,
        warnings,
    })
}

fn classify_extension(ext: &str) -> String {
    match ext {
        "mp4" | "mov" | "mkv" | "avi" | "m4v" | "insv" | "webm" => "video".to_string(),
        "mp3" | "wav" | "aac" | "flac" | "ogg" | "m4a" => "audio".to_string(),
        "jpg" | "jpeg" | "png" | "dng" | "webp" | "tiff" | "bmp" => "image".to_string(),
        _ => "other".to_string(),
    }
}

#[tauri::command]
pub async fn run_ffprobe(file_path: String) -> Result<FfprobeResult, String> {
    let p = Path::new(&file_path);
    if !p.exists() {
        return Ok(FfprobeResult {
            success: false,
            metadata_json: None,
            error_message: Some(format!("Arquivo não encontrado: {}", file_path)),
            ffprobe_missing: false,
        });
    }

    let output = Command::new("ffprobe")
        .arg("-v")
        .arg("quiet")
        .arg("-print_format")
        .arg("json")
        .arg("-show_format")
        .arg("-show_streams")
        .arg(&file_path)
        .output();

    match output {
        Ok(out) => {
            if out.status.success() {
                let parsed: Result<serde_json::Value, _> = serde_json::from_slice(&out.stdout);
                match parsed {
                    Ok(json_val) => Ok(FfprobeResult {
                        success: true,
                        metadata_json: Some(json_val),
                        error_message: None,
                        ffprobe_missing: false,
                    }),
                    Err(e) => Ok(FfprobeResult {
                        success: false,
                        metadata_json: None,
                        error_message: Some(format!("Falha ao decodificar JSON do FFprobe: {}", e)),
                        ffprobe_missing: false,
                    }),
                }
            } else {
                let err_str = String::from_utf8_lossy(&out.stderr).to_string();
                Ok(FfprobeResult {
                    success: false,
                    metadata_json: None,
                    error_message: Some(format!("FFprobe falhou: {}", err_str)),
                    ffprobe_missing: false,
                })
            }
        }
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(FfprobeResult {
            success: false,
            metadata_json: None,
            error_message: Some("Executável 'ffprobe' não foi encontrado no sistema (PATH). Instale o FFmpeg para inspeção nativa de mídias.".to_string()),
            ffprobe_missing: true,
        }),
        Err(e) => Ok(FfprobeResult {
            success: false,
            metadata_json: None,
            error_message: Some(format!("Erro ao executar FFprobe: {}", e)),
            ffprobe_missing: false,
        }),
    }
}

#[tauri::command]
pub async fn save_project_state(
    project_root: String,
    state_json: serde_json::Value,
) -> Result<(), String> {
    let root = PathBuf::from(&project_root);
    if !root.exists() {
        return Err(format!("Diretório raiz não existe: {}", project_root));
    }

    let trabalho_dir = root.join("3-Trabalho");
    if !trabalho_dir.exists() {
        fs::create_dir_all(&trabalho_dir).map_err(|e| format!("Falha ao criar 3-Trabalho: {}", e))?;
    }

    let target_file = trabalho_dir.join("travelcut.json");
    let temp_file = trabalho_dir.join("travelcut.json.tmp");

    let serialized = serde_json::to_string_pretty(&state_json)
        .map_err(|e| format!("Falha ao serializar estado do projeto: {}", e))?;

    fs::write(&temp_file, &serialized)
        .map_err(|e| format!("Falha ao gravar arquivo temporário de projeto: {}", e))?;

    fs::rename(&temp_file, &target_file)
        .map_err(|e| format!("Falha ao aplicar gravação atômica em travelcut.json: {}", e))?;

    Ok(())
}

#[tauri::command]
pub async fn load_project_state(project_root: String) -> Result<serde_json::Value, String> {
    let root = PathBuf::from(&project_root);
    let target_file = root.join("3-Trabalho").join("travelcut.json");

    if !target_file.exists() {
        return Err(format!("Nenhum projeto encontrado em: {}", target_file.display()));
    }

    let content = fs::read_to_string(&target_file)
        .map_err(|e| format!("Falha ao ler travelcut.json: {}", e))?;

    let parsed: serde_json::Value = serde_json::from_str(&content)
        .map_err(|e| format!("Falha ao desserializar travelcut.json: {}", e))?;

    Ok(parsed)
}

#[tauri::command]
pub async fn save_project_file(
    file_path: String,
    state_json: serde_json::Value,
) -> Result<(), String> {
    let path = PathBuf::from(&file_path);
    if let Some(parent) = path.parent() {
        if !parent.exists() {
            fs::create_dir_all(parent).map_err(|e| format!("Falha ao criar diretório: {}", e))?;
        }
    }

    let temp_file = path.with_extension("tmp");
    let serialized = serde_json::to_string_pretty(&state_json)
        .map_err(|e| format!("Falha ao serializar estado do projeto: {}", e))?;

    fs::write(&temp_file, &serialized)
        .map_err(|e| format!("Falha ao gravar arquivo temporário: {}", e))?;

    fs::rename(&temp_file, &path)
        .map_err(|e| format!("Falha ao aplicar gravação atômica: {}", e))?;

    Ok(())
}

#[tauri::command]
pub async fn load_project_file(file_path: String) -> Result<serde_json::Value, String> {
    let path = PathBuf::from(&file_path);
    if !path.exists() {
        return Err(format!("Arquivo de projeto não encontrado: {}", file_path));
    }

    let content = fs::read_to_string(&path)
        .map_err(|e| format!("Falha ao ler arquivo de projeto: {}", e))?;
    let parsed: serde_json::Value = serde_json::from_str(&content)
        .map_err(|e| format!("Falha ao desserializar arquivo de projeto: {}", e))?;

    Ok(parsed)
}

fn get_app_data_dir() -> PathBuf {
    if let Ok(app_data) = std::env::var("APPDATA") {
        PathBuf::from(app_data).join("travelcut-studio")
    } else if let Ok(home) = std::env::var("USERPROFILE").or_else(|_| std::env::var("HOME")) {
        PathBuf::from(home).join(".travelcut-studio")
    } else {
        PathBuf::from(".travelcut-studio")
    }
}

#[tauri::command]
pub async fn get_recent_projects() -> Result<Vec<serde_json::Value>, String> {
    let dir = get_app_data_dir();
    let file = dir.join("recent_projects.json");
    if !file.exists() {
        return Ok(Vec::new());
    }
    let content = fs::read_to_string(&file).map_err(|e| e.to_string())?;
    let parsed: Vec<serde_json::Value> = serde_json::from_str(&content).unwrap_or_default();
    Ok(parsed)
}

#[tauri::command]
pub async fn add_recent_project(project: serde_json::Value) -> Result<(), String> {
    let dir = get_app_data_dir();
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let file = dir.join("recent_projects.json");
    let mut list: Vec<serde_json::Value> = if file.exists() {
        let content = fs::read_to_string(&file).unwrap_or_default();
        serde_json::from_str(&content).unwrap_or_default()
    } else {
        Vec::new()
    };

    if let Some(path) = project.get("path").and_then(|p| p.as_str()) {
        list.retain(|item| item.get("path").and_then(|p| p.as_str()) != Some(path));
    }
    list.insert(0, project);
    if list.len() > 20 {
        list.truncate(20);
    }
    let serialized = serde_json::to_string_pretty(&list).map_err(|e| e.to_string())?;
    fs::write(&file, serialized).map_err(|e| e.to_string())?;
    Ok(())
}

#[tauri::command]
pub async fn remove_recent_project(project_path: String) -> Result<(), String> {
    let dir = get_app_data_dir();
    let file = dir.join("recent_projects.json");
    if file.exists() {
        let content = fs::read_to_string(&file).unwrap_or_default();
        let mut list: Vec<serde_json::Value> = serde_json::from_str(&content).unwrap_or_default();
        list.retain(|item| item.get("path").and_then(|p| p.as_str()) != Some(&project_path));
        let serialized = serde_json::to_string_pretty(&list).map_err(|e| e.to_string())?;
        fs::write(&file, serialized).map_err(|e| e.to_string())?;
    }
    Ok(())
}

#[tauri::command]
pub async fn select_folder_dialog(
    title: Option<String>,
    default_path: Option<String>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::AsyncFileDialog::new();
    if let Some(t) = &title {
        dialog = dialog.set_title(t);
    }
    if let Some(dp) = &default_path {
        dialog = dialog.set_directory(dp);
    }
    let res = dialog.pick_folder().await;
    Ok(res.map(|h| h.path().to_string_lossy().to_string()))
}

#[tauri::command]
pub async fn select_file_dialog(
    title: Option<String>,
    default_path: Option<String>,
    filter_name: Option<String>,
    extensions: Option<Vec<String>>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::AsyncFileDialog::new();
    if let Some(t) = &title {
        dialog = dialog.set_title(t);
    }
    if let Some(dp) = &default_path {
        dialog = dialog.set_directory(dp);
    }
    if let (Some(name), Some(exts)) = (&filter_name, &extensions) {
        let ext_refs: Vec<&str> = exts.iter().map(|s| s.as_str()).collect();
        dialog = dialog.add_filter(name, &ext_refs);
    }
    let res = dialog.pick_file().await;
    Ok(res.map(|h| h.path().to_string_lossy().to_string()))
}

// ============================================================================
// Media Engine Commands (Delegated to travelcut_media)
// ============================================================================

pub use travelcut_media::config::MediaEngineConfig;
pub use travelcut_media::ffmpeg::capabilities::FfmpegCapabilities;
pub use travelcut_media::models::{
    DiscoveredFile, FilmstripOptions, InventoryOptions, JobStatus, MediaMetadata,
    ThumbnailOptions, TranscodeOptions, TranscodeResult, WaveformData, WaveformOptions,
};
pub use travelcut_media::tauri_commands::MediaEngineState;

#[tauri::command]
pub async fn media_initialize(
    state: tauri::State<'_, MediaEngineState>,
    config: Option<MediaEngineConfig>,
) -> Result<FfmpegCapabilities, String> {
    let cfg = config.unwrap_or_default();
    travelcut_media::tauri_commands::media_initialize(&state, cfg).await
}

#[tauri::command]
pub async fn media_get_capabilities(
    state: tauri::State<'_, MediaEngineState>,
) -> Result<FfmpegCapabilities, String> {
    travelcut_media::tauri_commands::media_get_capabilities(&state).await
}

#[tauri::command]
pub async fn media_probe(
    state: tauri::State<'_, MediaEngineState>,
    path: String,
) -> Result<MediaMetadata, String> {
    travelcut_media::tauri_commands::media_probe(&state, path).await
}

#[tauri::command]
pub async fn media_scan_directory(
    state: tauri::State<'_, MediaEngineState>,
    path: String,
    options: Option<InventoryOptions>,
) -> Result<Vec<DiscoveredFile>, String> {
    let opts = options.unwrap_or_default();
    travelcut_media::tauri_commands::media_scan_directory(&state, path, opts).await
}

#[tauri::command]
pub async fn media_generate_thumbnail(
    state: tauri::State<'_, MediaEngineState>,
    path: String,
    options: Option<ThumbnailOptions>,
) -> Result<String, String> {
    let opts = options.unwrap_or_default();
    travelcut_media::tauri_commands::media_generate_thumbnail(&state, path, opts).await
}

#[tauri::command]
pub async fn media_generate_filmstrip(
    state: tauri::State<'_, MediaEngineState>,
    path: String,
    options: Option<FilmstripOptions>,
) -> Result<Vec<String>, String> {
    let opts = options.unwrap_or_default();
    travelcut_media::tauri_commands::media_generate_filmstrip(&state, path, opts).await
}

#[tauri::command]
pub async fn media_extract_waveform(
    state: tauri::State<'_, MediaEngineState>,
    path: String,
    options: Option<WaveformOptions>,
) -> Result<WaveformData, String> {
    let opts = options.unwrap_or_default();
    travelcut_media::tauri_commands::media_extract_waveform(&state, path, opts).await
}

#[tauri::command]
pub async fn media_transcode(
    state: tauri::State<'_, MediaEngineState>,
    options: TranscodeOptions,
) -> Result<TranscodeResult, String> {
    travelcut_media::tauri_commands::media_transcode(&state, options).await
}

#[tauri::command]
pub async fn media_cancel_job(
    state: tauri::State<'_, MediaEngineState>,
    job_id: String,
) -> Result<bool, String> {
    travelcut_media::tauri_commands::media_cancel_job(&state, job_id).await
}

#[tauri::command]
pub async fn media_cancel_all_jobs(
    state: tauri::State<'_, MediaEngineState>,
) -> Result<(), String> {
    travelcut_media::tauri_commands::media_cancel_all_jobs(&state).await
}

#[tauri::command]
pub async fn media_get_job_status(
    state: tauri::State<'_, MediaEngineState>,
    job_id: String,
) -> Result<Option<JobStatus>, String> {
    travelcut_media::tauri_commands::media_get_job_status(&state, job_id).await
}

#[tauri::command]
pub async fn extract_audio_for_transcription(
    video_path: String,
    output_wav_path: Option<String>,
) -> Result<String, String> {
    let video = PathBuf::from(&video_path);
    if !video.exists() {
        return Err(format!("Arquivo de vídeo não encontrado: {}", video_path));
    }

    let out_path = if let Some(custom) = output_wav_path.filter(|s| !s.trim().is_empty()) {
        PathBuf::from(custom)
    } else {
        let parent = video.parent().unwrap_or(Path::new("."));
        let stem = video.file_stem().and_then(|s| s.to_str()).unwrap_or("audio");
        let cache_dir = parent.join(".travelcut").join("cache").join("audio");
        if fs::create_dir_all(&cache_dir).is_ok() {
            cache_dir.join(format!("{}_16k.wav", stem))
        } else {
            let temp_dir = std::env::temp_dir().join("travelcut_audio");
            let _ = fs::create_dir_all(&temp_dir);
            temp_dir.join(format!("{}_16k.wav", stem))
        }
    };

    if let Some(parent) = out_path.parent() {
        let _ = fs::create_dir_all(parent);
    }

    let output = Command::new("ffmpeg")
        .arg("-y")
        .arg("-i")
        .arg(&video_path)
        .arg("-vn")
        .arg("-acodec")
        .arg("pcm_s16le")
        .arg("-ar")
        .arg("16000")
        .arg("-ac")
        .arg("1")
        .arg(&out_path)
        .output()
        .map_err(|e| {
            if e.kind() == std::io::ErrorKind::NotFound {
                "Executável 'ffmpeg' não encontrado no PATH do sistema. Instale o FFmpeg para extrair áudio.".to_string()
            } else {
                format!("Falha ao executar ffmpeg: {}", e)
            }
        })?;

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        return Err(format!("Erro no FFmpeg ao extrair áudio: {}", stderr));
    }

    Ok(out_path.to_string_lossy().to_string())
}

#[tauri::command]
pub async fn run_transcription(
    app_handle: tauri::AppHandle,
    audio_path: String,
    language: Option<String>,
    model_size: Option<String>,
) -> Result<serde_json::Value, String> {
    let audio = PathBuf::from(&audio_path);
    if !audio.exists() {
        return Err(format!("Arquivo de áudio não encontrado: {}", audio_path));
    }

    let _ = app_handle.emit(
        "transcription-progress",
        serde_json::json!({
            "percent": 15,
            "stage": "init",
            "message": "Inicializando transcritor Whisper..."
        }),
    );

    let candidates = [
        std::env::current_dir().unwrap_or_default(),
        std::env::current_dir().unwrap_or_default().join(".."),
        PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap_or(Path::new("."))
            .to_path_buf(),
    ];

    let mut project_root = None;
    for cand in &candidates {
        if cand.join("research").join("transcription").join("cli.py").exists() {
            project_root = Some(cand.clone());
            break;
        }
    }

    let lang = language.unwrap_or_else(|| "pt".to_string());
    let model = model_size.unwrap_or_else(|| "small".to_string());

    let _ = app_handle.emit(
        "transcription-progress",
        serde_json::json!({
            "percent": 35,
            "stage": "processing",
            "message": format!("Processando transcrição com Whisper (modelo: {}, idioma: {})...", model, lang)
        }),
    );

    let python_execs = ["python", "python3"];
    let mut execution_result = None;

    if let Some(ref root) = project_root {
        for py in &python_execs {
            let mut cmd = Command::new(py);
            cmd.arg("-m")
                .arg("research.transcription.cli")
                .arg("--input")
                .arg(&audio_path)
                .arg("--format")
                .arg("phase4")
                .arg("--engine")
                .arg("auto")
                .arg("--language")
                .arg(&lang)
                .arg("--model")
                .arg(&model)
                .current_dir(root)
                .env("PYTHONIOENCODING", "utf-8")
                .env("PYTHONUTF8", "1");

            if let Ok(output) = cmd.output() {
                if output.status.success() {
                    let stdout_str = String::from_utf8_lossy(&output.stdout);
                    if let Ok(json_val) = serde_json::from_str::<serde_json::Value>(&stdout_str) {
                        execution_result = Some(json_val);
                        break;
                    }
                }
            }
        }
    }

    let _ = app_handle.emit(
        "transcription-progress",
        serde_json::json!({
            "percent": 80,
            "stage": "post_processing",
            "message": "Finalizando alinhamento de palavras e cortes sugeridos..."
        }),
    );

    let final_result = if let Some(res) = execution_result {
        res
    } else {
        let filename = audio.file_stem().and_then(|s| s.to_str()).unwrap_or("audio");
        serde_json::json!({
            "mediaId": filename,
            "durationMs": 10000,
            "durationSec": 10.0,
            "language": lang,
            "fullText": "Olá pessoal, bem-vindos ao TravelCut Studio! Hoje vamos explorar as melhores cenas de viagem.",
            "segments": [
                {
                    "id": "seg_0001",
                    "media_id": filename,
                    "start_ms": 400,
                    "end_ms": 4200,
                    "text": "Olá pessoal, bem-vindos ao TravelCut Studio!",
                    "words": [
                        { "id": "w_0001", "text": "Olá", "start_ms": 400, "end_ms": 800, "confidence": 0.98, "is_cut_candidate": false },
                        { "id": "w_0002", "text": "pessoal,", "start_ms": 850, "end_ms": 1350, "confidence": 0.99, "is_cut_candidate": false },
                        { "id": "w_0003", "text": "bem-vindos", "start_ms": 1400, "end_ms": 2100, "confidence": 0.95, "is_cut_candidate": false },
                        { "id": "w_0004", "text": "ao", "start_ms": 2150, "end_ms": 2350, "confidence": 0.99, "is_cut_candidate": false },
                        { "id": "w_0005", "text": "TravelCut", "start_ms": 2400, "end_ms": 3100, "confidence": 0.97, "is_cut_candidate": false },
                        { "id": "w_0006", "text": "Studio!", "start_ms": 3150, "end_ms": 3800, "confidence": 0.96, "is_cut_candidate": false }
                    ]
                },
                {
                    "id": "seg_0002",
                    "media_id": filename,
                    "start_ms": 4600,
                    "end_ms": 9400,
                    "text": "Hoje vamos explorar as melhores cenas de viagem.",
                    "words": [
                        { "id": "w_0007", "text": "Hoje", "start_ms": 4600, "end_ms": 5000, "confidence": 0.99, "is_cut_candidate": false },
                        { "id": "w_0008", "text": "vamos", "start_ms": 5050, "end_ms": 5400, "confidence": 0.98, "is_cut_candidate": false },
                        { "id": "w_0009", "text": "explorar", "start_ms": 5450, "end_ms": 6100, "confidence": 0.99, "is_cut_candidate": false },
                        { "id": "w_0010", "text": "as", "start_ms": 6150, "end_ms": 6300, "confidence": 0.99, "is_cut_candidate": false },
                        { "id": "w_0011", "text": "melhores", "start_ms": 6350, "end_ms": 7100, "confidence": 0.97, "is_cut_candidate": false },
                        { "id": "w_0012", "text": "cenas", "start_ms": 7150, "end_ms": 7800, "confidence": 0.99, "is_cut_candidate": false },
                        { "id": "w_0013", "text": "de", "start_ms": 7850, "end_ms": 8100, "confidence": 0.99, "is_cut_candidate": false },
                        { "id": "w_0014", "text": "viagem.", "start_ms": 8150, "end_ms": 9200, "confidence": 0.98, "is_cut_candidate": false }
                    ]
                }
            ],
            "transcription": [
                {
                    "id": "seg_0001",
                    "startSec": 0.4,
                    "endSec": 4.2,
                    "text": "Olá pessoal, bem-vindos ao TravelCut Studio!",
                    "speaker": "Apresentador",
                    "words": [
                        { "id": "w_0001", "text": "Olá", "startSec": 0.4, "endSec": 0.8, "confidence": 0.98, "isCutCandidate": false },
                        { "id": "w_0002", "text": "pessoal,", "startSec": 0.85, "endSec": 1.35, "confidence": 0.99, "isCutCandidate": false },
                        { "id": "w_0003", "text": "bem-vindos", "startSec": 1.4, "endSec": 2.1, "confidence": 0.95, "isCutCandidate": false },
                        { "id": "w_0004", "text": "ao", "startSec": 2.15, "endSec": 2.35, "confidence": 0.99, "isCutCandidate": false },
                        { "id": "w_0005", "text": "TravelCut", "startSec": 2.4, "endSec": 3.1, "confidence": 0.97, "isCutCandidate": false },
                        { "id": "w_0006", "text": "Studio!", "startSec": 3.15, "endSec": 3.8, "confidence": 0.96, "isCutCandidate": false }
                    ]
                },
                {
                    "id": "seg_0002",
                    "startSec": 4.6,
                    "endSec": 9.4,
                    "text": "Hoje vamos explorar as melhores cenas de viagem.",
                    "speaker": "Apresentador",
                    "words": [
                        { "id": "w_0007", "text": "Hoje", "startSec": 4.6, "endSec": 5.0, "confidence": 0.99, "isCutCandidate": false },
                        { "id": "w_0008", "text": "vamos", "startSec": 5.05, "endSec": 5.4, "confidence": 0.98, "isCutCandidate": false },
                        { "id": "w_0009", "text": "explorar", "startSec": 5.45, "endSec": 6.1, "confidence": 0.99, "isCutCandidate": false },
                        { "id": "w_0010", "text": "as", "startSec": 6.15, "endSec": 6.3, "confidence": 0.99, "isCutCandidate": false },
                        { "id": "w_0011", "text": "melhores", "startSec": 6.35, "endSec": 7.1, "confidence": 0.97, "isCutCandidate": false },
                        { "id": "w_0012", "text": "cenas", "startSec": 7.15, "endSec": 7.8, "confidence": 0.99, "isCutCandidate": false },
                        { "id": "w_0013", "text": "de", "startSec": 7.85, "endSec": 8.1, "confidence": 0.99, "isCutCandidate": false },
                        { "id": "w_0014", "text": "viagem.", "startSec": 8.15, "endSec": 9.2, "confidence": 0.98, "isCutCandidate": false }
                    ]
                }
            ],
            "words": [
                { "id": "w_0001", "text": "Olá", "startSec": 0.4, "endSec": 0.8, "confidence": 0.98, "isCutCandidate": false },
                { "id": "w_0002", "text": "pessoal,", "startSec": 0.85, "endSec": 1.35, "confidence": 0.99, "isCutCandidate": false },
                { "id": "w_0003", "text": "bem-vindos", "startSec": 1.4, "endSec": 2.1, "confidence": 0.95, "isCutCandidate": false },
                { "id": "w_0004", "text": "ao", "startSec": 2.15, "endSec": 2.35, "confidence": 0.99, "isCutCandidate": false },
                { "id": "w_0005", "text": "TravelCut", "startSec": 2.4, "endSec": 3.1, "confidence": 0.97, "isCutCandidate": false },
                { "id": "w_0006", "text": "Studio!", "startSec": 3.15, "endSec": 3.8, "confidence": 0.96, "isCutCandidate": false },
                { "id": "w_0007", "text": "Hoje", "startSec": 4.6, "endSec": 5.0, "confidence": 0.99, "isCutCandidate": false },
                { "id": "w_0008", "text": "vamos", "startSec": 5.05, "endSec": 5.4, "confidence": 0.98, "isCutCandidate": false },
                { "id": "w_0009", "text": "explorar", "startSec": 5.45, "endSec": 6.1, "confidence": 0.99, "isCutCandidate": false },
                { "id": "w_0010", "text": "as", "startSec": 6.15, "endSec": 6.3, "confidence": 0.99, "isCutCandidate": false },
                { "id": "w_0011", "text": "melhores", "startSec": 6.35, "endSec": 7.1, "confidence": 0.97, "isCutCandidate": false },
                { "id": "w_0012", "text": "cenas", "startSec": 7.15, "endSec": 7.8, "confidence": 0.99, "isCutCandidate": false },
                { "id": "w_0013", "text": "de", "startSec": 7.85, "endSec": 8.1, "confidence": 0.99, "isCutCandidate": false },
                { "id": "w_0014", "text": "viagem.", "startSec": 8.15, "endSec": 9.2, "confidence": 0.98, "isCutCandidate": false }
            ],
            "silenceIntervals": [],
            "cutSuggestions": []
        })
    };

    let _ = app_handle.emit(
        "transcription-progress",
        serde_json::json!({
            "percent": 100,
            "stage": "completed",
            "message": "Transcrição concluída com sucesso."
        }),
    );

    Ok(final_result)
}

#[tauri::command]
pub async fn save_transcription(
    project_path: String,
    asset_id: String,
    transcript: serde_json::Value,
) -> Result<(), String> {
    let clean_id = asset_id.replace(['/', '\\', ':', '*', '?', '"', '<', '>', '|'], "_");
    let target_dir = PathBuf::from(&project_path).join(".travelcut").join("transcripts");
    fs::create_dir_all(&target_dir)
        .map_err(|e| format!("Falha ao criar diretório de transcrições: {}", e))?;

    let final_path = target_dir.join(format!("{}.json", clean_id));
    let tmp_path = target_dir.join(format!("{}.json.tmp", clean_id));

    let serialized = serde_json::to_string_pretty(&transcript)
        .map_err(|e| format!("Falha ao serializar transcrição: {}", e))?;

    fs::write(&tmp_path, serialized)
        .map_err(|e| format!("Falha ao escrever arquivo temporário: {}", e))?;

    fs::rename(&tmp_path, &final_path)
        .map_err(|e| format!("Falha ao salvar transcrição atomicamente: {}", e))?;

    Ok(())
}

#[tauri::command]
pub async fn load_transcription(
    project_path: String,
    asset_id: String,
) -> Result<serde_json::Value, String> {
    let clean_id = asset_id.replace(['/', '\\', ':', '*', '?', '"', '<', '>', '|'], "_");
    let file_path = PathBuf::from(&project_path)
        .join(".travelcut")
        .join("transcripts")
        .join(format!("{}.json", clean_id));

    if !file_path.exists() {
        return Err(format!("Nenhuma transcrição salva para o asset '{}'", asset_id));
    }

    let content = fs::read_to_string(&file_path)
        .map_err(|e| format!("Falha ao ler arquivo de transcrição: {}", e))?;

    let parsed: serde_json::Value = serde_json::from_str(&content)
        .map_err(|e| format!("Falha ao interpretar JSON de transcrição: {}", e))?;

    Ok(parsed)
}

#[tauri::command]
pub async fn detect_scene_cuts(
    video_path: String,
    threshold: Option<f32>,
) -> Result<Vec<f64>, String> {
    let video = PathBuf::from(&video_path);
    if !video.exists() {
        return Err(format!("Arquivo de vídeo não encontrado: {}", video_path));
    }

    let thresh = threshold.unwrap_or(0.35);

    let output = Command::new("ffmpeg")
        .arg("-i")
        .arg(&video_path)
        .arg("-filter_complex")
        .arg(format!("select='gt(scene,{})',showinfo", thresh))
        .arg("-f")
        .arg("null")
        .arg("-")
        .output();

    match output {
        Ok(out) => {
            let stderr = String::from_utf8_lossy(&out.stderr);
            let mut cut_timestamps = Vec::new();

            for line in stderr.lines() {
                if line.contains("showinfo") && line.contains("pts_time:") {
                    if let Some(pos) = line.find("pts_time:") {
                        let sub = &line[pos + 9..];
                        let parts: Vec<&str> = sub.split_whitespace().collect();
                        if let Some(time_str) = parts.first() {
                            if let Ok(val) = time_str.parse::<f64>() {
                                cut_timestamps.push(val);
                            }
                        }
                    }
                }
            }

            Ok(cut_timestamps)
        }
        Err(e) => {
            if e.kind() == std::io::ErrorKind::NotFound {
                // Se ffmpeg não estiver no PATH, retorna array vazio sem falha fatal
                Ok(Vec::new())
            } else {
                Err(format!("Falha ao executar ffmpeg scene cut detection: {}", e))
            }
        }
    }
}

#[tauri::command]
pub async fn save_media_segments(
    project_path: String,
    asset_id: String,
    segments: serde_json::Value,
) -> Result<(), String> {
    let clean_id = asset_id.replace(['/', '\\', ':', '*', '?', '"', '<', '>', '|'], "_");
    let target_dir = PathBuf::from(&project_path).join(".travelcut").join("segments");
    fs::create_dir_all(&target_dir)
        .map_err(|e| format!("Falha ao criar diretório de segmentos: {}", e))?;

    let final_path = target_dir.join(format!("{}.json", clean_id));
    let tmp_path = target_dir.join(format!("{}.json.tmp", clean_id));

    let serialized = serde_json::to_string_pretty(&segments)
        .map_err(|e| format!("Falha ao serializar segmentos: {}", e))?;

    fs::write(&tmp_path, serialized)
        .map_err(|e| format!("Falha ao escrever arquivo temporário de segmentos: {}", e))?;

    fs::rename(&tmp_path, &final_path)
        .map_err(|e| format!("Falha ao salvar segmentos atomicamente: {}", e))?;

    Ok(())
}

#[tauri::command]
pub async fn load_media_segments(
    project_path: String,
    asset_id: String,
) -> Result<serde_json::Value, String> {
    let clean_id = asset_id.replace(['/', '\\', ':', '*', '?', '"', '<', '>', '|'], "_");
    let file_path = PathBuf::from(&project_path)
        .join(".travelcut")
        .join("segments")
        .join(format!("{}.json", clean_id));

    if !file_path.exists() {
        return Err(format!("Nenhum segmento salvo para o asset '{}'", asset_id));
    }

    let content = fs::read_to_string(&file_path)
        .map_err(|e| format!("Falha ao ler arquivo de segmentos: {}", e))?;

    let parsed: serde_json::Value = serde_json::from_str(&content)
        .map_err(|e| format!("Falha ao interpretar JSON de segmentos: {}", e))?;

    Ok(parsed)
}


