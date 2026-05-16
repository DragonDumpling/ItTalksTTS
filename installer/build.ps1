# Publish ItTalksTTS + MCP + embedded Python, then compile Inno Setup installer.
# Requires: .NET 9 SDK, Inno Setup 6 (iscc.exe on PATH or default install path).

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishApp = Join-Path $root "publish\App"
$publishMcp = Join-Path $root "publish\Mcp"
$dist = Join-Path $root "dist"
$embedDir = Join-Path $publishApp "python-embed"

Write-Host "Publishing app..."
dotnet publish (Join-Path $root "src\ItTalksTTS.App\ItTalksTTS.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishApp

Write-Host "Bundling embedded Python (for end-user install, no separate Python needed)..."
& (Join-Path $root "tools\bundle-python-embed.ps1") -DestDir $embedDir

Write-Host "Publishing MCP server..."
dotnet publish (Join-Path $root "src\ItTalksTTS.McpServer\ItTalksTTS.McpServer.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishMcp

$isccPath = $null
if (Get-Command iscc -ErrorAction SilentlyContinue) { $isccPath = (Get-Command iscc).Source }
if (-not $isccPath) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { $isccPath = $c; break } }
}
if (-not $isccPath) {
    throw "Inno Setup 6 not found. Install from https://jrsoftware.org/isinfo.php (winget install JRSoftware.InnoSetup)."
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Write-Host "Compiling installer..."
& $isccPath (Join-Path $PSScriptRoot "ittalks.iss")

Write-Host ""
Write-Host "Done. Give users this file:"
Write-Host "  $dist\ItTalksTTS-Setup.exe"
