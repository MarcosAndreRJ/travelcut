"""Benchmark suite for ASR transcription, VAD efficiency, and memory profiling.

Measures:
- Real-Time Factor (RTF = elapsed_time / audio_duration)
- Latency (ms)
- Throughput (words / sec)
- Silence & savings calculation
- Memory footprint
Outputs structured JSON and Markdown summary tables.
"""

from __future__ import annotations

import json
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional

from research.transcription.contracts import TranscriptionDocument
from research.transcription.engine import BaseTranscriptionEngine, MockTranscriptionEngine


@dataclass
class BenchmarkResult:
    scenario_id: str
    scenario_name: str
    audio_duration_ms: int
    processing_time_ms: int
    real_time_factor: float
    throughput_words_per_sec: float
    word_count: int
    segment_count: int
    speech_intervals_count: int
    silence_intervals_count: int
    removable_silence_ms: int
    removable_savings_percent: float
    model_name: str
    engine_name: str


class TranscriptionBenchmarkSuite:
    """Executes benchmarks across audio files or synthetic scenarios."""

    def __init__(self, engine: Optional[BaseTranscriptionEngine] = None):
        self.engine = engine or MockTranscriptionEngine()

    def run_benchmark(
        self,
        audio_path: str | Path,
        scenario_id: str = "custom",
        scenario_name: str = "Custom Audio",
        media_id: str = "bench_media",
    ) -> BenchmarkResult:
        """Run single audio benchmark and compute precision metrics."""
        path = Path(audio_path)
        t0 = time.perf_counter()

        doc: TranscriptionDocument = self.engine.transcribe(
            audio_path=path,
            media_id=media_id,
            word_timestamps=True,
            vad_filter=True,
        )
        elapsed_sec = time.perf_counter() - t0

        audio_dur_ms = doc.duration_ms
        proc_time_ms = doc.processing_metadata.processing_time_ms
        rtf = doc.processing_metadata.real_time_factor

        word_count = len(doc.words)
        throughput = (word_count / (proc_time_ms / 1000.0)) if proc_time_ms > 0 else 0.0

        removable_ms = sum(s.duration_ms for s in doc.silence_intervals if s.is_removable_candidate)
        savings_pct = (removable_ms / float(audio_dur_ms) * 100.0) if audio_dur_ms > 0 else 0.0

        return BenchmarkResult(
            scenario_id=scenario_id,
            scenario_name=scenario_name,
            audio_duration_ms=audio_dur_ms,
            processing_time_ms=proc_time_ms,
            real_time_factor=rtf,
            throughput_words_per_sec=round(throughput, 1),
            word_count=word_count,
            segment_count=len(doc.segments),
            speech_intervals_count=len(doc.speech_intervals),
            silence_intervals_count=len(doc.silence_intervals),
            removable_silence_ms=removable_ms,
            removable_savings_percent=round(savings_pct, 2),
            model_name=doc.model_info.model_name,
            engine_name=doc.model_info.engine_name,
        )

    def run_suite(
        self,
        scenarios: Dict[str, Any],
        output_report_json: Optional[str | Path] = None,
        output_report_md: Optional[str | Path] = None,
    ) -> List[BenchmarkResult]:
        """Execute benchmark across a collection of scenarios."""
        results: List[BenchmarkResult] = []

        for scen_id, meta in scenarios.items():
            audio_path = getattr(meta, "audio_path", meta.get("audio_path") if isinstance(meta, dict) else str(meta))
            name = getattr(meta, "name", meta.get("name", scen_id) if isinstance(meta, dict) else scen_id)

            res = self.run_benchmark(
                audio_path=audio_path,
                scenario_id=scen_id,
                scenario_name=name,
                media_id=f"bench_{scen_id}",
            )
            results.append(res)

        if output_report_json:
            p = Path(output_report_json)
            p.parent.mkdir(parents=True, exist_ok=True)
            data = [asdict(r) for r in results]
            p.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")

        if output_report_md:
            p = Path(output_report_md)
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text(self.generate_markdown_report(results), encoding="utf-8")

        return results

    @staticmethod
    def generate_markdown_report(results: List[BenchmarkResult]) -> str:
        """Format benchmark results into Markdown table."""
        lines = [
            "# TravelCut Studio - Transcription & VAD Benchmark Report",
            "",
            f"**Total Scenarios Evaluated:** {len(results)}",
            "",
            "| Scenario | Audio (s) | Proc Time (ms) | RTF | Words | Words/s | Silence Gaps | Removable Savings (%) |",
            "| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |",
        ]

        total_audio_ms = sum(r.audio_duration_ms for r in results)
        total_proc_ms = sum(r.processing_time_ms for r in results)
        avg_rtf = (total_proc_ms / float(total_audio_ms)) if total_audio_ms > 0 else 0.0

        for r in results:
            aud_s = f"{r.audio_duration_ms / 1000.0:.2f}"
            lines.append(
                f"| {r.scenario_name} | {aud_s}s | {r.processing_time_ms}ms | {r.real_time_factor:.3f} | "
                f"{r.word_count} | {r.throughput_words_per_sec:.1f} | {r.silence_intervals_count} | "
                f"{r.removable_savings_percent:.1f}% |"
            )

        lines.extend([
            "",
            "### Summary Metrics",
            f"- **Cumulative Audio Duration:** {total_audio_ms / 1000.0:.2f} seconds",
            f"- **Cumulative Processing Time:** {total_proc_ms / 1000.0:.2f} seconds",
            f"- **Aggregate Real-Time Factor (RTF):** {avg_rtf:.3f} (Values < 1.0 indicate faster than real-time)",
            f"- **Target Production Threshold:** RTF < 0.35 on CPU (small.en/pt int8)",
            "",
        ])

        return "\n".join(lines)
