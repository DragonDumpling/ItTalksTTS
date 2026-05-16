# Cursor integration (hooks and MCP)

ItTalksTTS exposes a **local HTTP API** when the desktop app is running. The API listens on `http://127.0.0.1:<port>/` where `<port>` is chosen at startup and written to:

`%LocalAppData%\ItTalksTTS\runtime.json`

Your **Bearer token** is stored in:

`%LocalAppData%\ItTalksTTS\settings.json` (`apiToken` field)

## Enqueue from any script (PowerShell)

```powershell
$rt = Get-Content "$env:LOCALAPPDATA\ItTalksTTS\runtime.json" | ConvertFrom-Json
$settings = Get-Content "$env:LOCALAPPDATA\ItTalksTTS\settings.json" | ConvertFrom-Json
$body = @{ text = "Hello from a hook."; source = "cursor-hook" } | ConvertTo-Json
Invoke-RestMethod -Uri "http://127.0.0.1:$($rt.port)/v1/queue" -Method Post -Body $body -ContentType "application/json" `
  -Headers @{ Authorization = "Bearer $($settings.apiToken)" }
```

## Cursor MCP (`mcp.json`)

Build the MCP host once, then point Cursor at the published `ItTalksTTS.McpServer` executable (or `dotnet run` for development).

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

Tools exposed:

- `EnqueueTts` — add text to **The Q** (`source` defaults to `mcp`).
- `GetApiStatus` — confirms `runtime.json` exists and reports the port.

The MCP server only talks to the app over loopback HTTP; **start ItTalksTTS before** using MCP tools.

## Health check (no auth)

`GET http://127.0.0.1:<port>/v1/health` returns `{ "status": "ok" }` for simple uptime checks.

## Hooks

Use Cursor hooks to POST assistant output to `/v1/queue` with the same JSON body and `Authorization` header as in the PowerShell example. Keep payloads bounded so hooks stay fast.
