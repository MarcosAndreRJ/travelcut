"""Telemetria do worker Whisper. Roda sem openai-whisper instalado: python -m unittest discover -s tests -v (em workers/)."""

import io
import json
import os
import sys
import types
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import whisper_worker as ww  # noqa: E402


class CapturedOutput:
    def __enter__(self):
        self.buffer = io.StringIO()
        self.previous = ww._proto_out
        ww._proto_out = self.buffer
        return self

    def __exit__(self, *exc):
        ww._proto_out = self.previous

    def messages(self):
        return [json.loads(line) for line in self.buffer.getvalue().splitlines() if line]


class ProgressPercentTests(unittest.TestCase):
    def test_real_percent(self):
        self.assertEqual(ww._progress_percent(250, 1000), 25.0)

    def test_unknown_total_has_no_percent(self):
        self.assertIsNone(ww._progress_percent(10, None))
        self.assertIsNone(ww._progress_percent(10, 0))

    def test_never_exceeds_100(self):
        self.assertEqual(ww._progress_percent(1500, 1000), 100.0)


class TelemetryTests(unittest.TestCase):
    def test_event_has_request_id_and_no_ok_field(self):
        with CapturedOutput() as out:
            ww._Telemetry("req-1").report("transcribing", 12.5)

        (message,) = out.messages()
        self.assertEqual(message["requestId"], "req-1")
        self.assertEqual(message["event"], "progress")
        self.assertEqual(message["stage"], "transcribing")
        self.assertEqual(message["percent"], 12.5)
        self.assertNotIn("ok", message)

    def test_stop_ends_heartbeat_thread(self):
        with CapturedOutput():
            telemetry = ww._Telemetry("req-1")
            telemetry.start()
            telemetry.stop()
        self.assertFalse(telemetry._thread.is_alive())


class ProgressHookTests(unittest.TestCase):
    def setUp(self):
        class FakeBar:
            def __init__(self, *args, **kwargs):
                self.closed = False

            def update(self, n=1):
                return None

            def __enter__(self):
                return self

            def __exit__(self, *exc):
                return False

        self.fake_tqdm = types.SimpleNamespace(tqdm=FakeBar)
        self.module = types.ModuleType("whisper.transcribe")
        self.module.tqdm = self.fake_tqdm
        sys.modules["whisper.transcribe"] = self.module

    def tearDown(self):
        sys.modules.pop("whisper.transcribe", None)

    def test_bar_updates_become_real_percent_and_hook_restores(self):
        with CapturedOutput() as out:
            restore = ww._install_progress_hook(ww._Telemetry("req-1"))
            with self.module.tqdm.tqdm(total=200, unit="frames", disable=True) as bar:
                bar.update(50)
                bar.update(50)
            restore()

        self.assertEqual([m["percent"] for m in out.messages()], [25.0, 50.0])
        self.assertIs(self.module.tqdm, self.fake_tqdm)

    def test_missing_internal_tqdm_means_no_percent_and_no_failure(self):
        self.module.tqdm = None
        restore = ww._install_progress_hook(ww._Telemetry("req-1"))
        restore()
        self.assertIsNone(self.module.tqdm)


if __name__ == "__main__":
    unittest.main()
