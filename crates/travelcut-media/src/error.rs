//! Structured error types for the TravelCut media engine.
//!
//! Provides rich, typed errors using `thiserror` with actionable diagnostics,
//! debug context, and user-facing messages.

use std::path::PathBuf;
use thiserror::Error;

/// Core error type representing any failure within the media engine.
#[derive(Debug, Error)]
pub enum MediaEngineError {
    /// FFmpeg or FFprobe executable was not found.
    #[error("Ferramenta necessária não encontrada: {tool_name}. Verifique se está instalada no PATH ou configure o caminho manualmente.")]
    ToolNotFound {
        tool_name: String,
        searched_paths: Vec<PathBuf>,
    },

    /// Tool execution returned a non-zero exit code or terminated with error.
    #[error("Execução de {tool_name} falhou com código de saída {exit_code:?}: {stderr_tail}")]
    ToolExecutionFailed {
        tool_name: String,
        exit_code: Option<i32>,
        stderr_tail: String,
        command_line: String,
    },

    /// Tool execution exceeded the configured timeout limit.
    #[error("Tempo limite ({timeout_secs}s) excedido durante execução de {tool_name} para {path}")]
    ToolTimeout {
        tool_name: String,
        path: PathBuf,
        timeout_secs: u64,
    },

    /// The requested path does not exist, is not accessible, or is invalid.
    #[error("Caminho inválido ou inacessível: {path}. Motivo: {reason}")]
    InvalidPath {
        path: PathBuf,
        reason: String,
    },

    /// The media container or stream codec is not supported for processing.
    #[error("Formato ou codec de mídia não suportado para {path}: {details}")]
    UnsupportedFormat {
        path: PathBuf,
        details: String,
    },

    /// The media file is truncated, unreadable, or corrupted.
    #[error("Mídia corrompida ou ilegível: {path}. Detalhes: {details}")]
    CorruptedMedia {
        path: PathBuf,
        details: String,
    },

    /// Disk space is insufficient for the requested operation.
    #[error("Espaço em disco insuficiente em {target_dir}. Necessário aprox. {required_bytes} bytes, disponível {available_bytes} bytes.")]
    InsufficientStorage {
        target_dir: PathBuf,
        required_bytes: u64,
        available_bytes: u64,
    },

    /// The operation was explicitly cancelled by the user or task supervisor.
    #[error("Operação cancelada pelo usuário ou supervisor de tarefas: job_id {job_id}")]
    TaskCancelled {
        job_id: String,
    },

    /// Cache failure during read, write, or integrity verification.
    #[error("Falha no subsistema de cache: {message}")]
    CacheError {
        message: String,
        path: Option<PathBuf>,
    },

    /// Failed to parse JSON output from FFprobe.
    #[error("Falha ao interpretar resposta JSON do FFprobe para {path}: {source}")]
    JsonParseError {
        path: PathBuf,
        source: serde_json::Error,
        raw_output_sample: String,
    },

    /// Failed to spawn child process due to an OS-level error.
    #[error("Falha do sistema operacional ao iniciar processo {command}: {source}")]
    ProcessSpawnError {
        command: String,
        source: std::io::Error,
    },

    /// General filesystem or I/O error.
    #[error("Erro de I/O em {context}: {source}")]
    Io {
        context: String,
        source: std::io::Error,
    },

    /// Configuration error.
    #[error("Configuração inválida do motor de mídia: {reason}")]
    Configuration {
        reason: String,
    },
}

impl MediaEngineError {
    /// Return a machine-readable error code for UI and IPC integration.
    pub fn error_code(&self) -> &'static str {
        match self {
            Self::ToolNotFound { .. } => "TOOL_NOT_FOUND",
            Self::ToolExecutionFailed { .. } => "TOOL_EXECUTION_FAILED",
            Self::ToolTimeout { .. } => "TOOL_TIMEOUT",
            Self::InvalidPath { .. } => "INVALID_PATH",
            Self::UnsupportedFormat { .. } => "UNSUPPORTED_FORMAT",
            Self::CorruptedMedia { .. } => "CORRUPTED_MEDIA",
            Self::InsufficientStorage { .. } => "INSUFFICIENT_STORAGE",
            Self::TaskCancelled { .. } => "TASK_CANCELLED",
            Self::CacheError { .. } => "CACHE_ERROR",
            Self::JsonParseError { .. } => "JSON_PARSE_ERROR",
            Self::ProcessSpawnError { .. } => "PROCESS_SPAWN_ERROR",
            Self::Io { .. } => "IO_ERROR",
            Self::Configuration { .. } => "CONFIGURATION_ERROR",
        }
    }

    /// Return affected file path if applicable.
    pub fn affected_path(&self) -> Option<&PathBuf> {
        match self {
            Self::ToolTimeout { path, .. }
            | Self::InvalidPath { path, .. }
            | Self::UnsupportedFormat { path, .. }
            | Self::CorruptedMedia { path, .. }
            | Self::JsonParseError { path, .. } => Some(path),
            Self::CacheError { path, .. } => path.as_ref(),
            _ => None,
        }
    }
}

/// Convenience result alias for media engine operations.
pub type Result<T> = std::result::Result<T, MediaEngineError>;
