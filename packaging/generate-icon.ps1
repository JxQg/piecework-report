[CmdletBinding()]
param(
    [string]$SourcePath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assetDirectory = Join-Path $repositoryRoot 'src\PieceworkReport.Launcher\Assets'
$webDirectory = Join-Path $repositoryRoot 'src\PieceworkReport.Web\wwwroot'
if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Join-Path $assetDirectory 'app-icon-source.png'
}
$SourcePath = [System.IO.Path]::GetFullPath($SourcePath)
if (-not (Test-Path -LiteralPath $SourcePath)) {
    throw "Icon source image was not found: $SourcePath"
}

$pngPath = Join-Path $assetDirectory 'app-icon.png'
$iconPath = Join-Path $assetDirectory 'app-icon.ico'
$faviconPath = Join-Path $webDirectory 'favicon.ico'
New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null

function Add-RoundedRectangle {
    param(
        [System.Drawing.Drawing2D.GraphicsPath]$Path,
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $Path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $Path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $Path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $Path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $Path.CloseFigure()
}

function Convert-ToPngBytes {
    param([System.Drawing.Bitmap]$Source, [int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $drawing = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $drawing.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $drawing.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $drawing.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $drawing.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $drawing.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $drawing.Clear([System.Drawing.Color]::Transparent)
        $drawing.DrawImage($Source, [System.Drawing.Rectangle]::new(0, 0, $Size, $Size))
        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return ,$stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $drawing.Dispose()
        $bitmap.Dispose()
    }
}

$source = [System.Drawing.Image]::FromFile($SourcePath)
$master = [System.Drawing.Bitmap]::new(1024, 1024, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($master)
$clipPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
try {
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    Add-RoundedRectangle -Path $clipPath -X 8 -Y 8 -Width 1008 -Height 1008 -Radius 190
    $graphics.SetClip($clipPath)
    $graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, 1024, 1024))
}
finally {
    $clipPath.Dispose()
    $graphics.Dispose()
    $source.Dispose()
}

$pngBytes = [byte[]](Convert-ToPngBytes -Source $master -Size 512)
[System.IO.File]::WriteAllBytes($pngPath, $pngBytes)

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = foreach ($size in $sizes) {
    [pscustomobject]@{ Size = $size; Bytes = [byte[]](Convert-ToPngBytes -Source $master -Size $size) }
}
$master.Dispose()

$iconStream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }
    foreach ($image in $images) {
        $writer.Write([byte[]]$image.Bytes)
    }
    $writer.Flush()
    $iconBytes = $iconStream.ToArray()
}
finally {
    $writer.Dispose()
    $iconStream.Dispose()
}

$expectedIconLength = 6 + 16 * $images.Count + ($images | Measure-Object -Property { $_.Bytes.Length } -Sum).Sum
if ($iconBytes.Length -ne $expectedIconLength) {
    throw "Generated icon length is invalid. Expected $expectedIconLength bytes, got $($iconBytes.Length)."
}

[System.IO.File]::WriteAllBytes($iconPath, $iconBytes)
[System.IO.File]::WriteAllBytes($faviconPath, $iconBytes)

Write-Host "Source: $SourcePath"
Write-Host "PNG: $pngPath"
Write-Host "ICO: $iconPath"
Write-Host "Favicon: $faviconPath"
