# Create a GitHub release and upload release\ItTalksTTS-Setup.exe (uses git credentials / GitHub Desktop login).
param(
    [string]$Tag = "v0.3.0",
    [string]$Title = "ItTalksTTS 0.3.0",
    [string]$Owner = "DragonDumpling",
    [string]$Repo = "ItTalksTTS"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$setup = Join-Path $root "release\ItTalksTTS-Setup.exe"
$readme = Join-Path $root "release\README.txt"

if (-not (Test-Path $setup)) {
    throw "Missing $setup. Run .\installer\build.ps1 first."
}

# Feed git credential via a temp file through cmd's input redirection. Piping a
# string from PowerShell 5.1 prepends a UTF-8 BOM that breaks git's parser.
$credTmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($credTmp, "protocol=https`nhost=github.com`n`n", (New-Object System.Text.UTF8Encoding($false)))
try {
    $credOut = cmd /c "git credential fill < `"$credTmp`"" 2>$null
}
finally {
    Remove-Item $credTmp -Force -ErrorAction SilentlyContinue
}
$token = ($credOut | Where-Object { $_ -like "password=*" } | ForEach-Object { $_ -replace "^password=", "" })
if (-not $token) { throw "No GitHub token from git credential. Sign in with GitHub Desktop, then retry." }

$headers = @{
    Authorization        = "Bearer $token"
    Accept               = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}

$notes = @"
## What's new in 0.3.0

- **Optional speech preprocessing** — install a small (3B) open-source LLM that runs
  locally and rewrites text before it's spoken. Makes Cursor/Claude output sound more
  natural and shorter, and replaces ear-fatiguing blobs (API keys, hashes, URLs) with
  spoken descriptions. Fully optional, with a tooltip explaining what it does. Voice tab.
- **Word-by-word highlighting** in the Selected text field as each word is spoken. When
  preprocessing is on, timing is baked from per-word syllable counts for accurate
  highlighting; falls back to length-based estimates when off.
- **Per-phase progress in The Q** — the State column now shows an inline progress bar
  with a short label (Pre / TTS / Playing) for the current clip instead of jumping to Playing.
- **Fixed: queue stalled on Pending** — stopping while a clip was synthesizing no longer
  marks the TTS worker as broken, so playback continues to work for later clips.
- **Fixed: selected row unreadable** — selecting a row and clicking Play selected no
  longer turns the row white when the grid loses focus.
- Preserves the original text layout (line breaks, separators) in the Selected text view.

## Install

- Download **ItTalksTTS-Setup.exe** and run the setup
- First launch downloads Kokoro voice models (internet required)
- **F5-TTS** is optional: pick it on the Voice tab, then run Setup / Repair (needs system Python 3.10-3.12)
- **Speech preprocessing** is optional: enable it on the Voice tab, then Install (needs system Python 3.10-3.13; downloads a ~2GB GGUF model on first setup)
- **Cursor:** user hooks install automatically — restart Cursor, use Agent mode in any project
"@

$releaseBody = @{
    tag_name = $Tag
    name     = $Title
    body     = $notes
    draft    = $false
} | ConvertTo-Json -Depth 5

$releaseBytes = [System.Text.Encoding]::UTF8.GetBytes($releaseBody)

$releaseUrl = "https://api.github.com/repos/$Owner/$Repo/releases"
try {
    $release = Invoke-RestMethod -Uri $releaseUrl -Method Post -Headers $headers -Body $releaseBytes -ContentType "application/json; charset=utf-8"
}
catch {
    if ($_.ErrorDetails.Message -match "already_exists") {
        $release = Invoke-RestMethod -Uri "$releaseUrl/tags/$Tag" -Headers $headers
        Write-Host "Release $Tag already exists; uploading assets to it."
    }
    else { throw }
}

function Upload-Asset([string]$path, [string]$label) {
    $name = [Uri]::EscapeDataString([IO.Path]::GetFileName($path))
    $uploadUrl = "https://uploads.github.com/repos/$Owner/$Repo/releases/$($release.id)/assets?name=$name"
    Write-Host "Uploading $label..."
    Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers @{
        Authorization = $headers.Authorization
        Accept        = "application/vnd.github+json"
    } -ContentType "application/octet-stream" -InFile $path | Out-Null
}

Upload-Asset $setup "installer"
$zip = Join-Path $root "release\ItTalksTTS-Windows.zip"
if (Test-Path $zip) { Upload-Asset $zip "zip" }
if (Test-Path $readme) { Upload-Asset $readme "readme" }

Write-Host ""
Write-Host "Published: $($release.html_url)"
Write-Host "Latest:    https://github.com/$Owner/$Repo/releases/latest"
