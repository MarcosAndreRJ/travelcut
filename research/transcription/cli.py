"""Command-line interface (CLI) for TravelCut Studio speech transcription research.

Usage:
    python -m research.transcription.cli --input audio.wav --output transcript.json
    python -m research.transcription.cli --input audio.wav --format phase4 --engine mock
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Optional

from research.transcription.contracts import TranscriptionDocument
from research.transcription.engine import FasterWhisperEngine, MockTranscriptionEngine


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="python -m research.transcription.cli",
        description="TravelCut Studio - Audio Transcription & Word Alignment CLI (Phase 7 Research)",
    )
    parser.add_argument(
        "--input", "-i",
        required=True,
        type=str,
        help="Path to input audio file (.wav format)",
    )
    parser.add_argument(
        "--output", "-o",
        type=str,
        default=None,
        help="Path for output file (defaults to stdout if omitted)",
    )
    parser.add_argument(
        "--media-id",
        type=str,
        default="media_asset_001",
        help="Media asset identifier (default: media_asset_001)",
    )
    parser.add_argument(
        "--engine",
        choices=["auto", "mock", "faster-whisper"],
        default="auto",
        help="ASR backend engine (default: auto)",
    )
    parser.add_argument(
        "--model",
        default="small",
        help="ASR model size: tiny, base, small, medium, large-v3 (default: small)",
    )
    parser.add_argument(
        "--language",
        default="pt",
        help="Spoken language code, e.g. pt, en (default: pt)",
    )
    parser.add_argument(
        "--no-align",
        dest="word_timestamps",
        action="store_false",
        help="Disable word-level timestamp alignment",
    )
    parser.set_defaults(word_timestamps=True)
    parser.add_argument(
        "--no-vad",
        dest="vad_filter",
        action="store_false",
        help="Disable voice activity detection filtering",
    )
    parser.set_defaults(vad_filter=True)
    parser.add_argument(
        "--format",
        choices=["json", "phase4", "txt", "srt"],
        default="json",
        help="Output serialization format (default: json)",
    )
    parser.add_argument(
        "--video-category",
        type=str,
        default=None,
        help="Travel video context hint: scenery, drone, waterfall, road, speech",
    )
    parser.add_argument(
        "--bench",
        action="store_true",
        help="Print benchmark summary to stderr",
    )
    return parser


def format_srt(doc: TranscriptionDocument) -> str:
    """Format transcription document as SubRip (.srt) subtitle text."""
    lines = []
    for idx, seg in enumerate(doc.segments, start=1):
        def ms_to_srt(ms: int) -> str:
            hours = ms // 3600000
            ms %= 3600000
            minutes = ms // 60000
            ms %= 60000
            seconds = ms // 1000
            milis = ms % 1000
            return f"{hours:02d}:{minutes:02d}:{seconds:02d},{milis:03d}"

        lines.append(str(idx))
        lines.append(f"{ms_to_srt(seg.start_ms)} --> {ms_to_srt(seg.end_ms)}")
        lines.append(seg.text)
        lines.append("")
    return "\n".join(lines)


def run_cli(args: Optional[list] = None) -> int:
    parser = build_parser()
    parsed = parser.parse_args(args)

    input_path = Path(parsed.input)
    if not input_path.exists():
        sys.stderr.write(f"Error: Input audio file does not exist: {input_path}\n")
        return 1

    # Select engine
    engine = None
    if parsed.engine in ("faster-whisper", "auto"):
        fw = FasterWhisperEngine(model_size_or_path=parsed.model)
        if fw.is_available:
            engine = fw
        elif parsed.engine == "faster-whisper":
            sys.stderr.write(
                "Error: faster-whisper requested but not installed. Install via: pip install faster-whisper\n"
            )
            return 2
        else:
            # Fallback to mock
            engine = MockTranscriptionEngine(model_name=parsed.model)
    else:
        engine = MockTranscriptionEngine(model_name=parsed.model)

    try:
        doc = engine.transcribe(
            audio_path=input_path,
            media_id=parsed.media_id,
            language=parsed.language,
            word_timestamps=parsed.word_timestamps,
            vad_filter=parsed.vad_filter,
            video_category_hint=parsed.video_category,
        )
    except Exception as e:
        sys.stderr.write(f"Transcription error: {e}\n")
        return 3

    # Format output
    if parsed.format == "json":
        output_content = doc.model_dump_json(indent=2)
    elif parsed.format == "phase4":
        output_content = json.dumps(doc.to_phase4_workspace_export(), indent=2, ensure_ascii=False)
    elif parsed.format == "srt":
        output_content = format_srt(doc)
    elif parsed.format == "txt":
        output_content = doc.full_text
    else:
        output_content = doc.model_dump_json(indent=2)

    # Write output
    if parsed.output:
        out_path = Path(parsed.output)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(output_content, encoding="utf-8")
        if parsed.bench:
            sys.stderr.write(f"Wrote {parsed.format} output to: {out_path}\n")
    else:
        sys.stdout.write(output_content + "\n")

    if parsed.bench:
        meta = doc.processing_metadata
        sys.stderr.write(
            f"\n--- Benchmark Summary ---\n"
            f"Engine: {doc.model_info.engine_name} ({doc.model_info.model_name})\n"
            f"Audio Duration: {meta.audio_duration_ms} ms ({meta.audio_duration_ms / 1000.0:.2f} s)\n"
            f"Processing Time: {meta.processing_time_ms} ms\n"
            f"Real-Time Factor (RTF): {meta.real_time_factor:.3f}\n"
            f"Segments: {len(doc.segments)}\n"
            f"Words: {len(doc.words)}\n"
            f"Silence Intervals: {len(doc.silence_intervals)}\n"
            f"-------------------------\n"
        )

    return 0


if __name__ == "__main__":
    sys.exit(run_cli())
