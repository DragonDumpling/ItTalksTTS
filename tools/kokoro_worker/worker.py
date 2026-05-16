"""
JSON line protocol over stdin/stdout.
Each input line: JSON command. Each output line: JSON response.

Env:
  KOKORO_MODEL - path to kokoro-v1.0.onnx
  KOKORO_VOICES - path to voices-v1.0.bin
"""

from __future__ import annotations

import json
import os
import sys
import traceback

DEFAULT_VOICES = [
    "af_sarah",
    "af_sky",
    "am_adam",
    "bf_emma",
    "bm_george",
]


def main() -> None:
    model = os.environ.get("KOKORO_MODEL", "kokoro-v1.0.onnx")
    voices_path = os.environ.get("KOKORO_VOICES", "voices-v1.0.bin")
    kokoro = None

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
        except json.JSONDecodeError as e:
            _emit({"ok": False, "error": f"invalid json: {e}"})
            continue

        cmd = req.get("cmd")
        try:
            if cmd == "ping":
                if not os.path.isfile(model) or not os.path.isfile(voices_path):
                    _emit(
                        {
                            "ok": False,
                            "error": f"Model files missing. model={model} voices={voices_path}",
                            "voices": DEFAULT_VOICES,
                        }
                    )
                    continue
                if kokoro is None:
                    from kokoro_onnx import Kokoro

                    kokoro = Kokoro(model, voices_path)
                _emit({"ok": True, "voices": list(DEFAULT_VOICES)})
            elif cmd == "synthesize":
                text = req.get("text") or ""
                voice = req.get("voice") or "af_sarah"
                lang = req.get("lang") or "en-us"
                speed = float(req.get("speed") or 1.0)
                if not text:
                    _emit({"ok": False, "error": "empty text"})
                    continue
                if not os.path.isfile(model) or not os.path.isfile(voices_path):
                    _emit({"ok": False, "error": "model files missing"})
                    continue
                if kokoro is None:
                    from kokoro_onnx import Kokoro

                    kokoro = Kokoro(model, voices_path)
                import soundfile as sf
                import tempfile

                samples, sample_rate = kokoro.create(
                    text, voice=voice, speed=speed, lang=lang
                )
                fd, wav_path = tempfile.mkstemp(suffix=".wav")
                os.close(fd)
                sf.write(wav_path, samples, sample_rate)
                _emit({"ok": True, "wav": wav_path})
            else:
                _emit({"ok": False, "error": f"unknown cmd: {cmd}"})
        except Exception:
            _emit({"ok": False, "error": traceback.format_exc()})


def _emit(obj: dict) -> None:
    sys.stdout.write(json.dumps(obj) + "\n")
    sys.stdout.flush()


if __name__ == "__main__":
    main()
