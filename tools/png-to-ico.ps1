param(
    [Parameter(Mandatory = $true)][string]$PngPath,
    [Parameter(Mandatory = $true)][string]$IcoPath,
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256)
)

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile((Resolve-Path $PngPath))
$pngChunks = New-Object System.Collections.Generic.List[byte[]]
try {
    foreach ($size in $Sizes) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size
        try {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $g.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
                $g.DrawImage($src, 0, 0, $size, $size)
            }
            finally { $g.Dispose() }

            $ms = New-Object System.IO.MemoryStream
            try {
                $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
                $pngChunks.Add($ms.ToArray())
            }
            finally { $ms.Dispose() }
        }
        finally { $bmp.Dispose() }
    }
}
finally { $src.Dispose() }

$dir = Split-Path -Parent $IcoPath
if ($dir -and -not (Test-Path $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

$fs = [System.IO.File]::Open($IcoPath, [System.IO.FileMode]::Create)
try {
    $writer = New-Object System.IO.BinaryWriter $fs
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$pngChunks.Count)
    $offset = 6 + (16 * $pngChunks.Count)

    for ($i = 0; $i -lt $pngChunks.Count; $i++) {
        $size = $Sizes[$i]
        $png = $pngChunks[$i]
        $writer.Write([byte]([Math]::Min($size, 255)))
        $writer.Write([byte]([Math]::Min($size, 255)))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$png.Length)
        $writer.Write([uint32]$offset)
        $offset += $png.Length
    }

    foreach ($png in $pngChunks) {
        $writer.Write($png)
    }
}
finally { $fs.Dispose() }

Write-Host "Wrote $IcoPath ($($Sizes -join ', ') px)"
