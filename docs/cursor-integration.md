# Cursor integration (hooks default, MCP optional)

ItTalksTTS exposes a **local HTTP API** while the desktop app is running. **`POST /v1/queue` only adds a row to The Q** (pending). It does **not** start playback by itself.

Playback happens only when you use the app (**Play** / **Play selected**) or when **Autoplay** is enabled in **The Q** (then the next pending line may start automatically after the current clip). To keep hook-driven lines **waiting in the queue**, turn **Autoplay** off.

**MCP is not required for hooks.** Hooks call the HTTP API directly. MCP is optional when you want the model to call **`EnqueueTts`** on demand.

---

## First-time setup (new machine or new teammate)

Do these steps in order. The repo already contains [`.cursor/hooks.json`](../.cursor/hooks.json). After **`dotnet build`**, [`.cursor/hooks/ItTalksHookEnqueue.exe`](../.cursor/hooks/ItTalksHookEnqueue.exe) is copied automatically (gitignored; required for hooks).

### 1. Prerequisites

- **Windows**
- **[.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)**
- **Cursor** with **Hooks** support (see [Cursor hooks docs](https://cursor.com/docs/hooks))
- No separate PowerShell hook runtime; hooks call **`ItTalksHookEnqueue.exe`** (built with the solution).

### 2. Build and run ItTalksTTS

```powershell
cd path\to\ItTalksTTS
dotnet build .\ItTalksTTS.sln -c Release
```

This also publishes **`.cursor\hooks\ItTalksHookEnqueue.exe`** (~35 MB, self-contained; required for Cursor hooks). Building **`ItTalksTTS.App`** runs this step automatically. Then run:

```powershell
dotnet run --project .\src\ItTalksTTS.App\ItTalksTTS.App.csproj
```

Or `src\ItTalksTTS.App\bin\Release\net9.0-windows\ItTalksTTS.exe`.

Or run `src\ItTalksTTS.App\bin\Release\net9.0-windows\ItTalksTTS.exe`.

Leave the app running while you use Cursor. On the **Voice** tab, confirm Kokoro works if you plan to play the queue (enqueue works without Kokoro; playback needs the worker).

The app writes:

| File | Purpose |
|------|---------|
| `%LocalAppData%\ItTalksTTS\runtime.json` | API **port** (and token copy) while the app runs |
| `%LocalAppData%\ItTalksTTS\settings.json` | **`apiToken`** for `Authorization: Bearer ...` |

### 3. Open the repo in Cursor (trusted workspace)

1. **File → Open Folder** and choose the **ItTalksTTS** repo root (the folder that contains `.cursor\hooks.json`).
2. When Cursor asks, mark the workspace as **trusted**. Project hooks **do not run** in untrusted workspaces.
3. Reload hooks: save `.cursor/hooks.json` or restart Cursor.

### 4. Confirm hooks are loaded

1. Cursor **Settings** → search **Hooks**, or open **Output** and select the **Hooks** channel.
2. You should see project hooks for **`afterAgentResponse`** pointing at the `cmd.exe` + PowerShell command (see [`.cursor/hooks.json`](../.cursor/hooks.json)).

**Important:** Do **not** put `$env:CURSOR_PROJECT_DIR` in the `hooks.json` **command** string. Some Cursor builds expand it to a bare path (`f:\Projects\...`), which breaks PowerShell (`if (-not f:\...)`). This repo uses:

```text
cmd.exe /S /C "cd /d %CURSOR_PROJECT_DIR% && .cursor\hooks\ItTalksHookEnqueue.exe"
```

### 5. Use Agent-style chat (not Ask-only)

Hooks for assistant text use **`afterAgentResponse`**, which applies to **Agent Chat** and **Cmd+K** agent flows per Cursor docs. Plain **Ask** mode may **not** fire this hook.

1. Open **Composer** in **Agent** mode (or an agent chat panel).
2. Send a short message and wait for the full assistant reply.
3. In **ItTalksTTS → The Q**, look for a new row with source **`cursor-hook`** and state **Pending**.
4. In **Output → Hooks**, look for: `ittalks-hook: enqueued to The Q (N chars).`

Press **Play** in The Q when you want to hear it (unless **Autoplay** is on).

### 6. Optional: MCP

Only if you want the **model** to enqueue via tools (`EnqueueTts`, `GetApiStatus`). See [MCP (optional extras)](#mcp-optional-extras) below. Skip this for automatic “every reply → The Q” behavior; hooks already do that.

---

## How it works

| Step | What happens |
|------|----------------|
| 1 | Cursor finishes an assistant message in Agent mode. |
| 2 | **`afterAgentResponse`** runs the command in `.cursor/hooks.json`. |
| 3 | `cmd.exe` `cd`s to `%CURSOR_PROJECT_DIR%`, then runs **`.cursor\hooks\ItTalksHookEnqueue.exe`**. |
| 4 | The tool reads **UTF-8 JSON** from stdin (`System.Text.Json`, field **`text`**). |
| 5 | **POST** UTF-8 JSON to `http://127.0.0.1:<port>/v1/queue` with `source: "cursor-hook"`. |
| 6 | App applies **Filters**, enqueues **Pending** row. If **Autoplay** is on and nothing is playing, playback starts automatically (same as paste-to-queue in the UI). |

### Hook input (Cursor)

`afterAgentResponse` stdin is JSON ([Cursor hooks docs](https://cursor.com/docs/hooks)). The script uses the **`text`** field (assistant message body). Cursor may also send common fields such as `conversation_id`, `hook_event_name`, `workspace_roots`, etc.

```json
{
  "text": "<assistant final text>",
  "hook_event_name": "afterAgentResponse",
  "workspace_roots": ["F:\\Projects\\ItTalksTTS"]
}
```

### Manual API test (PowerShell)

Use this to verify the app and token without hooks:

```powershell
$rt = Get-Content "$env:LOCALAPPDATA\ItTalksTTS\runtime.json" | ConvertFrom-Json
$settings = Get-Content "$env:LOCALAPPDATA\ItTalksTTS\settings.json" | ConvertFrom-Json
$body = @{ text = "Hello from a manual test."; source = "cursor-hook" } | ConvertTo-Json
Invoke-RestMethod -Uri "http://127.0.0.1:$($rt.port)/v1/queue" -Method Post -Body $body -ContentType "application/json" `
  -Headers @{ Authorization = "Bearer $($settings.apiToken)" }
```

---

## Troubleshooting

| Symptom | What to check |
|--------|----------------|
| Nothing in The Q | ItTalksTTS running? **Trusted** workspace? **Agent** mode? **Output → Hooks** for errors. If you see `FileNotFoundException: System.Runtime`, rebuild the app so the **self-contained** `ItTalksHookEnqueue.exe` is republished (a framework-only copy cannot run alone). |
| PowerShell parse errors (`-not f:\...`) | `hooks.json` must use **`%CURSOR_PROJECT_DIR%`** via `cmd.exe`, not `$env:CURSOR_PROJECT_DIR` in the command string. Use this repo’s `.cursor/hooks.json`. |
| `invalid JSON` / `Invalid JSON primitive: .` | Fixed in current script: stdin read as **UTF-8** via `StreamReader`. Reload hook / restart Cursor after pulling latest script. |
| `runtime.json missing` | Start **ItTalksTTS** before chatting. |
| Garbled symbols in Hooks output (`â€`, `â€"`, etc.) | **Mojibake** in the log panel (see below). |
| `itâ€™s` / `cafÃ©` in **The Q** | Use **`ItTalksHookEnqueue.exe`** (build solution once). Do not use the legacy PowerShell hook. Rebuild **ItTalksTTS** app for API UTF-8 body read. |
| Row in Q but no sound with **Autoplay** on | Start **Kokoro** on Voice tab. Rebuild app so API enqueue triggers autoplay (not just UI paste). If already playing, new items wait until the current clip finishes. |
| Hook works, want model-driven enqueue | Add **MCP** (optional); not required for hooks. |

### Garbled symbols in Hooks output (`â€`, `â€"`, …)

This is **mojibake** (mis-decoded text), not a special Cursor symbol.

- Cursor and the hook script use **UTF-8** for JSON and messages.
- The **Hooks** output panel (or older log lines) sometimes displays bytes as **Windows-1252** or the console code page.
- A UTF-8 **em dash** (`—`) or similar punctuation can show up as **`â€"`** or **`â€`** when those bytes are read with the wrong encoding.

You can ignore it if you see **`ittalks-hook: enqueued to The Q`**. The current hook script only writes **ASCII** to stderr to reduce this. If you still see mojibake, it may be from an older run or from Cursor’s own messages, not from a failed enqueue.

---

## MCP (optional extras)

Use **`ItTalksTTS.McpServer`** when you want the **model** to enqueue text via a tool (`EnqueueTts`) or to check **`GetApiStatus`**, in addition to (or instead of) automatic hook enqueueing.

Build the MCP host:

```powershell
dotnet build .\src\ItTalksTTS.McpServer\ItTalksTTS.McpServer.csproj -c Release
```

Add to Cursor **MCP** config (user or project `mcp.json`):

```json
{
  "mcpServers": {
    "ittalks-tts": {
      "command": "F:\\\\path\\\\to\\\\ItTalksTTS.McpServer.exe",
      "args": []
    }
  }
}
```

Release binary path example: `src\ItTalksTTS.McpServer\bin\Release\net9.0\ItTalksTTS.McpServer.exe`

For development you can use `dotnet` as the command with `run --project ...` (slower startup).

**Tools**

- **`EnqueueTts`** — add text to **The Q** (`source` defaults to `mcp`). Enqueue only.
- **`GetApiStatus`** — confirms `runtime.json` exists and reports the port.

**Start ItTalksTTS before** using MCP tools.

---

## Health check (no auth)

`GET http://127.0.0.1:<port>/v1/health` returns `{ "status": "ok" }` for simple uptime checks.
