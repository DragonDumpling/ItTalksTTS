"""
F5-TTS worker. Same JSON line protocol as worker.py (one command per stdin line,
one JSON response per stdout line).

F5-TTS is a zero-shot voice-cloning model: it needs a short reference audio clip
plus that clip's transcript, and clones the voice for the generated text.

IMPORTANT: torch/F5-TTS and their deps print progress and load messages to
*stdout*. That would corrupt our JSON-on-stdout protocol, so we redirect stdout to
stderr for everything and keep a private handle to the real stdout for responses.

Env:
  HF_HOME - HuggingFace cache dir (model downloads land here)
"""

from __future__ import annotations

import json
import os
import sys
import tempfile
import traceback

# Reserve the real stdout for JSON responses; send everything else to stderr so
# chatty libraries (torch, f5_tts, tqdm, huggingface) can't corrupt the protocol.
_real_stdout = sys.stdout
sys.stdout = sys.stderr


# Id of the request currently being handled; echoed back so the host can match
# responses to requests and drop stale ones (e.g. after a cancelled slow synth).
_cur_id = None


def _emit(obj: dict) -> None:
    if _cur_id is not None and "id" not in obj:
        obj["id"] = _cur_id
    _real_stdout.write(json.dumps(obj) + "\n")
    _real_stdout.flush()


def _quiet(*_args, **_kwargs) -> None:
    pass


# Keep each chunk modest so F5 processes it as a single batch. F5's own multi-batch
# chunking + cross-fade can reorder/duplicate segments (especially around em-dashes),
# so we split deterministically here and concatenate in order.
MAX_CHUNK_CHARS = 200
CHUNK_GAP_SECONDS = 0.12


def _pack(pieces, limit):
    chunks = []
    cur = ""
    for piece in pieces:
        piece = piece.strip()
        if not piece:
            continue
        if not cur:
            cur = piece
        elif len(cur) + 1 + len(piece) <= limit:
            cur += " " + piece
        else:
            chunks.append(cur)
            cur = piece
    if cur:
        chunks.append(cur)
    return chunks


def split_text(text, limit=MAX_CHUNK_CHARS):
    """Split into ordered chunks under ``limit`` chars, preferring sentence then
    clause boundaries. Normalizes dashes/newlines that confuse F5's own splitter."""
    import re

    text = " ".join(text.replace("\r", " ").replace("\n", " ").split()).strip()
    if not text:
        return []
    if len(text) <= limit:
        return [text]

    chunks = []
    for sentence in re.split(r"(?<=[.!?])\s+", text):
        sentence = sentence.strip()
        if not sentence:
            continue
        if len(sentence) <= limit:
            chunks.append(sentence)
            continue
        # Long sentence: break on clause punctuation / dashes, then words.
        clauses = re.split(r"(?<=[,;:])\s+|\s+[—–-]\s+", sentence)
        for piece in _pack(clauses, limit):
            if len(piece) <= limit:
                chunks.append(piece)
            else:
                chunks.extend(_pack(piece.split(" "), limit))
    return _pack(chunks, limit)


def _install_audio_loader_shim() -> None:
    """Make torchaudio.load read via libsndfile (the soundfile package).

    Recent torchaudio routes all I/O through torchcodec, which needs FFmpeg shared
    DLLs that we don't ship. F5 only uses torchaudio.load to read the (WAV/FLAC)
    reference clip, so we replace it with a soundfile-backed loader that returns the
    same (channels, frames) float32 tensor + sample rate. No FFmpeg required.
    """
    import numpy as np
    import soundfile as sf
    import torch
    import torchaudio

    def _load(filepath, *_args, **_kwargs):
        data, sr = sf.read(str(filepath), dtype="float32", always_2d=True)  # (frames, channels)
        return torch.from_numpy(np.ascontiguousarray(data.T)), sr  # (channels, frames)

    torchaudio.load = _load


# Lazily-loaded Whisper for auto-transcribing a reference clip when the user
# didn't supply its transcript. F5 needs ref_text to match the clip or its
# duration estimate (and thus speaking rate) goes haywire. We load audio via
# soundfile (not torchaudio/ffmpeg) and feed samples straight to the model.
_asr = None
_ref_text_cache: dict = {}


def _transcribe_ref(path: str) -> str:
    try:
        key = (path, os.path.getmtime(path))
    except OSError:
        key = (path, 0)
    if key in _ref_text_cache:
        return _ref_text_cache[key]

    global _asr
    if _asr is None:
        from transformers import WhisperForConditionalGeneration, WhisperProcessor

        proc = WhisperProcessor.from_pretrained("openai/whisper-tiny.en")
        model = WhisperForConditionalGeneration.from_pretrained("openai/whisper-tiny.en")
        _asr = (proc, model)
    proc, model = _asr

    import librosa
    import soundfile as sf
    import torch

    data, sr = sf.read(path, dtype="float32")
    if getattr(data, "ndim", 1) > 1:
        data = data.mean(axis=1)
    if sr != 16000:
        data = librosa.resample(data, orig_sr=sr, target_sr=16000)
    feats = proc(data, sampling_rate=16000, return_tensors="pt").input_features
    with torch.no_grad():
        ids = model.generate(feats, max_new_tokens=200)
    text = proc.batch_decode(ids, skip_special_tokens=True)[0].strip()
    _ref_text_cache[key] = text
    return text


def main() -> None:
    f5 = None

    def ensure_model():
        nonlocal f5
        if f5 is None:
            _install_audio_loader_shim()
            from f5_tts.api import F5TTS

            # device=None lets F5TTS pick cuda when available, else cpu.
            f5 = F5TTS()
        return f5

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
                _emit({"ok": True, "voiceMode": "referenceAudio", "voices": []})
            elif cmd == "synthesize":
                text = req.get("text") or ""
                ref_audio = req.get("refAudio") or ""
                ref_text = req.get("refText") or ""
                speed = float(req.get("speed") or 1.0)
                if not text:
                    _emit({"ok": False, "error": "empty text"})
                    continue
                if not ref_audio or not os.path.isfile(ref_audio):
                    _emit({"ok": False, "error": f"reference audio not found: {ref_audio}"})
                    continue

                model = ensure_model()
                if not ref_text.strip():
                    ref_text = _transcribe_ref(ref_audio)
                    print(f"[f5] auto-transcribed reference: {ref_text[:70]!r}", file=sys.stderr, flush=True)
                import numpy as np
                import soundfile as sf

                # Split deterministically and synthesize each chunk as its own batch,
                # then concatenate in order. show_info -> no-op so F5 can't print to
                # (the redirected) stdout. speed<1.0 slows the delivery down.
                chunks = split_text(text) or [text]
                parts = []
                sample_rate = None
                gap = None
                for chunk in chunks:
                    wav, sr, _ = model.infer(
                        ref_file=ref_audio,
                        ref_text=ref_text,
                        gen_text=chunk,
                        speed=speed,
                        show_info=_quiet,
                    )
                    sample_rate = sr
                    wav = np.asarray(wav, dtype=np.float32)
                    if gap is None:
                        gap = np.zeros(int(sr * CHUNK_GAP_SECONDS), dtype=np.float32)
                    if parts:
                        parts.append(gap)
                    parts.append(wav)

                audio = np.concatenate(parts) if parts else np.zeros(0, dtype=np.float32)
                fd, wav_path = tempfile.mkstemp(suffix=".wav")
                os.close(fd)
                sf.write(wav_path, audio, sample_rate or 24000)
                _emit({"ok": True, "wav": wav_path})
            else:
                _emit({"ok": False, "error": f"unknown cmd: {cmd}"})
        except Exception:
            _emit({"ok": False, "error": traceback.format_exc()})


if __name__ == "__main__":
    main()
