[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$assetDirectory = Join-Path $projectRoot 'src\SoundPartyCompanion.App\Assets'
$iconPath = Join-Path $assetDirectory 'app.ico'

function New-RoundedPath {
    param([System.Drawing.RectangleF]$Bounds, [single]$Radius)
    $diameter = $Radius * 2
    $shape = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $shape.AddArc($Bounds.Left, $Bounds.Top, $diameter, $diameter, 180, 90)
    $shape.AddArc($Bounds.Right - $diameter, $Bounds.Top, $diameter, $diameter, 270, 90)
    $shape.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $shape.AddArc($Bounds.Left, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $shape.CloseFigure()
    return $shape
}

# Reuse TrayIconService's 32-pixel speaker geometry and colors, without a tile.
# Each frame is drawn independently to remain crisp across Windows DPI settings.
$frames = [System.Collections.Generic.List[object]]::new()
foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $body = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(31, 36, 37))
    $outline = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(238, 234, 228), 1.5)
    $accent = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(196, 105, 70))
    $bodyPath = New-RoundedPath ([System.Drawing.RectangleF]::new(8, 3, 16, 26)) 6
    $accentPath = New-RoundedPath ([System.Drawing.RectangleF]::new(20, 8, 2, 15)) 1
    $png = [System.IO.MemoryStream]::new()
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.ScaleTransform($size / 32.0, $size / 32.0)
        $graphics.FillPath($body, $bodyPath)
        $graphics.DrawPath($outline, $bodyPath)
        $graphics.FillPath($accent, $accentPath)
        $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames.Add(@{ Size = $size; Data = $png.ToArray() })
    }
    finally {
        $png.Dispose()
        $accentPath.Dispose()
        $bodyPath.Dispose()
        $accent.Dispose()
        $outline.Dispose()
        $body.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$iconStream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Data.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Data.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
    $writer.Flush()
    New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null
    [System.IO.File]::WriteAllBytes($iconPath, $iconStream.ToArray())
}
finally {
    $writer.Dispose()
    $iconStream.Dispose()
}
Write-Host "Generated $iconPath ($($frames.Count) sizes)."
