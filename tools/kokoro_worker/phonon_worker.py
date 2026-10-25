"""
Phonon 31m worker. Same JSON line protocol as worker.py (one command per stdin
line, one JSON response per stdout line).

Phonon is Gradium's on-device English TTS (Apache 2.0 weights, Rust runtime via
the `ptts` package). It does not use PyTorch. The first load downloads the q8
checkpoint and, on demand, the named voice from the Gradium catalog.

IMPORTANT: download progress and library logs must not land on stdout — that
pipe is reserved for JSON responses.

Env:
  HF_HOME        - Hugging Face cache (model + voice downloads land here)
  PHONON_MODEL   - optional override (default: ptts.DEFAULT_MODEL, Phonon 31m)
  PHONON_LANG    - normalization language (this checkpoint is English: en)
  PHONON_QUANT   - weight format, default q8
  PHONON_DEVICE  - default cpu
  PHONON_VOICE   - voice used when a request omits one (default Easton)
"""

from __future__ import annotations

import json
import os
import re
import sys
import tempfile
import traceback

# Reserve the real stdout for JSON responses; send everything else to stderr so
# Hugging Face progress and the runtime can't corrupt the protocol.
_real_stdout = sys.stdout
sys.stdout = sys.stderr

# Id of the request currently being handled; echoed back so the host can match
# responses to requests and drop stale ones (e.g. after a cancelled slow synth).
_cur_id = None

DEFAULT_VOICE = os.environ.get("PHONON_VOICE") or "Easton"

# Pauses at newline boundaries the preprocessing layer chose. A single newline
# is a sentence beat; a blank line (2+ newlines) is a paragraph beat.
SENTENCE_GAP_SECONDS = 0.22
PARAGRAPH_GAP_SECONDS = 0.45

# English catalog (gradium/phonon-voices) used when the catalog file isn't cached
# yet. Order matches the published catalog: US, UK, then character voices.
FALLBACK_ENGLISH_VOICES = [
    "Zoey",
    "Juniper",
    "Everly",
    "Harper",
    "Hadley",
    "Delia",
    "Easton",
    "Micah",
    "Trevor",
    "Russell",
    "Preston",
    "Miles",
    "Maeve",
    "Freya",
    "Pippa",
    "Elodie-Rose",
    "Imogen",
    "Lottie",
    "Bryony",
    "Alfie",
    "Hugh",
    "Edwin",
    "Spencer",
    "Toby",
    "Teddy",
    "Captain Silas Blackwater",
    "Morwenna of the Deep",
    "Old Mortlake",
    "Azgorath",
    "Fennick the Young Elf",
    "The Thing Under the Stairs",
    "Vyrmathrax the Ancient",
    "Grub the Ogre",
    "Master Aldric",
    "The Snow Queen",
    "Sir Bramwell",
    "Fairy Godmother",
]

# Kokoro ids (af_sarah, bm_george, …) share the SelectedVoice setting. They are
# not Phonon voices; substitute the default instead of failing the clip.
_KOKORO_VOICE = re.compile(
    r"^(?:af|am|bf|bm|ef|em|ff|hf|hm|if|im|jf|jm|pf|pm|zf|zm)_[a-z0-9_]+$",
    re.IGNORECASE,
)

_voice_cache: list[str] | None = None


def _emit(obj: dict) -> None:
    if _cur_id is not None and "id" not in obj:
        obj["id"] = _cur_id
    _real_stdout.write(json.dumps(obj) + "\n")
    _real_stdout.flush()


def duration_delta(speed: float) -> float:
    """Map the host's speed multiplier onto Phonon's duration_delta.

    1.0 is natural. Above 1 speaks faster (negative delta); below 1 speaks
    slower. Clamped so an extreme value can't stall generation. -0.3 is the
    runtime's documented "a bit faster" example, which lines up with speed 1.3.
    """
    try:
        speed_f = float(speed)
    except (TypeError, ValueError):
        speed_f = 1.0
    if speed_f <= 0:
        speed_f = 1.0
    return max(-1.0, min(1.0, 1.0 - speed_f))


def is_kokoro_voice(voice: str) -> bool:
    return bool(_KOKORO_VOICE.match(voice or ""))


def _is_english(lang: str) -> bool:
    lang = (lang or "").lower().replace("_", "-").strip()
    return lang in {"en", "english"} or lang.startswith("en-")


def _english_voice_names() -> list[str]:
    """English voices from the catalog, falling back to the built-in list."""
    global _voice_cache
    if _voice_cache:
        return list(_voice_cache)

    import ptts

    names: list[str] = []
    seen: set[str] = set()
    try:
        rows = ptts.catalog_voices()
    except Exception as exc:
        print(f"[phonon] voice catalog unavailable ({exc}); using built-in English list", file=sys.stderr)
        rows = None
    for row in rows or []:
        if not isinstance(row, dict) or not _is_english(str(row.get("language") or "")):
            continue
        name = str(row.get("name") or "").strip()
        if name and name not in seen:
            seen.add(name)
            names.append(name)
    if names:
        _voice_cache = names
        return list(names)
    return list(FALLBACK_ENGLISH_VOICES)


def _apply_rate(tts, speed: float) -> None:
    setter = getattr(tts, "set_conditions", None)
    if setter is None:
        return
    setter({"duration_delta": duration_delta(speed)})


def _resolve_voice(voice: str | None) -> str:
    voice = (voice or "").strip()
    if not voice or is_kokoro_voice(voice):
        if voice and is_kokoro_voice(voice):
            print(f"[phonon] {voice!r} is a Kokoro voice; using {DEFAULT_VOICE}", file=sys.stderr)
        return DEFAULT_VOICE
    return voice


def _write_wav(path: str, samples, sample_rate: int) -> None:
    import numpy as np
    import wave

    pcm = np.asarray(samples, dtype=np.float32).reshape(-1)
    ints = np.clip(pcm, -1.0, 1.0)
    ints = (ints * 32767.0).astype("<i2")
    with wave.open(path, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(int(sample_rate))
        wf.writeframes(ints.tobytes())


def _synthesize(tts, text: str, voice: str, speed: float):
    """Speak text, keeping the pauses preprocessing put on newline boundaries.

    Within a block, Phonon splits sentences itself. Blocks are concatenated in
    order with a short beat between them.
    """
    import numpy as np

    _apply_rate(tts, speed)
    sample_rate = int(tts.sample_rate)
    segments = re.split(r"(\n+)", text)
    parts: list = []
    first = True
    for j, tok in enumerate(segments):
        if j % 2 == 1:
            continue
        content = tok.strip()
        if not content:
            continue
        wav = np.asarray(tts.synth(content, voice=voice), dtype=np.float32).reshape(-1)
        if wav.size == 0:
            continue
        if not first:
            sep = segments[j - 1] if j else ""
            gap_s = PARAGRAPH_GAP_SECONDS if len(sep) >= 2 else SENTENCE_GAP_SECONDS
            parts.append(np.zeros(int(sample_rate * gap_s), dtype=np.float32))
        parts.append(wav)
        first = False
    if not parts:
        return None
    return np.concatenate(parts), sample_rate


def main() -> None:
    tts = None

    def ensure_model():
        nonlocal tts
        if tts is None:
            import ptts

            model = os.environ.get("PHONON_MODEL") or ptts.DEFAULT_MODEL
            lang = os.environ.get("PHONON_LANG") or "en"
            quant = os.environ.get("PHONON_QUANT") or "q8"
            device = os.environ.get("PHONON_DEVICE") or "cpu"
            print(f"[phonon] loading {model} lang={lang} quant={quant} device={device}", file=sys.stderr, flush=True)
            tts = ptts.TTS(model, lang=lang, quant=quant, device=device)
        return tts

    global _cur_id
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        _cur_id = None
        try:
            req = json.loads(line)
        except json.JSONDecodeError as e:
            _emit({"ok": False, "error": f"invalid json: {e}"})
            continue

        _cur_id = req.get("id")
        cmd = req.get("cmd")
        try:
            if cmd == "ping":
                ensure_model()
                _emit({"ok": True, "voiceMode": "namedVoices", "voices": _english_voice_names()})
            elif cmd == "synthesize":
                text = req.get("text") or ""
                voice = _resolve_voice(req.get("voice"))
                speed = req.get("speed") if req.get("speed") is not None else 1.0
                if not text.strip():
                    _emit({"ok": False, "error": "empty text"})
                    continue
                model = ensure_model()
                audio = _synthesize(model, text, voice, speed)
                if audio is None:
                    _emit({"ok": False, "error": "no speakable text"})
                    continue
                samples, sample_rate = audio
                fd, wav_path = tempfile.mkstemp(suffix=".wav")
                os.close(fd)
                _write_wav(wav_path, samples, sample_rate)
                _emit({"ok": True, "wav": wav_path})
            else:
                _emit({"ok": False, "error": f"unknown cmd: {cmd}"})
        except Exception:
            _emit({"ok": False, "error": traceback.format_exc()})


if __name__ == "__main__":
    main()
