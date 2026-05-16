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
Windows 64-bit installer.

- Download **ItTalksTTS-Setup.exe** (or **ItTalksTTS-Windows.zip**) and run the setup.
- First launch downloads Kokoro voice models (internet required).
- **Cursor:** user hooks install automatically — restart Cursor, use Agent mode in any project (no repo clone).
"@

$releaseBody = @{
    tag_name = $Tag
    name     = $Title
    body     = $notes
    draft    = $false
} | ConvertTo-Json

$releaseUrl = "https://api.github.com/repos/$Owner/$Repo/releases"
try {
    $release = Invoke-RestMethod -Uri $releaseUrl -Method Post -Headers $headers -Body $releaseBody -ContentType "application/json"
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
if (Test-Path $readme) { Upload-Asset $readme "readme" }

Write-Host ""
Write-Host "Published: $($release.html_url)"
Write-Host "Latest:    https://github.com/$Owner/$Repo/releases/latest"
