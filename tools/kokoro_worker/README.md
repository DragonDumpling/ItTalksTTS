# Kokoro worker

Placed under `%LocalAppData%\ItTalksTTS\kokoro_worker` by setup, or run from repo for development.

Requires `kokoro-v1.0.onnx` and `voices-v1.0.bin` (see ItTalks in-app Setup).

```bash
pip install -r requirements.txt
set KOKORO_MODEL=path\to\kokoro-v1.0.onnx
set KOKORO_VOICES=path\to\voices-v1.0.bin
python worker.py
```

Send JSON lines on stdin; read JSON lines from stdout.
