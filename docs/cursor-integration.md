# Cursor integration (hooks — no repo clone)

ItTalksTTS can enqueue each finished **Agent** reply to **The Q** automatically. **You do not need to clone this repository or open it in Cursor.**

## End-user setup (after `ItTalksTTS-Setup.exe`)

1. Install from [GitHub Releases](https://github.com/DragonDumpling/ItTalksTTS/releases/latest).
2. Run **ItTalksTTS** at least once (tray/app running — local API must be up).
3. The installer (and the app on first launch) installs **user-level Cursor hooks** under your home folder:
   - `%USERPROFILE%\.cursor\hooks.json`
   - `%USERPROFILE%\.cursor\hooks\ItTalksHookEnqueue.exe`
4. **Restart Cursor** if it was already open (so it reloads hooks).
5. Open **any** project in Cursor (your own code — not this repo).
6. Use **Agent** mode, finish a reply → row in **The Q** with source `cursor-hook`.

That is the entire Cursor setup. No `git clone`, no trusted workspace requirement for this repo.

### If hooks are missing

In ItTalksTTS → **Voice** tab → **Install / repair Cursor hooks**, then restart Cursor.

Or re-run silently:

```text
"C:\Program Files\ItTalksTTS\ItTalksTTS.exe" /installCursorHooks
```

(Adjust path if you installed elsewhere.)

---

## How it works

| Step | What happens |
|------|----------------|
| 1 | Cursor finishes an Agent message. |
| 2 | **User hook** `afterAgentResponse` runs `~/.cursor/hooks/ItTalksHookEnqueue.exe`. |
| 3 | Hook POSTs to `http://127.0.0.1:<port>/v1/queue` (reads `%LocalAppData%\ItTalksTTS\`). |
| 4 | App enqueues **Pending**; **Play** or **Autoplay** in The Q plays audio. |

Hooks apply to **all projects** on this machine (user hooks). Project-only hooks in a git repo are optional for developers contributing to ItTalksTTS itself.

---

## Playback

- **Autoplay off** (default for many users): hooks enqueue only; press **Play** in The Q.
- **Autoplay on**: next pending item may start when idle.
- **Kokoro** must be started on the Voice tab to hear speech.

---

## Troubleshooting

| Symptom | Fix |
|--------|-----|
| Nothing in The Q | ItTalksTTS running? **Agent** mode (not Ask-only)? **Output → Hooks** for errors. |
| Hooks not listed in Cursor | Restart Cursor; confirm `%USERPROFILE%\.cursor\hooks.json` exists; click **Install / repair** in the app. |
| `runtime.json missing` | Start ItTalksTTS. |
| Garbled text in The Q | Reinstall app; use current hook exe from install folder. |

---

## MCP (optional)

Not required for automatic enqueue. Use `ItTalksTTS.McpServer.exe` from the install folder only if you want the **model** to call `EnqueueTts` on demand.

Example `mcp.json`:

```json
{
  "mcpServers": {
    "ittalks-tts": {
      "command": "C:\\Program Files\\ItTalksTTS\\ItTalksTTS.McpServer.exe",
      "args": []
    }
  }
}
```

---

## Developers (this repository)

Use **user hooks** (installed by the app on first launch) when working in this repo. Do **not** add a project-level `afterAgentResponse` hook here — Cursor runs **both** user and project hooks, which would enqueue every reply twice. The repo’s [`.cursor/hooks.json`](../.cursor/hooks.json) is intentionally empty.

---

## Health check

`GET http://127.0.0.1:<port>/v1/health` → `{ "status": "ok" }`
