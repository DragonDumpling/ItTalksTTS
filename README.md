# ItTalksTTS

Windows desktop app that bridges **[Kokoro](https://github.com/thewh1teagle/kokoro-onnx)** text-to-speech (Python ONNX worker), a **playback queue**, a **local HTTP API**, and an **MCP** host for tools like Cursor.

## Features

- **Voice** — Start/stop the Kokoro worker, pick model/voice, test speech, service log.
- **The Q** — Queue lines with states (pending / playing / played / error), reorder, play/pause, play selected (including replay), autoplay chain, filters-ready pipeline.
- **The Paste** — Paste text, apply filter rules, enqueue.
- **Filters** — Manage normalization rules persisted with settings.
- **Local API** — `POST /v1/queue` with bearer auth; port and token in `%LocalAppData%\ItTalksTTS\runtime.json` / `settings.json` while the app runs.
- **MCP** — `ItTalksTTS.McpServer` (stdio) calls the same API for `EnqueueTts` and status.

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

## Cursor, hooks, and MCP

See **[docs/cursor-integration.md](docs/cursor-integration.md)** for `mcp.json`, hooks, PowerShell examples, and `/v1/health`.

## License

No license file is bundled in this repository; default copyright applies unless you add one.
