//! Standalone CLI utility for TravelCut Studio Media Engine testing and automation.

use clap::{Parser, Subcommand, ValueEnum};
use std::path::PathBuf;
use travelcut_media::config::MediaEngineConfig;
use travelcut_media::engine::MediaEngine;
use travelcut_media::models::{
    FilmstripOptions, HwAccelType, InventoryOptions, ThumbnailOptions, TranscodeOptions,
    WaveformOptions,
};

#[derive(Parser, Debug)]
#[command(
    name = "travelcut-media-cli",
    version = "0.1.0",
    about = "TravelCut Studio Media Engine CLI utility"
)]
struct Cli {
    #[arg(long, help = "Custom path to ffmpeg binary")]
    ffmpeg: Option<PathBuf>,

    #[arg(long, help = "Custom path to ffprobe binary")]
    ffprobe: Option<PathBuf>,

    #[arg(long, help = "Custom cache directory")]
    cache_dir: Option<PathBuf>,

    #[command(subcommand)]
    command: Commands,
}

#[derive(ValueEnum, Clone, Copy, Debug)]
enum CliHwAccel {
    Auto,
    Cuda,
    Qsv,
    Amf,
    D3d11va,
    Dxva2,
    None,
}

impl From<CliHwAccel> for HwAccelType {
    fn from(c: CliHwAccel) -> Self {
        match c {
            CliHwAccel::Auto => HwAccelType::Auto,
            CliHwAccel::Cuda => HwAccelType::Cuda,
            CliHwAccel::Qsv => HwAccelType::Qsv,
            CliHwAccel::Amf => HwAccelType::Amf,
            CliHwAccel::D3d11va => HwAccelType::D3d11va,
            CliHwAccel::Dxva2 => HwAccelType::Dxva2,
            CliHwAccel::None => HwAccelType::None,
        }
    }
}

#[derive(Subcommand, Debug)]
enum Commands {
    /// Detect and list FFmpeg capabilities, available hardware encoders, and decoders
    Capabilities {
        #[arg(long, help = "Output in JSON format")]
        json: bool,
    },

    /// Extract comprehensive metadata from a media file
    Probe {
        #[arg(help = "Path to the media file")]
        path: PathBuf,

        #[arg(long, help = "Output in JSON format")]
        json: bool,
    },

    /// Scan a directory for media assets and classify by camera/type
    Scan {
        #[arg(help = "Directory path to scan")]
        path: PathBuf,

        #[arg(long, default_value = "true", help = "Scan recursively")]
        recursive: bool,

        #[arg(long, help = "Extract deep metadata during scan")]
        deep: bool,

        #[arg(long, help = "Output in JSON format")]
        json: bool,
    },

    /// Generate a single video thumbnail
    Thumb {
        #[arg(help = "Source video file path")]
        path: PathBuf,

        #[arg(short, long, help = "Target output image path")]
        output: Option<PathBuf>,

        #[arg(short, long, default_value_t = 1.0, help = "Timestamp in seconds")]
        time: f64,

        #[arg(short, long, default_value_t = 320, help = "Target width")]
        width: u32,

        #[arg(short = 'H', long, default_value_t = 180, help = "Target height")]
        height: u32,
    },

    /// Generate a filmstrip thumbnail series
    Filmstrip {
        #[arg(help = "Source video file path")]
        path: PathBuf,

        #[arg(short, long, help = "Target output directory for frame images")]
        output_dir: PathBuf,

        #[arg(short, long, default_value_t = 10, help = "Number of thumbnails to sample")]
        count: usize,

        #[arg(short, long, default_value_t = 160, help = "Thumbnail width")]
        width: u32,

        #[arg(short = 'H', long, default_value_t = 90, help = "Thumbnail height")]
        height: u32,
    },

    /// Extract audio waveform peaks and optionally render PNG waveform image
    Waveform {
        #[arg(help = "Source media file path")]
        path: PathBuf,

        #[arg(short, long, help = "Output JSON path for raw peak data")]
        output: Option<PathBuf>,

        #[arg(long, help = "Output PNG image path for visual waveform rendering")]
        image: Option<PathBuf>,

        #[arg(short, long, default_value_t = 800, help = "Waveform image width")]
        width: u32,

        #[arg(short = 'H', long, default_value_t = 120, help = "Waveform image height")]
        height: u32,

        #[arg(long, default_value = "00ff88", help = "Waveform color in hex")]
        color: String,
    },

    /// Transcode media with proxy profiles and hardware acceleration
    Transcode {
        #[arg(help = "Source media file path")]
        input: PathBuf,

        #[arg(short, long, help = "Target output file path")]
        output: PathBuf,

        #[arg(
            long,
            default_value = "720p",
            help = "Proxy preset: 720p, 1080p, or prores"
        )]
        preset: String,

        #[arg(
            long,
            value_enum,
            default_value_t = CliHwAccel::Auto,
            help = "Hardware acceleration mode"
        )]
        hw: CliHwAccel,

        #[arg(long, help = "Target video bitrate in kbps")]
        bitrate: Option<u32>,
    },
}

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    tracing_subscriber::fmt::init();

    let cli = Cli::parse();

    let mut config = MediaEngineConfig::default();
    if let Some(ffmpeg) = cli.ffmpeg {
        config.ffmpeg_path = Some(ffmpeg);
    }
    if let Some(ffprobe) = cli.ffprobe {
        config.ffprobe_path = Some(ffprobe);
    }
    if let Some(cache_dir) = cli.cache_dir {
        config.cache_dir = cache_dir;
    }

    let engine = MediaEngine::initialize(config).await?;

    match cli.command {
        Commands::Capabilities { json } => {
            let caps = engine.capabilities();
            if json {
                println!("{}", serde_json::to_string_pretty(caps)?);
            } else {
                println!("=== TravelCut Studio Media Engine Capabilities ===");
                println!("FFmpeg Version : {}", caps.version);
                println!("Hardware Accels: {:?}", caps.hwaccels);
                println!(
                    "Key Encoders   : NVENC(h264/hevc): {}/{}, QSV(h264/hevc): {}/{}, AMF(h264/hevc): {}/{}",
                    caps.has_encoder("h264_nvenc"),
                    caps.has_encoder("hevc_nvenc"),
                    caps.has_encoder("h264_qsv"),
                    caps.has_encoder("hevc_qsv"),
                    caps.has_encoder("h264_amf"),
                    caps.has_encoder("hevc_amf"),
                );
                println!(
                    "Total Encoders : {} | Total Decoders: {}",
                    caps.video_encoders.len() + caps.audio_encoders.len(),
                    caps.video_decoders.len()
                );
            }
        }
        Commands::Probe { path, json } => {
            let metadata = engine.probe(&path).await?;
            if json {
                println!("{}", serde_json::to_string_pretty(&metadata)?);
            } else {
                println!("=== Media Metadata: {} ===", path.display());
                println!("Container: {}", metadata.format_name);
                println!("Duration : {:.2}s", metadata.duration_secs);
                println!("Size     : {} bytes", metadata.file_size_bytes);
                if let Some(ref v) = metadata.video {
                    println!(
                        "Video    : {}x{} @ {:.2} fps, Codec: {}, Profile: {}",
                        v.width, v.height, v.fps, v.codec, v.profile
                    );
                    println!(
                        "Color    : ColorSpace: {}, Transfer: {}, Primaries: {}, HDR: {}",
                        v.color_space.as_deref().unwrap_or("N/A"),
                        v.color_transfer.as_deref().unwrap_or("N/A"),
                        v.color_primaries.as_deref().unwrap_or("N/A"),
                        v.is_hdr
                    );
                }
                if let Some(ref a) = metadata.audio {
                    println!(
                        "Audio    : Codec: {}, Channels: {}, SampleRate: {} Hz, Bitrate: {} kbps",
                        a.codec,
                        a.channels,
                        a.sample_rate,
                        a.bitrate_kbps.unwrap_or(0)
                    );
                }
                if let Some(ref c) = metadata.camera {
                    println!(
                        "Camera   : Make: {:?}, Model: {:?}, Software: {:?}",
                        c.make, c.model, c.software
                    );
                }
                if let Some(ref g) = metadata.gps {
                    println!(
                        "GPS      : Lat: {:.6}, Lon: {:.6}, Alt: {:?}m",
                        g.latitude, g.longitude, g.altitude
                    );
                }
            }
        }
        Commands::Scan {
            path,
            recursive,
            deep,
            json,
        } => {
            let options = InventoryOptions {
                recursive,
                extract_deep_metadata: deep,
                ..Default::default()
            };
            let files = engine.scan_directory(&path, &options, None).await?;
            if json {
                println!("{}", serde_json::to_string_pretty(&files)?);
            } else {
                println!("=== Discovered {} media assets in {} ===", files.len(), path.display());
                for f in &files {
                    println!(
                        "[{:?}] {:?} | {} ({:.2} MB)",
                        f.category,
                        f.camera_profile,
                        f.path.file_name().unwrap_or_default().to_string_lossy(),
                        f.size_bytes as f64 / (1024.0 * 1024.0)
                    );
                }
            }
        }
        Commands::Thumb {
            path,
            output,
            time,
            width,
            height,
        } => {
            let options = ThumbnailOptions {
                timestamp_secs: time,
                width,
                height,
                fast_seek: true,
                output_path: output,
                ..Default::default()
            };
            let thumb = engine.generate_thumbnail(&path, &options, None).await?;
            println!("Thumbnail generated: {}", thumb.display());
        }
        Commands::Filmstrip {
            path,
            output_dir,
            count,
            width,
            height,
        } => {
            let options = FilmstripOptions {
                count,
                width,
                height,
                output_dir,
                ..Default::default()
            };
            let frames = engine
                .generate_filmstrip(&path, &options, None, |idx, total, p| {
                    eprintln!("Rendered frame [{}/{}] -> {}", idx + 1, total, p.display());
                })
                .await?;
            println!("Generated {} filmstrip frames", frames.len());
        }
        Commands::Waveform {
            path,
            output,
            image,
            width,
            height,
            color,
        } => {
            let options = WaveformOptions {
                target_points: 1000,
                channels: 2,
                points_per_second: 50,
            };
            let wf = engine.extract_waveform(&path, &options, None).await?;
            println!(
                "Extracted waveform: {} samples ({:.2}s duration, {} channels)",
                wf.peaks.len(),
                wf.duration_secs,
                wf.channels
            );

            if let Some(out_json) = output {
                let json_data = serde_json::to_string_pretty(&wf)?;
                tokio::fs::write(&out_json, json_data).await?;
                println!("Waveform peaks saved to: {}", out_json.display());
            }

            if let Some(out_image) = image {
                let img_path = engine
                    .render_waveform_image(&path, width, height, &color, None)
                    .await?;
                tokio::fs::copy(&img_path, &out_image).await?;
                println!("Waveform image saved to: {}", out_image.display());
            }
        }
        Commands::Transcode {
            input,
            output,
            preset,
            hw,
            bitrate,
        } => {
            let (target_res, video_codec) = match preset.to_lowercase().as_str() {
                "1080p" => (Some((1920, 1080)), Some("h264".to_string())),
                "prores" => (Some((1280, 720)), Some("prores".to_string())),
                _ => (Some((1280, 720)), Some("h264".to_string())),
            };

            let options = TranscodeOptions {
                source_path: input,
                output_path: output,
                target_resolution: target_res,
                video_codec,
                hw_accel: hw.into(),
                crf: Some(23),
                video_bitrate_kbps: bitrate,
                audio_bitrate_kbps: Some(128),
                force_cfr: true,
                target_fps: Some(30.0),
                profile: None,
            };

            let res = engine
                .transcode(&options, None, None, |prog| {
                    eprint!(
                        "\rProgress: {:.1}% | FPS: {:.1} | Frame: {} | Time: {:.1}s",
                        prog.percentage,
                        prog.current_fps.unwrap_or(0.0),
                        prog.frames_processed.unwrap_or(0),
                        prog.current_time_secs.unwrap_or(0.0)
                    );
                })
                .await?;

            eprintln!();
            println!("Transcode finished: {}", res.output_path.display());
            println!(
                "Size: {:.2} MB | Elapsed: {:.2}s | Encoder: {}",
                res.file_size_bytes as f64 / (1024.0 * 1024.0),
                res.elapsed_secs,
                res.encoder_used
            );
        }
    }

    Ok(())
}
