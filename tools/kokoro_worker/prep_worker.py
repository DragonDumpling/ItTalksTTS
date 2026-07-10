"""
Preprocessing worker for ItTalksTTS.

JSON-line protocol over stdin/stdout (mirrors the TTS workers). Loads a small GGUF
model with llama-cpp-python and rewrites incoming text into a form that sounds
natural when spoken by a TTS engine — stripping markdown/code/emoji, expanding
numbers/dates/abbreviations to spoken form, replacing URLs/API keys/hashes with
short spoken descriptions, and tightening verbose text. The original queue text is
untouched; only the string handed to the TTS engine is rewritten.

Env:
  ITTALKS_PREP_MODEL     - path to the GGUF model file
  ITTALKS_PREP_CHAT_FMT  - optional chat format hint (otherwise auto-detected)
  ITTALKS_PREP_NCTX      - optional context window override (default 4096)
  ITTALKS_PREP_THREADS   - optional thread count override (default cpu count)
"""

from __future__ import annotations

import json
import os
import re
import sys
import traceback

DEFAULT_NCTX = 4096

SYSTEM_PROMPT = """You rewrite text so it sounds natural when read aloud by a text-to-speech system. The input comes from tools like coding assistants (Cursor, Claude, ChatGPT) and is written to be READ on screen, not spoken. Your job is to make it pleasant to LISTEN to — and to pace it so the listener can absorb it.

You MUST follow every rule:

1. Output ONLY the rewritten text. No preamble, no labels, no quotes around it, no "Here is...". Just the words to be spoken.

2. Strip everything that is visual formatting:
   - All markdown: headings, bold (**), italics (*), inline code (`), bullet/numbered list markers, blockquotes, tables, horizontal rules.
   - Emojis, emoticons, and decorative symbols.
   - Raw JSON, YAML, TOML, XML, HTML tags, shell prompts ($, >), and box-drawing characters.

3. NEVER transcribe source code. If a code block appears (an "[a code block]" marker, or any fenced/inline code that slipped through), either describe what it does in ONE short plain sentence, or omit it entirely when the surrounding prose already explains it. Never read statements, braces, keywords, operators, semicolons, angle brackets, or any punctuation from code. The listener cannot follow code read aloud character by character.

4. Write numbers, dates, times, versions, and units in SPOKEN form:
   - 23 -> twenty-three; 1,234 -> one thousand two hundred thirty-four
   - 3/15/2026 -> March fifteenth, twenty twenty-six
   - 7:00 PM -> seven PM; v3.2 -> version three point two; 99% -> ninety-nine percent
   - $19.99 -> nineteen dollars and ninety-nine cents

5. Handle technical noise the way a human narrator would:
   - URLs: replace with "a link" unless the destination is itself the point, in which case say it as components ("github dot com slash inworld dash ai").
   - API keys, tokens, hashes, base64 blobs, hex strings, UUIDs, long commit ids: REPLACE with a short spoken label such as "an API key", "a token", "a hash", "a commit id". Never read the characters.
   - File paths: shorten to the meaningful filename only (e.g. "the build pipeline file" or "playback dot cs"), never the full path. NEVER invent or expand a path the way it was written.
   - Line ranges like "588-602" or "588:602": say "lines 588 to 602" (in spoken form). Never read digits with colons.
   - Code identifiers: SPLIT camelCase/PascalCase/snake_case into words AND SHORTEN to the meaningful core. Drop boilerplate suffixes (Resolver, Manager, Helper, Factory, Info, Settings, Impl, Util, Utils, Handler, Controller, Service, Adapter) when the rest of the name is already clear. Speak the result as words, not as the raw identifier.
     Examples — apply this kind of shortening:
       - "CombatKillCardInfoResolver.TryResolveKillerNetworkPlayer(victim)" -> "the resolve killer player call"
       - "victim.LastHostileDamageSettings" -> "last hostile damage"
       - "getUserSessionTokenFromCookie(req)" -> "get user session token"
       - "AuthenticationTokenProviderImpl" -> "auth token provider"
       - "combatKillCardInfoResolver" -> "the kill card resolver"
   - ALL-CAPS constants like E2_PERSISTENCE: speak as words ("E two persistence") or as what they mean ("the persistence flag"). Never read the underscores.
   - Arrows (-> or a right arrow): say "to" or "then" depending on context.
   - Acronyms: spell out on first use if obscure ("A.W.S., or Amazon Web Services"); say common ones as words if pronounceable (NASA) or spell them if not (A-P-I).

6. Pace the speech for listening. Think about where the listener needs a beat to absorb what just came:
   - Break long, nested sentences into two or three short ones, each ending in a period.
   - Put a comma where a natural breath falls in a long sentence.
   - Put a single line break between sentences for a normal pause.
   - Put a BLANK LINE (two line breaks) at every section or topic shift, after a section title, before a key conclusion or recommendation, and between enumerated items — anywhere the listener should get a longer beat.
   - When the source has section headers, speak the title as a short sentence ending with a period, then a blank line, then the content. Example: "Recommendation. <blank line> Don't commit this change."

7. Make it sound natural and a bit shorter:
   - Use contractions (don't, can't, I'm, we're).
   - Drop filler phrases that don't add meaning when spoken ("It's worth noting that", "As an AI language model", "Let me know if you'd like me to", "I hope this helps").
   - Keep the meaning and all concrete facts. Never invent details, never delete information the user needs.
   - Keep plain prose: full sentences ending in a period, question mark, or exclamation. No bullet lists, no headers (other than the spoken section titles in rule 6).

8. If the input is already clean, conversational, and easy to listen to, return it essentially unchanged. Do not over-edit short, natural sentences.

9. Keep the same language as the input. If the input is mostly English, output English. If it's mostly another language, rewrite in that language using the same rules.

10. Preserve proper nouns and names. If a name is genuinely hard to pronounce, leave it as written — the TTS engine will attempt it."""


# ---------------------------------------------------------------------------
# Deterministic pre/post passes — guarantee relief even when the model slips.
# ---------------------------------------------------------------------------

# A single "word" 32+ chars with no spaces is almost never meant to be read
# aloud: a URL, an API key, a hash, a base64 blob, a long path, a UUID.
_LONG_TOKEN = re.compile(r"\S{32,}")

_URLISH = re.compile(r"^(https?://|www\.|ftp://|ssh://|git@)", re.IGNORECASE)
_HEX_BLOB = re.compile(r"^[0-9a-fA-F]{32,}$")
_BASE64ISH = re.compile(r"^[A-Za-z0-9+/_\-]{40,}={0,2}$")
_UUIDISH = re.compile(
    r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"
)
_PATHISH = re.compile(r"([A-Za-z]:[\\/]|[\\/]\S+|\.{0,2}[\\/]\S+)")

# Residual markdown / formatting the model sometimes leaves behind. The underscore
# alternatives only match runs that are NOT between two word characters, so code
# identifiers like E2_PERSISTENCE or get_user_name are preserved while markdown
# emphasis (_word_, __bold__) and standalone rules are stripped. The ">" alt is
# scoped to line start so it only strips blockquotes, not ">" in generics or "=>".
_MARKDOWN_NOISE = re.compile(r"(`{1,3}|\*{1,3}|(?<![A-Za-z0-9])_{1,3}|_{1,3}(?![A-Za-z0-9])|#{1,6}\s?|(?m:^>\s?)|\|\s?|={3,}|-{3,})")
# Emoji & wide range of pictographic / symbol characters.
_EMOJI = re.compile(
    "["
    "\U0001F000-\U0001FAFF"
    "\U00002600-\U000027BF"
    "\U0001F1E6-\U0001F1FF"
    "\U00002B00-\U00002BFF"
    "\U0001F300-\U0001F5FF"
    "\U0001F600-\U0001F64F"
    "\U0001F680-\U0001F6FF"
    "\U0001F700-\U0001F77F"
    "]+",
    flags=re.UNICODE,
)

# Spaced dashes used as clause separators (em-dashes are already normalized to "-"
# by the host). A bare dash reads as nothing; a comma reads as a beat.
_SPACED_DASH = re.compile(r"\s[—–-]\s")
# Arrows -> spoken "to" / "then".
_ARROW = re.compile(r"(?:->|→|⟶)")
# Fenced code blocks. Replaced with a short marker so the model summarizes rather
# than transcribing the code (see SYSTEM_PROMPT rule 3).
_CODE_FENCE = re.compile(r"```[^\n]*\n.*?```", re.DOTALL)
# Bare lowercase short commit SHAs (7-12 hex chars, must contain at least one digit
# so we don't label dictionary words like "abcdefgh").
_SHORT_SHA = re.compile(r"\b(?=[0-9a-f]*\d)[0-9a-f]{7,12}\b")
# Tilde-leading config paths, e.g. ~/.e2/secrets.local.json.
_TILDE_PATH = re.compile(r"~[/\\][^\s]+")
# Invented "NN:NN:path" line refs the model sometimes emits.
_LINE_REF_PATH = re.compile(r"\b(\d{1,5}):(\d{1,5}):[^\s]+")
# Long path-like tokens in the model's output -> shorten to the basename.
_LONG_PATH_OUT = re.compile(r"\S*[/\\]\S{8,}")
# Strip [bracketed] attribute markers, e.g. [InitializeOnLoad] -> InitializeOnLoad.
_BRACKETED = re.compile(r"\[([^\[\]]+)\]")
# Residual code-like lines the model leaked (statements, braces, signatures). The
# call-statement alt requires an "=" so a prose sentence ending in a semicolon with
# a parenthetical doesn't get dropped.
_CODE_LINE = re.compile(
    r"^\s*[{}][;\s]*$"
    r"|^\s*(static|void|var|public|private|protected|readonly|return|using|if|for|while|switch|case|class|struct|enum|function|def|import|namespace|new|async|await|try|catch|finally)\b"
    r"|.*=.*\w+\([^)]*\).*;\s*$"
    r"|.*\{.*\}.*"
)


def _tilde_path_label(m: re.Match) -> str:
    base = re.split(r"[/\\]", m.group(0))[-1]
    return f"the file {base}" if base else "a config file"


def _path_basename(m: re.Match) -> str:
    tok = m.group(0)
    base = re.split(r"[/\\]", tok)[-1]
    return base if base and len(base) < len(tok) else tok


def _strip_code_lines(text: str) -> str:
    """Drop lines that still look like source code after the model's pass."""
    keep: list[str] = []
    for line in text.splitlines():
        s = line.strip()
        if s and _CODE_LINE.match(s):
            continue
        keep.append(line)
    return "\n".join(keep)


def _looks_like_code(tok: str) -> bool:
    """True if a long token is a meaningful code identifier (Class.method, a call,
    PascalCase type, snake_case name) that the LLM should split + shorten itself —
    NOT an opaque blob to be replaced with a label."""
    # A call: Name(args)
    if "(" in tok and ")" in tok:
        return True
    # Dot-separated readable segments: Class.Method, obj.prop.chain
    if "." in tok:
        segs = [s for s in tok.split(".") if s]
        if any(re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", s) and len(s) >= 2 for s in segs):
            return True
    # Repeated PascalCase words: FooBarBaz (two or more capitalized words)
    if re.search(r"[A-Z][a-z]+([A-Z][a-z]+)+", tok):
        return True
    # snake_case with at least two lowercase words: get_user_name
    if re.search(r"[a-z]+_[a-z]+", tok):
        return True
    return False


def _classify_long_token(tok: str) -> str | None:
    """Return a spoken label to substitute for an opaque long token, or None to leave
    the token in place (e.g. for code identifiers the LLM should split + shorten)."""
    if _UUIDISH.match(tok):
        return "an identifier"
    if _URLISH.match(tok):
        return "a link"
    if tok.startswith(("sk-", "pk-", "ghp_", "gho_", "ghs_", "xox", "AKIA", "Bearer ")):
        return "an API key"
    if _HEX_BLOB.match(tok):
        return "a hash"
    if _PATHISH.match(tok) and any(c in tok for c in "\\/"):
        last = re.split(r"[\\/]", tok)[-1]
        if last and len(last) < len(tok):
            return f"the file {last}"
        return "a file path"
    # Leave meaningful code identifiers for the LLM to split + shorten.
    if _looks_like_code(tok):
        return None
    if _BASE64ISH.match(tok) and any(c.isalpha() for c in tok):
        return "a token"
    return "a long identifier"


def deterministic_prewrite(text: str) -> str:
    """Tidy the most readable-as-speech problems BEFORE the model sees the text:
    replace fenced code with a marker, turn spaced dashes/arrows into beats, label
    bare commit SHAs and tilde config paths, and replace opaque long tokens with
    short spoken labels. Code identifiers are left in place so the model can split
    them into words and shorten them to their meaningful core."""
    text = _CODE_FENCE.sub(" [a code block] ", text)
    text = _SPACED_DASH.sub(", ", text)
    text = _ARROW.sub(" to ", text)
    text = _SHORT_SHA.sub("a commit id", text)
    text = _TILDE_PATH.sub(_tilde_path_label, text)

    def sub(m: re.Match) -> str:
        label = _classify_long_token(m.group(0))
        return m.group(0) if label is None else label

    return _LONG_TOKEN.sub(sub, text)


def deterministic_postwrite(text: str) -> str:
    """Tidy the model's output: drop residual markdown/emoji/code, recover beats
    from dashes/arrows, shorten invented paths and line refs, and collapse
    whitespace. Preserves up to three line breaks so the TTS worker can tier
    pauses by newline count (sentence vs paragraph vs major section)."""
    text = _EMOJI.sub("", text)
    text = text.replace("[a code block]", "")
    text = _SPACED_DASH.sub(", ", text)
    text = _ARROW.sub(" to ", text)
    text = _BRACKETED.sub(r"\1", text)
    text = _LINE_REF_PATH.sub(r"lines \1 to \2", text)
    text = _LONG_PATH_OUT.sub(_path_basename, text)
    text = _MARKDOWN_NOISE.sub("", text)
    text = _strip_code_lines(text)
    # Collapse runs of whitespace inside lines and tidy line breaks.
    text = re.sub(r"[ \t]+", " ", text)
    text = re.sub(r"[ \t]*\n[ \t]*", "\n", text)
    text = re.sub(r"\n{4,}", "\n\n\n", text)
    return text.strip()


# ---------------------------------------------------------------------------
# llama-cpp-python wrapper
# ---------------------------------------------------------------------------

class _PrepModel:
    def __init__(self):
        from llama_cpp import Llama  # imported lazily so ping works without the model

        model_path = os.environ.get("ITTALKS_PREP_MODEL", "")
        if not model_path or not os.path.isfile(model_path):
            raise RuntimeError(f"model file not found: {model_path!r}")

        nctx = int(os.environ.get("ITTALKS_PREP_NCTX", str(DEFAULT_NCTX)))
        threads = int(os.environ.get("ITTALKS_PREP_THREADS", str(os.cpu_count() or 4)))
        kwargs = dict(
            model_path=model_path,
            n_ctx=nctx,
            n_threads=threads,
            verbose=False,
            use_mlock=False,
            use_mmap=True,
        )
        chat_fmt = os.environ.get("ITTALKS_PREP_CHAT_FMT")
        if chat_fmt:
            kwargs["chat_format"] = chat_fmt
        self.llm = Llama(**kwargs)

    def rewrite(self, text: str) -> str:
        # Generous cap; the post-pass trims. Faithful rewrites need little novelty.
        max_tokens = max(256, min(2048, len(text) * 3))
        resp = self.llm.create_chat_completion(
            messages=[
                {"role": "system", "content": SYSTEM_PROMPT},
                {"role": "user", "content": text},
            ],
            max_tokens=max_tokens,
            temperature=0.3,
            top_p=0.9,
            stream=False,
        )
        choice = (resp or {}).get("choices", [{}])[0]
        content = choice.get("message", {}).get("content") or ""
        return content.strip()


_prep: _PrepModel | None = None
_cur_id = None


def _emit(obj: dict) -> None:
    if _cur_id is not None and "id" not in obj:
        obj["id"] = _cur_id
    sys.stdout.write(json.dumps(obj) + "\n")
    sys.stdout.flush()


def _ensure_model():
    global _prep
    if _prep is None:
        _prep = _PrepModel()
    return _prep


def _count_syllables(word: str) -> int:
    """Rough English syllable estimate — vowel-group counting with a silent-e / -le
    correction. Used only as a per-word duration weight for highlight timing, so it
    doesn't need to be phonetically perfect, just better than character length."""
    word = re.sub(r"[^A-Za-z]", "", word).lower()
    if not word:
        return 0
    vowels = "aeiouy"
    count = 0
    prev_v = False
    for ch in word:
        is_v = ch in vowels
        if is_v and not prev_v:
            count += 1
        prev_v = is_v
    if word.endswith("e") and count > 1:
        count -= 1
    if word.endswith("le") and len(word) > 2 and word[-3] not in vowels:
        count += 1
    return max(count, 1)


def _word_weights(text: str) -> list[int]:
    """Per-word syllable counts aligned to ``text.split()`` (whitespace split). The host
    splits the same way, so indices line up with the word Runs it builds for highlighting."""
    return [_count_syllables(w) for w in text.split()]


def _rewrite(text: str) -> tuple[str, list[int]]:
    stripped = text.strip()
    if not stripped:
        return text, []
    if len(stripped) <= 3:
        return stripped, _word_weights(stripped)

    pre = deterministic_prewrite(text)
    try:
        out = _ensure_model().rewrite(pre)
    except Exception:
        # If the model fails, fall back to the deterministic pass so speech still
        # benefits from the long-token replacement.
        out = pre

    cleaned = deterministic_postwrite(out)
    if not cleaned:
        cleaned = deterministic_postwrite(pre)
    return cleaned, _word_weights(cleaned)


def main() -> None:
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
                model_path = os.environ.get("ITTALKS_PREP_MODEL", "")
                if not os.path.isfile(model_path):
                    _emit({"ok": False, "error": f"model missing: {model_path}"})
                    continue
                _emit({"ok": True})
            elif cmd == "prep":
                text = req.get("text") or ""
                if not text.strip():
                    _emit({"ok": False, "error": "empty text"})
                    continue
                cleaned, weights = _rewrite(text)
                _emit({"ok": True, "text": cleaned, "weights": weights})
            else:
                _emit({"ok": False, "error": f"unknown cmd: {cmd}"})
        except Exception:
            _emit({"ok": False, "error": traceback.format_exc()})


if __name__ == "__main__":
    main()
