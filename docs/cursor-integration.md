# Cursor integration (hooks default, MCP optional)

ItTalksTTS exposes a **local HTTP API** while the desktop app is running. **`POST /v1/queue` only adds a row to The Q** (pending). It does **not** start playback unless **Autoplay** is enabled in **The Q** and nothing is already playing.

Playback otherwise requires **Play** / **Play selected** in the app (and **Kokoro** running on the Voice tab).

**MCP is not required for hooks.** Hooks call the HTTP API via **`ItTalksHookEnqueue.exe`**. MCP is optional when you want the model to call **`EnqueueTts`** on demand.

---

## First-time setup (new machine or new teammate)

Do these steps in order. The repo ships [`.cursor/hooks.json`](../.cursor/hooks.json). After **`dotnet build`**, [`.cursor/hooks/ItTalksHookEnqueue.exe`](../.cursor/hooks/ItTalksHookEnqueue.exe) is published automatically (gitignored; required).

### 1. Prerequisites

- **Windows**
- **ItTalksTTS** installed ([README](../README.md) — double-click **`ItTalksTTS-Setup.exe`**, finish first-run setup) **or** built from source
- **Cursor** with **Hooks** ([Cursor hooks docs](https://cursor.com/docs/hooks))
- This **git repo** open in Cursor (for `.cursor/hooks.json` and a built hook exe)

### 2. Run ItTalksTTS

Leave the desktop app running. First launch runs Kokoro setup automatically if models are missing.

**Cursor hooks** still require building this repo once (publishes `.cursor\hooks\ItTalksHookEnqueue.exe`):

```powershell
cd path\to\ItTalksTTS
dotnet build .\ItTalksTTS.sln -c Release
```

| File | Purpose |
|------|---------|
| `%LocalAppData%\ItTalksTTS\runtime.json` | API **port** while the app runs |
| `%LocalAppData%\ItTalksTTS\settings.json` | **`apiToken`** for `Authorization: Bearer ...` |

### 3. Open the repo in Cursor (trusted workspace)

1. **File → Open Folder** → ItTalksTTS repo root (contains `.cursor\hooks.json`).
2. Mark the workspace **trusted** (project hooks do not run in untrusted workspaces).
3. Reload hooks (save `hooks.json` or restart Cursor).

To use hooks in **another** project, copy `.cursor\hooks.json` into that repo and either copy a built `ItTalksHookEnqueue.exe` into `.cursor\hooks\` or use a path that points at this repo’s exe (hooks always `cd` to `%CURSOR_PROJECT_DIR%` first).

### 4. Confirm hooks are loaded

1. Cursor **Settings** → **Hooks**, or **Output** → **Hooks** channel.
2. You should see **`afterAgentResponse`** calling:

```text
cmd.exe /S /C "cd /d %CURSOR_PROJECT_DIR% && .cursor\hooks\ItTalksHookEnqueue.exe"
```

**Do not** put `$env:CURSOR_PROJECT_DIR` in the `hooks.json` **command** string. Some Cursor builds expand it to a bare path and break the launcher.

**Do not** use the legacy script `.cursor/hooks/ittalks-enqueue-response.ps1` (encoding issues).

### 5. Use Agent-style chat (not Ask-only)

`afterAgentResponse` applies to **Agent** / agent-style Composer flows. Plain **Ask** may not fire the hook.

1. Open **Composer** in **Agent** mode.
2. Send a message and wait for the full reply.
3. **ItTalksTTS → The Q** — new row, source **`cursor-hook`**, state **Pending**.
4. **Output → Hooks** — `ittalks-hook: enqueued to The Q (N chars)`.

Press **Play** (or enable **Autoplay**) to hear it.

### 6. Optional: MCP

Skip for automatic “every reply → The Q”. See [MCP (optional)](#mcp-optional) below.

---

## How it works

| Step | What happens |
|------|----------------|
| 1 | Cursor finishes an assistant message (Agent mode). |
| 2 | **`afterAgentResponse`** runs the command in `.cursor/hooks.json`. |
| 3 | `cmd.exe` `cd`s to `%CURSOR_PROJECT_DIR%`, runs **`.cursor\hooks\ItTalksHookEnqueue.exe`**. |
| 4 | Hook reads stdin (UTF-16 LE on Windows is detected; otherwise UTF-8), parses JSON field **`text`**. |
| 5 | **POST** UTF-8 JSON to `http://127.0.0.1:<port>/v1/queue` with `source: "cursor-hook"`. |
| 6 | API runs **`PrepareForQueue`** (mojibake repair + typography normalization), **Filters**, enqueues **Pending**. |
| 7 | If **Autoplay** is on and playback is idle, the next item starts (same as paste/API from UI). |

### Hook input (Cursor)

```json
{
  "text": "<assistant final text>",
  "hook_event_name": "afterAgentResponse",
  "workspace_roots": ["F:\\Projects\\ItTalksTTS"]
}
```

### Manual API test (PowerShell)

```powershell
$rt = Get-Content "$env:LOCALAPPDATA\ItTalksTTS\runtime.json" | ConvertFrom-Json
$settings = Get-Content "$env:LOCALAPPDATA\ItTalksTTS\settings.json" | ConvertFrom-Json
$body = @{ text = "Hello — café test."; source = "manual-test" } | ConvertTo-Json -Compress
$bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
Invoke-RestMethod -Uri "http://127.0.0.1:$($rt.port)/v1/queue" -Method Post `
  -Body $bytes -ContentType "application/json; charset=utf-8" `
  -Headers @{ Authorization = "Bearer $($settings.apiToken)" }
```

---

## Troubleshooting

| Symptom | What to check |
|--------|----------------|
| Nothing in The Q | App running? **Trusted** workspace? **Agent** mode? **Output → Hooks** errors. |
| `FileNotFoundException: System.Runtime` | Rebuild app so **self-contained** `ItTalksHookEnqueue.exe` is republished under `.cursor\hooks\`. |
| PowerShell parse errors (`-not f:\...`) | Use **`%CURSOR_PROJECT_DIR%`** via `cmd.exe` in `hooks.json` (see this repo). |
| `runtime.json missing` | Start **ItTalksTTS** before chatting. |
| `` or `?` where em dash / smart quotes should be | Rebuild app + hook (UTF-16 stdin decode + `PrepareForQueue`). Do not use legacy PowerShell hook. |
| `itâ€™s` / `cafÃ©` in **The Q** | Same as above; old mojibake path. |
| Garbled text only in **Hooks output** panel | Often log encoding (`â€"` for `—`); ignore if enqueue succeeded. |
| Row in Q, no sound | **Start Kokoro**; press **Play** or enable **Autoplay**. |
| Hook works from repo, not another folder | That project needs its own `.cursor\hooks.json` + `ItTalksHookEnqueue.exe` (or open ItTalksTTS repo). |

### Text encoding (The Q)

The hook and API normalize text before enqueue:

- **Stdin:** Windows Cursor often sends **UTF-16 LE**; the hook detects `{` + `0x00` and decodes correctly.
- **Queue text:** `PrepareForQueue` repairs UTF-8 mojibake and maps curly quotes / em dashes to ASCII-friendly forms for display and TTS.

---

## MCP (optional)

Use when the **model** should call **`EnqueueTts`** or **`GetApiStatus`**.

Build:

```powershell
dotnet build .\src\ItTalksTTS.McpServer\ItTalksTTS.McpServer.csproj -c Release
```

**mcp.json** example (dev build):

```json
{
  "mcpServers": {
    "ittalks-tts": {
      "command": "F:\\Projects\\ItTalksTTS\\src\\ItTalksTTS.McpServer\\bin\\Release\\net9.0\\ItTalksTTS.McpServer.exe",
      "args": []
    }
  }
}
```

After **installer** install:

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

**Tools:** `EnqueueTts` (enqueue only), `GetApiStatus` (port / runtime file).

**Start ItTalksTTS** before using MCP.

---

## Health check (no auth)

`GET http://127.0.0.1:<port>/v1/health` → `{ "status": "ok" }`
