"""Deterministic audio sample generator for the 15 research scenarios (Section 9).

Generates realistic 16-bit mono 16kHz WAV files and associated ground-truth
metadata for testing speech recognition, VAD, alignment, and silence detection.
"""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Dict, List, Optional

from research.transcription.audio import SyntheticSpeechSegment, generate_synthetic_wav


@dataclass
class ScenarioMetadata:
    scenario_id: str
    name: str
    description: str
    audio_path: str
    duration_ms: int
    expected_text: str
    expected_speech_segments_count: int
    has_background_noise: bool
    has_hesitations: bool
    has_long_pauses: bool
    has_speakers_alternation: bool
    is_silence_only: bool
    expected_removable_silence: bool


def generate_all_15_scenarios(output_dir: str | Path) -> Dict[str, ScenarioMetadata]:
    """Generate all 15 required research audio scenarios into output_dir.

    Returns dictionary mapping scenario_id to ScenarioMetadata.
    """
    out_dir = Path(output_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    scenarios: Dict[str, ScenarioMetadata] = {}

    # Scenario 1: Fala clara em português
    s1_path = out_dir / "scen01_fala_clara.wav"
    s1_segments = [
        SyntheticSpeechSegment(start_ms=300, end_ms=1800, frequency_hz=210.0, amplitude=0.6),
        SyntheticSpeechSegment(start_ms=2100, end_ms=3900, frequency_hz=210.0, amplitude=0.6),
    ]
    generate_synthetic_wav(s1_path, total_duration_sec=4.2, speech_segments=s1_segments)
    scenarios["scen01"] = ScenarioMetadata(
        scenario_id="scen01",
        name="Fala clara em português",
        description="Enunciado com dicção nítida e cadência estável de vlog de viagem.",
        audio_path=str(s1_path),
        duration_ms=4200,
        expected_text="Chegamos no centro histórico de Paraty e o dia está maravilhoso.",
        expected_speech_segments_count=2,
        has_background_noise=False,
        has_hesitations=False,
        has_long_pauses=False,
        has_speakers_alternation=False,
        is_silence_only=False,
        expected_removable_silence=False,
    )

    # Scenario 2: Fala rápida
    s2_path = out_dir / "scen02_fala_rapida.wav"
    s2_segments = [
        SyntheticSpeechSegment(start_ms=100, end_ms=1500, frequency_hz=230.0, amplitude=0.65),
    ]
    generate_synthetic_wav(s2_path, total_duration_sec=1.7, speech_segments=s2_segments)
    scenarios["scen02"] = ScenarioMetadata(
        scenario_id="scen02",
        name="Fala rápida",
        description="Locução acelerada com alta densidade silábica e palavras aglutinadas.",
        audio_path=str(s2_path),
        duration_ms=1700,
        expected_text="Olhaessedetalhedafachadaqueincrívelagentejávaisubir.",
        expected_speech_segments_count=1,
        has_background_noise=False,
        has_hesitations=False,
        has_long_pauses=False,
        has_speakers_alternation=False,
        is_silence_only=False,
        expected_removable_silence=False,
    )

    # Scenario 3: Pausas longas
    s3_path = out_dir / "scen03_pausas_longas.wav"
    s3_segments = [
        SyntheticSpeechSegment(start_ms=200, end_ms=1400, frequency_hz=200.0, amplitude=0.55),
        # 2200ms of dead air silence between 1400ms and 3600ms
        SyntheticSpeechSegment(start_ms=3600, end_ms=4800, frequency_hz=200.0, amplitude=0.55),
    ]
    generate_synthetic_wav(s3_path, total_duration_sec=5.2, speech_segments=s3_segments)
    scenarios["scen03"] = ScenarioMetadata(
        scenario_id="scen03",
        name="Pausas longas",
        description="Frases separadas por vácuo acústico longo (>2000ms), candidato a corte.",
        audio_path=str(s3_path),
        duration_ms=5200,
        expected_text="A vista daqui de cima é espetacular. Vamos descer pela trilha agora.",
        expected_speech_segments_count=2,
        has_background_noise=False,
        has_hesitations=False,
        has_long_pauses=True,
        has_speakers_alternation=False,
        is_silence_only=False,
        expected_removable_silence=True,
    )

    # Scenario 4: Repetições ("eu fui... eu fui ontem")
    s4_path = out_dir / "scen04_repeticoes.wav"
    s4_segments = [
        SyntheticSpeechSegment(start_ms=200, end_ms=1100, frequency_hz=215.0, amplitude=0.55),
        SyntheticSpeechSegment(start_ms=1500, end_ms=3200, frequency_hz=215.0, amplitude=0.6),
    ]
    generate_synthetic_wav(s4_path, total_duration_sec=3.5, speech_segments=s4_segments)
    scenarios["scen04"] = ScenarioMetadata(
        scenario_id="scen04",
        name="Repetições de palavras",
        description="Falso início e repetição de sintagma ('eu fui... eu fui ontem').",
        audio_path=str(s4_path),
        duration_ms=3500,
        expected_text="Eu fui... eu fui ontem na cachoeira do tobogã.",
        expected_speech_segments_count=2,
        has_background_noise=False,
        has_hesitations=False,
        has_long_pauses=False,
        has_speakers_alternation=False,
        is_silence_only=False,
        expected_removable_silence=False,
    )

    # Scenario 5: Hesitações ("ééé... humm... tipo assim")
    s5_path = out_dir / "scen05_hesitacoes.wav"
    s5_segments = [
        SyntheticSpeechSegment(start_ms=200, end_ms=1200, frequency_hz=190.0, amplitude=0.4),  # hesitação baixa
        SyntheticSpeechSegment(start_ms=1400, end_ms=3200, frequency_hz=220.0, amplitude=0.6),
    ]
    generate_synthetic_wav(s5_path, total_duration_sec=3.6, speech_segments=s5_segments)
    scenarios["scen05"] = ScenarioMetadata(
        scenario_id="scen05",
        name="Hesitações e vícios de linguagem",
        description="Vocalizações de preenchimento ('ééé', 'humm', 'tipo assim').",
        audio_path=str(s5_path),
        duration_ms=3600,
        expected_text="Ééé... humm... tipo assim, nós chegamos bem cedo.",
        expected_speech_segments_count=2,
        has_background_noise=False,
        has_hesitations=True,
        has_long_pauses=False,
        has_speakers_alternation=False,
        is_silence_only=False,
        expected_removable_silence=False,
    )

    # Scenario 6: Duas pessoas conversando (alternância de falantes)
    s6_path = out_dir / "scen06_duas_pessoas.wav"
    s6_segments = [
        SyntheticSpeechSegment(start_ms=300, end_ms=1900, frequency_hz=150.0, amplitude=0.6),  # Falante 1 (grave)
        SyntheticSpeechSegment(start_ms=2200, end_ms=3800, frequency_hz=250.0, amplitude=0.6),  # Falante 2 (agudo)
    ]
    generate_synthetic_wav(s6_path, total_duration_sec=4.2, speech_segments=s6_segments)
    scenarios["scen06"] = ScenarioMetadata(
        scenario_id="scen06",
        name="Conversa entre duas pessoas",
        description="Alternância de falantes com diferentes frequências fundamentais (f0).",
        audio_path=str(s6_path),
        duration_ms=4200,
        expected_text="Você trouxe o mapa da trilha? Sim, está na minha mochila.",
        expected_speech_segments_count=2,
        has_background_noise=False,
        has_hesitations=False,
        has_long_pauses=False,
        has_speakers_alternation=True,
        is_silence_only=False,
        expected_removable_silence=False,
    )

    # Scenario 7: Ruído ambiente (praia / tráfego moderado)
    s7_path = out_dir / "scen07_ruido_ambiente.wav"
    s7_segments = [
        SyntheticSpeechSegment(start_ms=500, end_ms=2800, frequency_hz=220.0, amplitude=0.55),
    ]
    generate_synthetic_wav(
        s7_path, total_duration_sec=3.2, speech_segments=s7_segments, background_noise_level=0.03
    )
    scenarios["scen07"] = ScenarioMetadata(
        scenario_id="scen07",
        name="Ruído ambiente de praia/tráfego",
        description="Fala sobreposta a ruído contínuo de ondas e vento moderado.",
        audio_path=str(s7_path),
        duration_ms=3200,
        expected_text="O mar está agitado hoje aqui na praia do Sono.",
        expected_speech_segments_count=1,
        has_background_noise=True,
        has_hesitations=False,
        has_long_pauses=False,
        has_speakers_alternation=False,
        is_silence_only=False,
        expected_removable_silence=False,
    )

    # Scenario 8: Ruído de vento forte
    s8_path = out_dir / "scen08_vento_forte.wav"
    s8_segments = [
        SyntheticSpeechSegment(start_ms=400, end_ms=2500, frequency_hz=215.0, amplitude=0.5),
    ]
    generate_synthetic_wav(
        s8_path, total_duration_sec=3.0, speech_segments=s8_segments, background_noise_level=0.08
    )
    scenarios["scen08"] = ScenarioMetadata(
        scenario_id="scen08",
        name="Ruído de vento forte no microfone",
        description="Rajadas de vento de baixa frequência impactando a cápsula do microfone.",
        audio_path=str(s8_path),
        duration_ms=3000,
        expected_text="Estamos no topo do morro com vento fortíssimo.",
        expected_speech_segments_count=1,
        has_background_noise=True,
        has_hesitations=False,
        has_long_pauses=False,
        has_speakers_alternation=False,
        is_silence_only=False,
        expected_removable_silence=False,
    )

    # Scenario 9: Música de fundo
    s9_path = out_dir / "scen09_musica_fundo.wav"
    s9_segments = [
        SyntheticSpeechSegment(start_ms=300, end_ms=3000, frequency_hz=220.0, amplitude=0.6),
    ]
    generate_synthetic_wav(
        s9_path, total_duration_sec=3.4, speech_segments=s9_segments, background_noise_level=0.04
    )
    scenarios["scen09"] = ScenarioMetadata(
        scenario_id="scen09",
        name="Música de fundo acústica",
        description="Voz com trilha instrumental de violão/percussão tocando ao fundo.",
        audio_path=str(s9_path),
        duration_ms=3400,
        expected_text="Essa pousada tem um som ao vivo bem relaxante no restaurante.",
        expected_speech_segments_count=1,
        has_background_noise=True,
        has_hesitations=False,
        has_long_pauses=False,
        has_speakers_alternation=False,
        is_silence_only=False,
        expected_removable_silence=False,
    )

    # Scenario 10: Fala próxima ao início (0ms) e ao fim do arquivo
    s10_path = out_dir / "scen10_fala_nas_bordas.wav"
    s10_segments = [
        SyntheticSpeechSegment(start_ms=0, end_ms=1200, frequency_hz=210.0, amplitude=0.6),
        SyntheticSpeechSegment(start_ms=1800, end_ms=3000, frequency_hz=210.0, amplitude=0.6),
    ]
    generate_synthetic_wav(s10_path, total_duration_sec=3.0, speech_segments=s10_segments)
    scenarios["scen10"] = ScenarioMetadata(
        scenario_id="scen10",
        name="Fala rente às bordas (0ms e fim)",
        description="Palavra iniciada no frame 0 e terminada exatamente no fim do áudio.",
        audio_path=str(s10_path),
        duration_ms=3000,
        expected_text="Já começamos gravando e vamos direto ao ponto final.",
        expected_speech_segments_count=2,
        has_background_noise=False,
        has_hesitations=False,
        has_long_pauses=False,
        has_speakers_alternation=False,
        is_silence_only=False,
        expected_removable_silence=False,
    )

    # Scenario 11: Ausência total de fala (silêncio / som ambiente)
    s11_path = out_dir / "scen11_ausencia_total_fala.wav"
    generate_synthetic_wav(s11_path, total_duration_sec=4.0, speech_segments=[])
    scenarios["scen11"] = ScenarioMetadata(
        scenario_id="scen11",
        name="Ausência total de fala",
        description="Tomada de drone ou paisagem sem nenhuma locução humana.",
        audio_path=str(s11_path),
        duration_ms=4000,
        expected_text="",
        expected_speech_segments_count=0,
        has_background_noise=False,
        has_hesitations=False,
        has_long_pauses=True,
        has_speakers_alternation=False,
        is_silence_only=True,
        expected_removable_silence=True,
    )

    # Scenario 12: Áudio muito curto (< 500ms)
    s12_path = out_dir / "scen12_audio_muito_curto.wav"
    s12_segments = [
        SyntheticSpeechSegment(start_ms=50, end_ms=320, frequency_hz=220.0, amplitude=0.6),
    ]
    generate_synthetic_wav(s12_path, total_duration_sec=0.4, speech_segments=s12_segments)
    scenarios["scen12"] = ScenarioMetadata(
        scenario_id="scen12",
        name="Áudio muito curto (<500ms)",
        description="Fragmento de corte ultrarrápido ou interjeição monosilábica (ex: 'Olha!').",
        audio_path=str(s12_path),
        duration_ms=400,
        expected_text="Olha!",
        expected_speech_segments_count=1,
        has_background_noise=False,
        has_hesitations=False,
        has_long_pauses=False,
        has_speakers_alternation=False,
        is_silence_only=False,
        expected_removable_silence=False,
    )

    # Scenario 13: Áudio longo (> 30s)
    s13_path = out_dir / "scen13_audio_longo.wav"
    s13_segments = [
        SyntheticSpeechSegment(start_ms=1000, end_ms=8000, frequency_hz=205.0, amplitude=0.55),
        SyntheticSpeechSegment(start_ms=10000, end_ms=19000, frequency_hz=205.0, amplitude=0.55),
        SyntheticSpeechSegment(start_ms=22000, end_ms=31000, frequency_hz=205.0, amplitude=0.55),
    ]
    generate_synthetic_wav(s13_path, total_duration_sec=33.0, speech_segments=s13_segments)
    scenarios["scen13"] = ScenarioMetadata(
        scenario_id="scen13",
        name="Áudio longo (>30s)",
        description="Take contínuo de vlog com múltiplas sentenças e pausas intercaladas.",
        audio_path=str(s13_path),
        duration_ms=33000,
        expected_text="Primeira parte do relato de viagem. Segunda parte com detalhes históricos. Terceira parte com dicas gastronômicas.",
        expected_speech_segments_count=3,
        has_background_noise=False,
        has_hesitations=False,
        has_long_pauses=True,
        has_speakers_alternation=False,
        is_silence_only=False,
        expected_removable_silence=True,
    )

    # Scenario 14: Falha de alinhamento / descontinuidade temporal
    s14_path = out_dir / "scen14_descontinuidade.wav"
    s14_segments = [
        SyntheticSpeechSegment(start_ms=200, end_ms=1500, frequency_hz=210.0, amplitude=0.6),
        SyntheticSpeechSegment(start_ms=1900, end_ms=3000, frequency_hz=210.0, amplitude=0.6),
    ]
    generate_synthetic_wav(s14_path, total_duration_sec=3.5, speech_segments=s14_segments)
    scenarios["scen14"] = ScenarioMetadata(
        scenario_id="scen14",
        name="Falha de alinhamento e descontinuidade temporal",
        description="Simulação de ASR com saltos temporais ou perda de alinhador fonético.",
        audio_path=str(s14_path),
        duration_ms=3500,
        expected_text="Segmento inicial bem alinhado porém o segundo perdeu sincronia fonética.",
        expected_speech_segments_count=2,
        has_background_noise=False,
        has_hesitations=False,
        has_long_pauses=False,
        has_speakers_alternation=False,
        is_silence_only=False,
        expected_removable_silence=False,
    )

    # Scenario 15: Texto reconhecido incorretamente / baixa confiança acústica
    s15_path = out_dir / "scen15_baixa_confianca.wav"
    s15_segments = [
        SyntheticSpeechSegment(start_ms=400, end_ms=2400, frequency_hz=180.0, amplitude=0.25),  # amplitude fraca
    ]
    generate_synthetic_wav(
        s15_path, total_duration_sec=2.8, speech_segments=s15_segments, background_noise_level=0.06
    )
    scenarios["scen15"] = ScenarioMetadata(
        scenario_id="scen15",
        name="Baixa confiança acústica e balbucio",
        description="Fala em tom muito baixo, com sussurro ou interferência acústica forte.",
        audio_path=str(s15_path),
        duration_ms=2800,
        expected_text="Frase inaudível e sussurrada com palavras de baixa probabilidade.",
        expected_speech_segments_count=1,
        has_background_noise=True,
        has_hesitations=False,
        has_long_pauses=False,
        has_speakers_alternation=False,
        is_silence_only=False,
        expected_removable_silence=False,
    )

    # Export scenario index JSON
    index_file = out_dir / "scenarios_index.json"
    index_data = {k: asdict(v) for k, v in scenarios.items()}
    index_file.write_text(json.dumps(index_data, indent=2, ensure_ascii=False), encoding="utf-8")

    return scenarios
