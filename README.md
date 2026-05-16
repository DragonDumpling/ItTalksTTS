<p align="center">
  <img src="src/ItTalksTTS.App/Assets/ittalks-logo.png" alt="ItTalksTTS logo" width="280"/>
</p>

# ItTalksTTS

Windows desktop app that bridges **[Kokoro](https://github.com/thewh1teagle/kokoro-onnx)** text-to-speech, a **playback queue (The Q)**, a **local HTTP API**, and optional **Cursor** hooks / MCP.

## Install

**You only need the setup program.**

1. Get **`ItTalksTTS-Setup.exe`** (from [Releases](https://github.com/YOUR_USER/ItTalksTTS/releases), or the **`release\`** folder after a maintainer build — open the zip and run the setup file at the top).
2. **Double-click** it and follow the wizard (Next → Install → Finish).
3. Launch **ItTalksTTS** from the desktop shortcut or Start menu (the installer can launch it for you).

**First launch:** the app opens a one-time setup window automatically. It installs Kokoro’s Python pieces and downloads voice models over the internet (often a few minutes). You do not need to install Python or .NET separately when using the setup EXE.

**Then:** use the app — paste or enqueue text, press **Play** in **The Q** (or turn on **Autoplay**). On the **Voice** tab, **Start Kokoro** if you want speech right away after setup.

| You want… | Do this |
|-----------|---------|
| Hear queued text | **The Q** → **Play** (or enable **Autoplay**) |
| Cursor Agent → queue | See [Cursor integration](#cursor-integration) (separate from the Windows installer) |

---

## Features

- **Voice** — Kokoro worker, voices, test line, service log.
- **The Q** — Queue, play/pause, replay, reorder, Autoplay.
- **The Paste** — Paste, filter, enqueue.
- **Filters** — Persistent normalization rules.
- **Local API** — `POST /v1/queue` (bearer token while the app runs).
- **Cursor hooks** — Agent replies → The Q (`cursor-hook`).
- **MCP** (optional) — `EnqueueTts`, `GetApiStatus`.

---

## Cursor integration

Hooks are **not** configured by the Windows installer. Developers open this repo in Cursor (trusted workspace) after building once so `.cursor\hooks\ItTalksHookEnqueue.exe` exists.

**Guide:** [docs/cursor-integration.md](docs/cursor-integration.md)

---

## For developers (build from source)

**Requirements:** Windows, [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
cd path\to\ItTalksTTS
dotnet build .\ItTalksTTS.sln -c Release
.\src\ItTalksTTS.App\bin\Release\net9.0-windows\ItTalksTTS.exe
```

First run still auto-downloads Kokoro models; use **Python 3.10+** on PATH unless you published with embedded Python (installer build does).

### Build the Windows installer (maintainers)

Needs .NET 9 SDK + [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
.\installer\build.ps1
```

Output: **`release\ItTalksTTS-Setup.exe`** plus **`release\README.txt`**. Zip the `release` folder for download so the setup file is obvious at the top level. The script publishes the app (self-contained + embedded Python + Kokoro worker scripts) and compiles the installer.

### App icon

`ittalks-logo-rgb.png` (master, may be RGB on black) → `ittalks-logo.png` (32-bit alpha for the app). After replacing the master:

```powershell
.\tools\rgb-logo-to-alpha.ps1 -SourcePath .\src\ItTalksTTS.App\Assets\ittalks-logo-rgb.png -DestPath .\src\ItTalksTTS.App\Assets\ittalks-logo.png
.\tools\png-to-ico.ps1 -PngPath .\src\ItTalksTTS.App\Assets\ittalks-logo.png -IcoPath .\src\ItTalksTTS.App\Assets\ittalks.ico
```

### Solution layout

| Project | Role |
|--------|------|
| `ItTalksTTS.App` | WPF UI, playback, API host |
| `ItTalksTTS.Core` | Queue, persistence, filters, encoding |
| `ItTalksTTS.Tts` | Kokoro worker / setup |
| `ItTalksTTS.Api` | Local HTTP API |
| `ItTalksTTS.HookEnqueue` | Cursor hook → API |
| `ItTalksTTS.McpServer` | MCP stdio server |
| `ItTalksTTS.Tests` | Unit tests |

### Data locations

| Path | Purpose |
|------|---------|
| `%LocalAppData%\ItTalksTTS\settings.json` | Voice, filters, API token, Autoplay |
| `%LocalAppData%\ItTalksTTS\runtime.json` | API port (while running) |
| `%LocalAppData%\ItTalksTTS\models\` | Kokoro ONNX + voices |
| `%LocalAppData%\ItTalksTTS\queue.json` | Saved queue |

## License

No license file is bundled; default copyright applies unless you add one.
