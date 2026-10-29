//! FFmpeg and FFprobe binary management, capability discovery, and process execution.

pub mod capabilities;
pub mod discovery;
pub mod process;

pub use capabilities::FfmpegCapabilities;
pub use discovery::{DiscoveredTool, ToolDiscoveryManager, ToolsetDiscovery};
pub use process::{normalize_windows_path, SafeProcessRunner};
