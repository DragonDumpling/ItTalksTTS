# ItTalksTTS: enqueue Cursor assistant text to The Q via local API (enqueue only -- no playback).
# Invoked from hooks.json afterAgentResponse (see docs/cursor-integration.md).
$ErrorActionPreference = 'Continue'
if ($env:CURSOR_PROJECT_DIR) {
    try {
        Set-Location -LiteralPath $env:CURSOR_PROJECT_DIR
    } catch {
        [Console]::Error.WriteLine('ittalks-hook: could not Set-Location to CURSOR_PROJECT_DIR')
    }
}

# Hook stdin is UTF-8 JSON; [Console]::In with default encoding often corrupts it (invalid JSON / stray dots).
$utf8 = New-Object System.Text.UTF8Encoding $false
$reader = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), $utf8, $true)
$raw = $reader.ReadToEnd()
$reader.Dispose()

if ([string]::IsNullOrWhiteSpace($raw)) {
    [Console]::Error.WriteLine('ittalks-hook: empty stdin')
    exit 0
}

$s = $raw.Trim()
# Strip UTF-8 BOM if present as prefix bytes
if ($s.Length -gt 0 -and $s[0] -eq [char]0xFEFF) {
    $s = $s.Substring(1).Trim()
}

# If wrapper text or noise, keep only outermost JSON object
$i0 = $s.IndexOf('{')
$i1 = $s.LastIndexOf('}')
if ($i0 -ge 0 -and $i1 -gt $i0) {
    $s = $s.Substring($i0, $i1 - $i0 + 1)
}

try {
    $j = $s | ConvertFrom-Json
} catch {
    $preview = $s
    if ($preview.Length -gt 240) {
        $preview = $preview.Substring(0, 240) + '...'
    }
    [Console]::Error.WriteLine('ittalks-hook: invalid JSON - ' + $_.Exception.Message)
    [Console]::Error.WriteLine('ittalks-hook: stdin preview (first 240 chars): ' + $preview)
    exit 0
}

$text = $null
if ($null -ne $j.PSObject.Properties['text']) {
    $text = [string]$j.text
}

if ([string]::IsNullOrWhiteSpace($text)) {
    [Console]::Error.WriteLine('ittalks-hook: no text field (keys: ' + ($j.PSObject.Properties.Name -join ', ') + ')')
    exit 0
}

$max = 400000
if ($text.Length -gt $max) {
    $text = $text.Substring(0, $max)
}

$rtPath = Join-Path $env:LOCALAPPDATA 'ItTalksTTS\runtime.json'
$settingsPath = Join-Path $env:LOCALAPPDATA 'ItTalksTTS\settings.json'
if (-not (Test-Path -LiteralPath $rtPath)) {
    [Console]::Error.WriteLine('ittalks-hook: runtime.json missing -- start ItTalksTTS first.')
    exit 0
}

if (-not (Test-Path -LiteralPath $settingsPath)) {
    [Console]::Error.WriteLine('ittalks-hook: settings.json missing.')
    exit 0
}

$rt = Get-Content -LiteralPath $rtPath -Raw -Encoding UTF8 | ConvertFrom-Json
$settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$token = $null
if ($null -ne $settings.PSObject.Properties['apiToken']) {
    $token = [string]$settings.apiToken
}

if ($null -eq $rt.port -or $rt.port -le 0 -or [string]::IsNullOrWhiteSpace($token)) {
    [Console]::Error.WriteLine('ittalks-hook: invalid port or apiToken in app data files.')
    exit 0
}

$bodyJson = (@{ text = $text; source = 'cursor-hook' } | ConvertTo-Json -Compress)
# Invoke-WebRequest -Body [string] often uses the system ANSI code page and mojibakes smart quotes (e.g. it's -> itâ€™s).
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyJson)
$uri = "http://127.0.0.1:$($rt.port)/v1/queue"
try {
    $resp = Invoke-WebRequest -Uri $uri -Method Post -Body $bodyBytes -ContentType 'application/json; charset=utf-8' `
        -Headers @{ Authorization = "Bearer $token" } -TimeoutSec 20 -UseBasicParsing
    if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 300) {
        [Console]::Error.WriteLine('ittalks-hook: enqueued to The Q (' + $text.Length + ' chars).')
    } else {
        [Console]::Error.WriteLine('ittalks-hook: HTTP ' + [int]$resp.StatusCode + ' ' + $resp.Content)
    }
} catch {
    $msg = $_.Exception.Message
    if ($_.Exception.Response -and $_.ErrorDetails.Message) {
        $msg = $msg + ' -- ' + $_.ErrorDetails.Message
    }
    [Console]::Error.WriteLine('ittalks-hook: API error -- ' + $msg)
}

exit 0
