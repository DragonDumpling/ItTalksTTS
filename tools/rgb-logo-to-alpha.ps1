# Convert RGB logo (black backdrop) to 32-bit PNG with alpha for WPF.
param(
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [Parameter(Mandatory = $true)][string]$DestPath
)

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $SourcePath))
try {
    $out = New-Object System.Drawing.Bitmap $src.Width, $src.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y = 0; $y -lt $src.Height; $y++) {
        for ($x = 0; $x -lt $src.Width; $x++) {
            $c = $src.GetPixel($x, $y)
            if ($c.R -lt 28 -and $c.G -lt 28 -and $c.B -lt 28) {
                $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
            }
            else {
                $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $c.R, $c.G, $c.B))
            }
        }
    }

    $dir = Split-Path -Parent $DestPath
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $tmp = "$DestPath.part.png"
    $out.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
    $out.Dispose()
    if (Test-Path $DestPath) { Remove-Item $DestPath -Force }
    Move-Item $tmp $DestPath
    Write-Host "Wrote $DestPath ($($src.Width)x$($src.Height), Format32bppArgb)"
}
finally { $src.Dispose() }
