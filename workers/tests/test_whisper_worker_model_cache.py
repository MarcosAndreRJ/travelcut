"""P10: o modelo Whisper é carregado uma vez por processo/lote. Roda sem openai-whisper: python -m unittest discover -s tests -v (em workers/)."""

import os
import sys
import tempfile
import types
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import whisper_worker as ww  # noqa: E402
from test_whisper_worker import CapturedOutput  # noqa: E402


class _FakeModel:
    def transcribe(self, audio, **kwargs):
        return {
            "text": " oi",
            "language": "pt",
            "segments": [{"words": [{"word": " oi", "start": 0.0, "end": 0.5, "probability": 0.9}]}],
        }


class ModelCacheTests(unittest.TestCase):
    def setUp(self):
        self.loads = []
        self.cuda = True

        fake_whisper = types.ModuleType("whisper")
        fake_whisper.audio = types.SimpleNamespace(SAMPLE_RATE=16000)
        fake_whisper.load_audio = lambda path: [0.0] * 16000

        def load_model(name, device=None):
            self.loads.append((name, device))
            return _FakeModel()

        fake_whisper.load_model = load_model
        fake_torch = types.ModuleType("torch")
        fake_torch.cuda = types.SimpleNamespace(is_available=lambda: self.cuda)

        self._saved_modules = {k: sys.modules.get(k) for k in ("whisper", "torch")}
        sys.modules["whisper"] = fake_whisper
        sys.modules["torch"] = fake_torch
        self._saved_attrs = (ww._model_cached, ww.shutil.which)
        ww._model_cached = lambda name: True
        ww.shutil.which = lambda name: "ffmpeg"
        ww._MODEL_CACHE.clear()

        handle, self.media = tempfile.mkstemp(suffix=".wav")
        os.close(handle)

    def tearDown(self):
        ww._model_cached, ww.shutil.which = self._saved_attrs
        for key, value in self._saved_modules.items():
            if value is None:
                sys.modules.pop(key, None)
            else:
                sys.modules[key] = value
        ww._MODEL_CACHE.clear()
        os.remove(self.media)

    def _transcribe(self, request_id, device=None):
        payload = {"mediaPath": self.media, "model": "large-v3", "language": "pt"}
        if device:
            payload["device"] = device
        with CapturedOutput() as out:
            ww.handle_transcribe(request_id, payload)
        return [m for m in out.messages() if "ok" in m]

    def test_three_media_load_the_model_once(self):
        results = [self._transcribe(f"req-{i}") for i in range(3)]

        for (message,) in results:
            self.assertTrue(message["ok"])
            self.assertEqual(message["result"]["model"], "large-v3")
        self.assertEqual(self.loads, [("large-v3", "cuda")])

    def test_device_change_reloads_the_same_model_once(self):
        self._transcribe("req-1")
        self._transcribe("req-2", device="cpu")
        self._transcribe("req-3", device="cpu")

        self.assertEqual(self.loads, [("large-v3", "cuda"), ("large-v3", "cpu")])


if __name__ == "__main__":
    unittest.main()
