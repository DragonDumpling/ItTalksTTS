# ItTalksTTS

Windows desktop app that bridges **[Kokoro](https://github.com/thewh1teagle/kokoro-onnx)** text-to-speech (Python ONNX worker), a **playback queue**, a **local HTTP API**, and an **MCP** host for tools like Cursor.

## Features

- **Voice** — Start/stop the Kokoro worker, pick model/voice, test speech, service log.
- **The Q** — Queue lines with states (pending / playing / played / error), reorder, play/pause, play selected (including replay), optional autoplay chain, filters-ready pipeline.
- **The Paste** — Paste text, apply filter rules, enqueue.
- **Filters** — Manage normalization rules persisted with settings.
- **Local API** — `POST /v1/queue` with bearer auth; port and token in `%LocalAppData%\ItTalksTTS\runtime.json` / `settings.json` while the app runs. **Enqueue only** — does not start audio by itself.
- **MCP** (optional) — `ItTalksTTS.McpServer` (stdio) for on-demand `EnqueueTts` / `GetApiStatus`.

## Requirements

- **Windows** (WPF / `net9.0-windows`)
- **[.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)**

## Build & run

```powershell
cd path\to\ItTalksTTS
dotnet build .\ItTalksTTS.sln -c Release
dotnet run --project .\src\ItTalksTTS.App\ItTalksTTS.App.csproj
```

Release output: `src\ItTalksTTS.App\bin\Release\net9.0-windows\ItTalksTTS.exe`

## Cursor → The Q (default: hooks)

Each finished **Agent** assistant message can be **enqueued** automatically (source `cursor-hook`, state **Pending**). **Hooks do not play audio**; press **Play** in The Q (or enable **Autoplay**). **MCP is optional** and not required for hooks.

**Full first-time setup (trusted workspace, `cmd.exe` launcher, UTF-8 stdin, Agent vs Ask, troubleshooting, mojibake in Hooks output):**

**[docs/cursor-integration.md](docs/cursor-integration.md)**

Quick checklist:

1. Build/run **ItTalksTTS** (API must be up; see `%LocalAppData%\ItTalksTTS\runtime.json`).
2. Open this repo in Cursor as a **trusted** workspace (folder containing `.cursor\hooks.json`).
3. Use **Composer → Agent** (Ask-only chat may not run `afterAgentResponse`).
4. Confirm **Output → Hooks**: `ittalks-hook: enqueued to The Q (...)` and a new row in **The Q**.

Shipped hook files: [`.cursor/hooks.json`](.cursor/hooks.json) and **`ItTalksHookEnqueue.exe`** (built into `.cursor/hooks/` on `dotnet build`).

## Solution layout

| Project | Role |
|--------|------|
| `ItTalksTTS.App` | WPF UI, playback (NAudio), API host, worker supervision |
| `ItTalksTTS.Core` | Queue models, persistence, filter engine |
| `ItTalksTTS.Tts` | Kokoro process / protocol |
| `ItTalksTTS.Api` | Shared API contracts / helpers |
| `ItTalksTTS.McpServer` | MCP stdio server (enqueue + status) |
| `ItTalksTTS.Tests` | Unit tests |

Python worker sources live under `tools\kokoro_worker\` (see that folder’s README).

## GitHub (private remote)

To create a **private** GitHub repository and push (after [GitHub CLI](https://cli.github.com/) is on your `PATH` or use the full path to `gh.exe`, then `gh auth login`):

```powershell
cd path\to\ItTalksTTS
gh auth login
gh repo create ItTalksTTS --private --source=. --remote=origin --push --description "WPF Kokoro TTS bridge: queue, local API, MCP for Cursor"
```

If `origin` already exists, adjust remote or repo name. Non-interactive setups can use `GH_TOKEN` with `repo` scope.

## License

No license file is bundled in this repository; default copyright applies unless you add one.
