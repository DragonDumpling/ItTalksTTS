param(
    [Parameter(Mandatory = $true)][string]$DestDir,
    [string]$PythonVersion = "3.12.7"
)

$ErrorActionPreference = "Stop"
$arch = "amd64"
$zipName = "python-$PythonVersion-embed-$arch.zip"
$url = "https://www.python.org/ftp/python/$PythonVersion/$zipName"
$toolsDir = Join-Path $PSScriptRoot "python-cache"
$zipPath = Join-Path $toolsDir $zipName
$getPipPath = Join-Path $toolsDir "get-pip.py"

New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
if (-not (Test-Path $zipPath)) {
    Write-Host "Downloading $url ..."
    Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing
}

if (-not (Test-Path $getPipPath)) {
    Write-Host "Downloading get-pip.py ..."
    Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile $getPipPath -UseBasicParsing
}

if (Test-Path $DestDir) { Remove-Item -Recurse -Force $DestDir }
New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
Expand-Archive -Path $zipPath -DestinationPath $DestDir -Force
Copy-Item $getPipPath (Join-Path $DestDir "get-pip.py") -Force

$pth = Get-ChildItem -Path $DestDir -Filter "python*._pth" | Select-Object -First 1
if ($pth) {
    $lines = Get-Content $pth.FullName
    $out = foreach ($line in $lines) {
        if ($line -match '^\s*#\s*import site') { 'import site' }
        else { $line }
    }
    if ($out -notcontains 'import site') { $out += 'import site' }
    Set-Content -Path $pth.FullName -Value $out -Encoding ascii
}

Write-Host "Bundled embed Python $PythonVersion -> $DestDir"
