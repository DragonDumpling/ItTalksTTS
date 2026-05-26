# Create a GitHub release and upload release\ItTalksTTS-Setup.exe (uses git credentials / GitHub Desktop login).
param(
    [string]$Tag = "v0.1.0",
    [string]$Title = "ItTalksTTS 0.1.0",
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

$credIn = "protocol=https`nhost=github.com`n`n"
$credOut = $credIn | git credential fill 2>$null
$token = ($credOut | Where-Object { $_ -like "password=*" } | ForEach-Object { $_ -replace "^password=", "" })
if (-not $token) { throw "No GitHub token from git credential. Sign in with GitHub Desktop, then retry." }

$headers = @{
    Authorization        = "Bearer $token"
    Accept               = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}

$notes = @"
## What's new in 0.1.2

- **In-app updates** - update button downloads and installs the latest release automatically
- **The Q improvements** - copy text, send to Paste, error handling fixes, autoplay continues from selected item
- **Cursor hooks** - fixed duplicate enqueue when user + project hooks both ran

## Install

- Download **ItTalksTTS-Setup.exe** and run the setup
- First launch downloads Kokoro voice models (internet required)
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
