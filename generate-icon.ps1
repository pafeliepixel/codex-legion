[CmdletBinding()]
param()

Add-Type -AssemblyName System.Drawing

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$assetDirectory = Join-Path $projectRoot 'assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null

function New-RoundedPath {
    param([System.Drawing.RectangleF]$Rectangle, [float]$Radius)

    $diameter = $Radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($Rectangle.Left, $Rectangle.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rectangle.Left, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-AppIconBitmap {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $inset = [single]([Math]::Max(1, $Size * 0.055))
    $shadowOffset = [single]([Math]::Max(1, $Size * 0.025))
    $radius = [single]($Size * 0.22)
    $tile = New-Object System.Drawing.RectangleF $inset, $inset, ($Size - 2 * $inset), ($Size - 2 * $inset)
    $shadowRect = New-Object System.Drawing.RectangleF $inset, ($inset + $shadowOffset), ($Size - 2 * $inset), ($Size - 2 * $inset)
    $shadowPath = New-RoundedPath $shadowRect $radius
    $shadowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(105, 10, 6, 18))
    $graphics.FillPath($shadowBrush, $shadowPath)

    $tilePath = New-RoundedPath $tile $radius
    $start = New-Object System.Drawing.PointF 0, 0
    $finish = New-Object System.Drawing.PointF $Size, $Size
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush $start, $finish, ([System.Drawing.Color]::FromArgb(255, 156, 131, 247)), ([System.Drawing.Color]::FromArgb(255, 255, 139, 107))
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend
    $blend.Positions = [single[]](0, 0.55, 1)
    $blend.Colors = [System.Drawing.Color[]]@(
        [System.Drawing.Color]::FromArgb(255, 156, 131, 247),
        [System.Drawing.Color]::FromArgb(255, 202, 126, 199),
        [System.Drawing.Color]::FromArgb(255, 255, 139, 107)
    )
    $gradient.InterpolationColors = $blend
    $graphics.FillPath($gradient, $tilePath)

    $highlightPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(100, 255, 255, 255)), ([single][Math]::Max(0.7, $Size * 0.008))
    $graphics.DrawPath($highlightPen, $tilePath)

    $slashWidth = [single][Math]::Max(1.5, $Size * 0.085)
    $slashPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(235, 24, 14, 32)), $slashWidth
    $slashPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $slashPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($slashPen, [single]($Size * 0.30), [single]($Size * 0.69), [single]($Size * 0.45), [single]($Size * 0.31))
    $graphics.DrawLine($slashPen, [single]($Size * 0.53), [single]($Size * 0.69), [single]($Size * 0.68), [single]($Size * 0.31))

    $slashPen.Dispose()
    $highlightPen.Dispose()
    $gradient.Dispose()
    $tilePath.Dispose()
    $shadowBrush.Dispose()
    $shadowPath.Dispose()
    $graphics.Dispose()
    return $bitmap
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$images = New-Object System.Collections.Generic.List[byte[]]

foreach ($size in $sizes) {
    $bitmap = New-AppIconBitmap $size
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())
    if ($size -eq 256) {
        $bitmap.Save((Join-Path $assetDirectory 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $stream.Dispose()
    $bitmap.Dispose()
}

$iconPath = Join-Path $assetDirectory 'CodexNativeLite.ico'
$file = [System.IO.File]::Open($iconPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($index = 0; $index -lt $sizes.Count; $index++) {
    $size = $sizes[$index]
    $bytes = $images[$index]
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $bytes.Length
}

foreach ($bytes in $images) {
    $writer.Write($bytes)
}

$writer.Dispose()
$file.Dispose()
Write-Host "Generated $iconPath"
