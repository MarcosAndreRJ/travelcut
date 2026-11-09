#!/usr/bin/env python3
"""
TravelCut Studio - worker Whisper (protocolo "travelcut.whisper" v1).

Transporte: NDJSON (uma mensagem JSON por linha) em stdin/stdout.
Comandos: "probe", "transcribe", "exit".

Durante "transcribe" o worker também emite eventos de telemetria, sem o campo "ok"
(a resposta final continua sendo a única mensagem com "ok"):
  {"protocol", "version", "requestId", "event": "progress"|"heartbeat", "stage", "percent"}
- stage: loading_audio | loading_model | transcribing.
- percent: avanço real da decodificação (quadros processados / total), ou null quando desconhecido.
- heartbeat: processo vivo a cada HEARTBEAT_SECONDS; não prova avanço. O áudio nunca é dividido para gerar percentual.

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
import threading
import time

PROTOCOL = "travelcut.whisper"
PROTOCOL_VERSION = 1
DEFAULT_MODEL = "large-v3"
DEFAULT_LANGUAGE = "pt"
MODEL_NAME_RE = re.compile(r"^[A-Za-z0-9._-]{1,64}$")
HEARTBEAT_SECONDS = 10

# Canal de protocolo: guardado antes de redirecionar sys.stdout para sys.stderr.
_proto_out = sys.stdout
_emit_lock = threading.Lock()


def _emit(message):
    line = json.dumps(message, ensure_ascii=True) + "\n"
    with _emit_lock:
        _proto_out.write(line)
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


def _progress_percent(done, total):
    """Percentual real, ou None sem total válido. Nunca passa de 100."""
    if not total or total <= 0 or done < 0:
        return None
    return round(min(100.0, done * 100.0 / total), 1)


class _Telemetry:
    """Etapa atual do transcribe, publicada como eventos e repetida pelo heartbeat."""

    def __init__(self, request_id):
        self.request_id = request_id
        self.stage = "loading_audio"
        self.percent = None
        self._stop = threading.Event()
        self._thread = None

    def _send(self, event):
        _emit({
            "protocol": PROTOCOL,
            "version": PROTOCOL_VERSION,
            "requestId": self.request_id,
            "event": event,
            "stage": self.stage,
            "percent": self.percent,
        })

    def report(self, stage, percent=None):
        self.stage = stage
        self.percent = percent
        self._send("progress")

    def _beat(self):
        while not self._stop.wait(HEARTBEAT_SECONDS):
            self._send("heartbeat")

    def start(self):
        self._thread = threading.Thread(target=self._beat, daemon=True)
        self._thread.start()

    def stop(self):
        # Antes da resposta final: nenhum evento depois do "ok".
        self._stop.set()
        if self._thread is not None:
            self._thread.join(timeout=2)


def _install_progress_hook(telemetry):
    """
    Liga a barra interna do openai-whisper à telemetria. Devolve a função que desfaz a troca.
    Depende de um detalhe interno (whisper.transcribe usa tqdm.tqdm com total em quadros): se o pacote
    mudar, não há percentual, apenas heartbeat. Falha aqui nunca interrompe a transcrição.
    """
    try:
        module = sys.modules.get("whisper.transcribe")
        original = getattr(module, "tqdm", None)
        base = getattr(original, "tqdm", None)
        if base is None:
            return lambda: None

        class _ReportingBar(base):
            def __init__(self, *args, **kwargs):
                super().__init__(*args, **kwargs)
                self._tc_total = kwargs.get("total")
                self._tc_done = 0

            def update(self, n=1):
                result = super().update(n)
                self._tc_done += n or 0
                percent = _progress_percent(self._tc_done, self._tc_total)
                if percent is not None:
                    telemetry.report("transcribing", percent)
                return result

        class _TqdmModule:
            tqdm = _ReportingBar

        module.tqdm = _TqdmModule

        def restore():
            module.tqdm = original

        return restore
    except Exception:
        return lambda: None


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
    telemetry = _Telemetry(request_id)
    restore_hook = lambda: None
    try:
        telemetry.start()
        telemetry.report("loading_audio")
        # P12: payload.device == "cpu" força CPU (fallback após falta de memória da GPU); o modelo nunca muda.
        cuda = bool(torch.cuda.is_available()) and payload.get("device") != "cpu"
        device = "cuda" if cuda else "cpu"
        lang = None if language in (None, "", "auto") else language

        # Áudio decodificado uma vez; a duração vem do próprio áudio decodificado.
        audio = whisper.load_audio(media_path)
        duration = len(audio) / float(whisper.audio.SAMPLE_RATE)

        # O arquivo já está no cache (verificado acima): load_model não baixa.
        telemetry.report("loading_model")
        model_obj = whisper.load_model(model, device=device)

        telemetry.report("transcribing")
        restore_hook = _install_progress_hook(telemetry)
        result = model_obj.transcribe(
            audio,
            language=lang,
            word_timestamps=True,
            fp16=cuda,
        )
        restore_hook()
        restore_hook = lambda: None
        telemetry.stop()

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
        restore_hook()
        telemetry.stop()
        if "out of memory" in str(exc).lower() or type(exc).__name__ == "OutOfMemoryError":
            _err(request_id, "cuda_out_of_memory", f"{type(exc).__name__}: {str(exc)[:300]}")
            return
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
