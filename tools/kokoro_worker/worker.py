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
import re
import sys
import traceback

DEFAULT_VOICES = [
    "af_sarah",
    "af_sky",
    "am_adam",
    "bf_emma",
    "bm_george",
]

# Kokoro caps a single synthesis call at 510 phonemes (MAX_PHONEME_LENGTH). Worse,
# its built-in splitter only breaks on punctuation, so a long punctuation-free run
# (hex codes, file paths, IDs) overflows, gets truncated to exactly 510, and then
# crashes on `voice[len(tokens)]` (IndexError: index 510 out of bounds, size 510).
#
# We avoid all of that by phonemizing ourselves and splitting the phoneme stream
# into batches safely under the cap, then synthesizing each batch with
# is_phonemes=True so Kokoro never re-splits or truncates. PHONEME_BATCH leaves a
# margin below 510 because the crashing index is len(tokens), which must stay <= 509.
PHONEME_BATCH = 500

# Character budget used only by the fallback path (when the phoneme API is
# unavailable for some reason); deliberately conservative.
MAX_CHUNK_CHARS = 320

# Silence inserted between batches so concatenated speech doesn't run together.
CHUNK_GAP_SECONDS = 0.18


def split_phoneme_batches(phonemes: str, limit: int = PHONEME_BATCH) -> list[str]:
    """Split a phoneme string into batches no longer than ``limit`` characters.

    Breaks on spaces (espeak separates words with spaces); a single run longer
    than the limit is hard-sliced so no batch can ever exceed it.
    """
    phonemes = phonemes.strip()
    if not phonemes:
        return []
    if len(phonemes) <= limit:
        return [phonemes]

    batches: list[str] = []
    cur = ""
    for word in phonemes.split(" "):
        if not word:
            continue
        while len(word) > limit:
            if cur:
                batches.append(cur)
                cur = ""
            batches.append(word[:limit])
            word = word[limit:]
        if not cur:
            cur = word
        elif len(cur) + 1 + len(word) <= limit:
            cur += " " + word
        else:
            batches.append(cur)
            cur = word
    if cur:
        batches.append(cur)
    return batches


def _pack(pieces: list[str]) -> list[str]:
    """Greedily combine pieces into chunks no longer than MAX_CHUNK_CHARS."""
    chunks: list[str] = []
    cur = ""
    for piece in pieces:
        piece = piece.strip()
        if not piece:
            continue
        if not cur:
            cur = piece
        elif len(cur) + 1 + len(piece) <= MAX_CHUNK_CHARS:
            cur += " " + piece
        else:
            chunks.append(cur)
            cur = piece
    if cur:
        chunks.append(cur)
    return chunks


def _hard_wrap(text: str) -> list[str]:
    """Last resort: wrap on word boundaries (or raw slices for giant tokens)."""
    out: list[str] = []
    cur = ""
    for word in text.split():
        while len(word) > MAX_CHUNK_CHARS:
            # A single token longer than the cap (e.g. a long URL/hash): slice it.
            if cur:
                out.append(cur)
                cur = ""
            out.append(word[:MAX_CHUNK_CHARS])
            word = word[MAX_CHUNK_CHARS:]
        if not cur:
            cur = word
        elif len(cur) + 1 + len(word) <= MAX_CHUNK_CHARS:
            cur += " " + word
        else:
            out.append(cur)
            cur = word
    if cur:
        out.append(cur)
    return out


def split_text(text: str) -> list[str]:
    """Split text into chunks that stay safely under Kokoro's phoneme limit.

    Prefers sentence boundaries, falls back to clause punctuation, then to word
    wrapping for runs with no usable breaks.
    """
    text = text.strip()
    if len(text) <= MAX_CHUNK_CHARS:
        return [text] if text else []

    chunks: list[str] = []
    sentences = re.split(r"(?<=[.!?])\s+", text)
    for sentence in sentences:
        sentence = sentence.strip()
        if not sentence:
            continue
        if len(sentence) <= MAX_CHUNK_CHARS:
            chunks.append(sentence)
            continue
        # Sentence itself is too long: break on clause punctuation, then words.
        clauses = re.split(r"(?<=[,;:])\s+", sentence)
        packed = _pack(clauses)
        for piece in packed:
            if len(piece) <= MAX_CHUNK_CHARS:
                chunks.append(piece)
            else:
                chunks.extend(_hard_wrap(piece))

    # Re-pack adjacent short sentences to cut down on the number of synth calls.
    return _pack(chunks)


def _render(kokoro, batches: list[str], voice: str, speed: float, lang: str, is_phonemes: bool):
    import numpy as np

    audio_parts: list = []
    sample_rate = None
    gap = None
    for batch in batches:
        if not batch.strip():
            continue
        samples, sr = kokoro.create(
            batch, voice=voice, speed=speed, lang=lang, is_phonemes=is_phonemes
        )
        sample_rate = sr
        if gap is None:
            gap = np.zeros(int(sr * CHUNK_GAP_SECONDS), dtype=samples.dtype)
        if audio_parts:
            audio_parts.append(gap)
        audio_parts.append(samples)

    if not audio_parts:
        return None, None
    return np.concatenate(audio_parts), sample_rate


def _synthesize(kokoro, text: str, voice: str, speed: float, lang: str):
    """Synthesize arbitrarily long text into one audio clip.

    Primary path: phonemize ourselves and batch the phoneme stream under the
    510-token cap, rendering each batch with is_phonemes=True. This avoids the
    upstream IndexError on long punctuation-free runs. Falls back to character
    based text chunking only if the phoneme API is unavailable.
    """
    try:
        phonemes = kokoro.tokenizer.phonemize(text, lang)
        batches = split_phoneme_batches(phonemes, PHONEME_BATCH)
        if not batches:
            return None, None
        return _render(kokoro, batches, voice, speed, lang, is_phonemes=True)
    except Exception:
        # Fall back to text-level chunking (plain create()) if anything in the
        # phoneme path is unavailable; still better than one oversized call.
        chunks = split_text(text)
        if not chunks:
            return None, None
        return _render(kokoro, chunks, voice, speed, lang, is_phonemes=False)


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

                samples, sample_rate = _synthesize(kokoro, text, voice, speed, lang)
                if samples is None:
                    _emit({"ok": False, "error": "no speakable text after splitting"})
                    continue
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
