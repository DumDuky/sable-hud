# Builds Assets/app.ico + Assets/mark.png for Sable HUD.
# Monochrome mark: rounded black tile, a white HUD bar and a soft drop chevron.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'src\SableHUD\Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-Mark([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # tile
    $pad = 6 * $s
    $tile = New-RoundedPath $pad $pad (256 * $s - 2 * $pad) (256 * $s - 2 * $pad) (56 * $s)
    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 11, 11, 13))
    $g.FillPath($bg, $tile)
    if ($size -ge 32) {
        $border = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(46, 255, 255, 255), [Math]::Max(1, 2 * $s))
        $g.DrawPath($border, $tile)
    }

    # HUD bar
    $barW = 118 * $s; $barH = 18 * $s
    $barX = (256 * $s - $barW) / 2; $barY = 84 * $s
    $bar = New-RoundedPath $barX $barY $barW $barH ($barH / 2)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 246, 246, 248))
    $g.FillPath($white, $bar)

    # chevron
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 246, 246, 248), (20 * $s))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pts = @(
        (New-Object System.Drawing.PointF((108 * $s), (124 * $s))),
        (New-Object System.Drawing.PointF((128 * $s), (146 * $s))),
        (New-Object System.Drawing.PointF((148 * $s), (124 * $s)))
    )
    $g.DrawLines($pen, $pts)

    # soft dot
    $dot = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(110, 246, 246, 248))
    $g.FillEllipse($dot, (124 * $s), (178 * $s), (8 * $s), (8 * $s))

    $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = New-Mark $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,($ms.ToArray())
    if ($sz -eq 256) { $bmp.Save((Join-Path $assets 'mark.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    $ms.Dispose()
}

$icoPath = Join-Path $assets 'app.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()
Write-Host "Wrote $icoPath ($((Get-Item $icoPath).Length) bytes)"
