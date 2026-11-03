#!/usr/bin/env python3
"""
TravelCut Studio - worker Whisper (protocolo "travelcut.whisper" v1).

Transporte: NDJSON (uma mensagem JSON por linha) em stdin/stdout.
Comandos: "probe", "transcribe", "exit".

Regras:
- Modelo padrão "large-v3". O worker NUNCA troca o modelo pedido por outro.
- Não baixa modelos. Se o arquivo do modelo não estiver no cache, responde
  "model_not_available" com instrução de instalação manual.
- stdout do protocolo é isolado: bibliotecas (tqdm, warnings) escrevem em stderr.
- Respostas usam ensure_ascii=True para não depender da codificação do console.

Este arquivo não foi executado na VM de desenvolvimento (sem Python). Validação real: host_pending.
"""

import importlib.util
import json
import os
import re
import shutil
import sys
import time

PROTOCOL = "travelcut.whisper"
PROTOCOL_VERSION = 1
DEFAULT_MODEL = "large-v3"
DEFAULT_LANGUAGE = "pt"
MODEL_NAME_RE = re.compile(r"^[A-Za-z0-9._-]{1,64}$")

# Canal de protocolo: guardado antes de redirecionar sys.stdout para sys.stderr.
_proto_out = sys.stdout


def _emit(message):
    _proto_out.write(json.dumps(message, ensure_ascii=True) + "\n")
    _proto_out.flush()


def _ok(request_id, result):
    _emit({
        "protocol": PROTOCOL,
        "version": PROTOCOL_VERSION,
        "requestId": request_id,
        "ok": True,
        "result": result,
    })


def _err(request_id, code, message):
    _emit({
        "protocol": PROTOCOL,
        "version": PROTOCOL_VERSION,
        "requestId": request_id,
        "ok": False,
        "error": {"code": code, "message": message},
    })


def _cache_dir():
    # Mesmo cálculo usado pelo openai-whisper (download_root padrão).
    default = os.path.join(os.path.expanduser("~"), ".cache")
    return os.path.join(os.getenv("XDG_CACHE_HOME", default), "whisper")


def _model_cached(model_name):
    return os.path.isfile(os.path.join(_cache_dir(), model_name + ".pt"))


def _whisper_version():
    try:
        from importlib.metadata import version
        return version("openai-whisper")
    except Exception:
        return None


def handle_probe(request_id):
    try:
        whisper_installed = False
        whisper_version = None
        if importlib.util.find_spec("whisper") is not None:
            try:
                import whisper  # noqa: F401  (confirma que o pacote é o openai-whisper)
                whisper_installed = hasattr(whisper, "load_model")
            except Exception:
                whisper_installed = False
            whisper_version = _whisper_version() if whisper_installed else None

        cuda = False
        device = "cpu"
        if importlib.util.find_spec("torch") is not None:
            try:
                import torch
                cuda = bool(torch.cuda.is_available())
                if cuda:
                    device = torch.cuda.get_device_name(0)
            except Exception:
                cuda = False
                device = "cpu"

        _ok(request_id, {
            "python": sys.version.split()[0],
            "whisperInstalled": whisper_installed,
            "whisperVersion": whisper_version,
            "cuda": cuda,
            "device": device,
            "ffmpegOnPath": shutil.which("ffmpeg") is not None,
            "defaultModel": DEFAULT_MODEL,
            "modelCached": _model_cached(DEFAULT_MODEL),
        })
    except Exception as exc:
        _err(request_id, "probe_failed", f"{type(exc).__name__}: {str(exc)[:200]}")


def handle_transcribe(request_id, payload):
    media_path = payload.get("mediaPath")
    model = payload.get("model", DEFAULT_MODEL)
    language = payload.get("language", DEFAULT_LANGUAGE)

    if not isinstance(media_path, str) or not media_path or not os.path.isfile(media_path):
        _err(request_id, "file_not_found", "Arquivo de mídia não encontrado.")
        return

    if not isinstance(model, str) or not MODEL_NAME_RE.match(model):
        _err(request_id, "invalid_request", "Nome de modelo inválido.")
        return

    # Nunca baixar e nunca trocar de modelo.
    if not _model_cached(model):
        _err(
            request_id,
            "model_not_available",
            f"Modelo '{model}' não está instalado no cache local. O worker não baixa modelos "
            "automaticamente. Instale manualmente conforme workers/README.md (requer autorização).",
        )
        return

    if shutil.which("ffmpeg") is None:
        _err(request_id, "ffmpeg_not_found", "ffmpeg não encontrado no PATH (necessário para decodificar o áudio).")
        return

    try:
        import torch
        import whisper
    except ImportError:
        _err(request_id, "whisper_not_installed", "openai-whisper ou torch não instalado neste ambiente Python.")
        return

    started = time.time()
    try:
        cuda = bool(torch.cuda.is_available())
        device = "cuda" if cuda else "cpu"
        lang = None if language in (None, "", "auto") else language

        # Áudio decodificado uma vez; a duração vem do próprio áudio decodificado.
        audio = whisper.load_audio(media_path)
        duration = len(audio) / float(whisper.audio.SAMPLE_RATE)

        # O arquivo já está no cache (verificado acima): load_model não baixa.
        model_obj = whisper.load_model(model, device=device)
        result = model_obj.transcribe(
            audio,
            language=lang,
            word_timestamps=True,
            fp16=cuda,
        )

        words = []
        for segment in result.get("segments") or []:
            for w in segment.get("words") or []:
                text = str(w.get("word", "")).strip()
                if not text:
                    continue
                words.append({
                    "text": text,
                    "start": round(float(w["start"]), 3),
                    "end": round(float(w["end"]), 3),
                    "probability": round(float(w["probability"]), 4),
                })

        _ok(request_id, {
            "text": str(result.get("text", "")).strip(),
            "audio_duration_secs": round(duration, 3),
            "words": words,
            "engine": f"openai-whisper {_whisper_version() or 'desconhecida'}",
            "model": model,
            "language": result.get("language") or (language or "auto"),
            "elapsedSecs": round(time.time() - started, 3),
        })
    except Exception as exc:
        _err(request_id, "transcription_failed", f"{type(exc).__name__}: {str(exc)[:300]}")


def main():
    global _proto_out
    _proto_out = sys.stdout
    sys.stdout = sys.stderr  # bibliotecas não podem poluir o protocolo

    for stream in (sys.stdin, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass

    for raw in sys.stdin:
        line = raw.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
        except json.JSONDecodeError:
            _err("unknown", "invalid_json", "Linha de comando não é JSON válido.")
            continue
        if not isinstance(req, dict):
            _err("unknown", "invalid_json", "Comando deve ser um objeto JSON.")
            continue

        request_id = str(req.get("requestId", "unknown"))
        command = req.get("command")
        payload = req.get("payload") or {}
        if not isinstance(payload, dict):
            payload = {}

        if command == "probe":
            handle_probe(request_id)
        elif command == "transcribe":
            handle_transcribe(request_id, payload)
        elif command == "exit":
            _ok(request_id, {"status": "exiting"})
            break
        else:
            _err(request_id, "unknown_command", f"Comando não reconhecido: {command}")


if __name__ == "__main__":
    main()
