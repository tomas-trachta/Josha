#requires -Version 7.0
<#
.SYNOPSIS
    Generates installer\Josha.ico from scratch using GDI+.
.DESCRIPTION
    Draws a multi-resolution .ico (16/24/32/48/64/128/256) in a dual-pane
    motif: rounded teal square with two vertical white panels. Re-run any
    time you want to tweak the design — adjust the colors at the top.
#>
[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'Josha.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# ---------- Design ----------
$bgTop      = [System.Drawing.Color]::FromArgb(255, 22, 154, 138)   # teal
$bgBottom   = [System.Drawing.Color]::FromArgb(255, 14,  98,  88)   # darker teal
$paneFill   = [System.Drawing.Color]::FromArgb(255, 240, 248, 250)  # near-white
$paneHeader = [System.Drawing.Color]::FromArgb(255,  82, 196, 180)  # accent for top stripe
$paneShadow = [System.Drawing.Color]::FromArgb(80,   0,   0,   0)   # subtle shadow

$sizes = @(16, 24, 32, 48, 64, 128, 256)

# ---------- Helpers ----------
function New-RoundedRectPath {
    param([float]$x, [float]$y, [float]$w, [float]$h, [float]$r)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    if ($r -le 0) {
        $path.AddRectangle([System.Drawing.RectangleF]::new($x, $y, $w, $h))
        return $path
    }
    $d = $r * 2
    $path.AddArc($x,           $y,           $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y,           $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d,   0, 90)
    $path.AddArc($x,           $y + $h - $d, $d, $d,  90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$size)

    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        # Background — rounded square with vertical gradient.
        # Drop the corner radius on tiny sizes so it doesn't eat the silhouette.
        $cornerR = [Math]::Max(0, [int]($size * 0.18))
        if ($size -le 20) { $cornerR = [int]($size * 0.10) }

        $bgRect = [System.Drawing.RectangleF]::new(0.5, 0.5, $size - 1, $size - 1)
        $bgPath = New-RoundedRectPath $bgRect.X $bgRect.Y $bgRect.Width $bgRect.Height $cornerR
        $bgBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.PointF]::new(0, 0),
            [System.Drawing.PointF]::new(0, $size),
            $bgTop, $bgBottom)
        $g.FillPath($bgBrush, $bgPath)
        $bgBrush.Dispose()

        # Two panes inside.
        $pad  = [Math]::Max(2, [int]($size * 0.18))
        $gap  = [Math]::Max(1, [int]($size * 0.06))
        $paneW = [int](($size - 2 * $pad - $gap) / 2)
        $paneH = $size - 2 * $pad
        $paneR = if ($size -ge 32) { [int]($size * 0.04) } else { 0 }
        $headerH = [Math]::Max(1, [int]($paneH * 0.16))

        $pane1X = $pad
        $pane2X = $pad + $paneW + $gap
        $paneY  = $pad

        foreach ($x in @($pane1X, $pane2X)) {
            # Soft shadow underneath (skip on tiny sizes — just noise).
            if ($size -ge 48) {
                $shadowOffset = [Math]::Max(1, [int]($size * 0.012))
                $shadowPath = New-RoundedRectPath ($x + $shadowOffset) ($paneY + $shadowOffset) $paneW $paneH $paneR
                $shadowBrush = [System.Drawing.SolidBrush]::new($paneShadow)
                $g.FillPath($shadowBrush, $shadowPath)
                $shadowBrush.Dispose()
                $shadowPath.Dispose()
            }

            $panePath = New-RoundedRectPath $x $paneY $paneW $paneH $paneR
            $paneBrush = [System.Drawing.SolidBrush]::new($paneFill)
            $g.FillPath($paneBrush, $panePath)
            $paneBrush.Dispose()

            # Header stripe — only when there's enough room to read it.
            if ($size -ge 32) {
                $headerPath = New-RoundedRectPath $x $paneY $paneW $headerH $paneR
                $headerRect = [System.Drawing.RectangleF]::new($x, $paneY + $headerH / 2, $paneW, $headerH / 2)
                $g.SetClip($headerPath)
                $headerBrush = [System.Drawing.SolidBrush]::new($paneHeader)
                $g.FillPath($headerBrush, $headerPath)
                $g.FillRectangle($headerBrush, $headerRect)
                $headerBrush.Dispose()
                $g.ResetClip()
                $headerPath.Dispose()
            }

            $panePath.Dispose()
        }

        $bgPath.Dispose()
    } finally {
        $g.Dispose()
    }

    return $bmp
}

function Save-IconFile {
    param(
        [string] $Path,
        [System.Collections.IList] $Bitmaps
    )

    $pngs = @()
    foreach ($bmp in $Bitmaps) {
        $ms = [System.IO.MemoryStream]::new()
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs += ,$ms.ToArray()
        $ms.Dispose()
    }

    $out = [System.IO.MemoryStream]::new()
    $w   = [System.IO.BinaryWriter]::new($out)

    $w.Write([uint16]0)               # reserved
    $w.Write([uint16]1)               # type: icon
    $w.Write([uint16]$Bitmaps.Count)  # image count

    $offset = 6 + (16 * $Bitmaps.Count)
    for ($i = 0; $i -lt $Bitmaps.Count; $i++) {
        $bmp = $Bitmaps[$i]
        $png = $pngs[$i]
        $bw = if ($bmp.Width  -ge 256) { 0 } else { $bmp.Width }
        $bh = if ($bmp.Height -ge 256) { 0 } else { $bmp.Height }
        $w.Write([byte]$bw)
        $w.Write([byte]$bh)
        $w.Write([byte]0)             # palette
        $w.Write([byte]0)             # reserved
        $w.Write([uint16]1)           # planes
        $w.Write([uint16]32)          # bit count
        $w.Write([uint32]$png.Length)
        $w.Write([uint32]$offset)
        $offset += $png.Length
    }

    foreach ($png in $pngs) { $w.Write($png) }

    $w.Flush()
    [System.IO.File]::WriteAllBytes($Path, $out.ToArray())
    $w.Dispose()
    $out.Dispose()
}

# ---------- Main ----------
$bitmaps = foreach ($s in $sizes) { New-IconBitmap -size $s }
try {
    Save-IconFile -Path $OutputPath -Bitmaps $bitmaps
    $info = Get-Item $OutputPath
    Write-Host "Wrote $($info.FullName) ($([math]::Round($info.Length / 1KB, 1)) KB, $($sizes.Count) frames)" -ForegroundColor Green
} finally {
    foreach ($b in $bitmaps) { $b.Dispose() }
}
