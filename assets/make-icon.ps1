# Generates the FlashDesk app icons (decided 2026-07-30, Conor's direction, proportion P3):
#   FlashDesk.ico      - IDLE: the brand mark — a bold two-stroke angular form in vivid green
#                        (#2BD16B) on a near-black tile (#17191E). The DESCENDING stroke is the
#                        long one on purpose: a checkmark is short-down/long-up, and this inverse
#                        proportion is what stops the mark reading as a check.
#   FlashDesk-live.ico - LIVE: the same mark turned AMBER plus a badge bump top-right (the long
#                        descending stroke owns the bottom-right corner). Colour change AND
#                        silhouette change — never colour alone. Shown while a viewer is
#                        connected; extends hard rule 2 to the taskbar.
# Colour values mirror Theme.cs (BrandGreen / BrandTile / AmberFill) — PowerShell cannot read the
# C# constants, so a change there must be repeated here.
#
# ICO layout: 16/24/32/48 as 32-bit BMP entries, 256 as a PNG entry. Re-run to regenerate.

param([string]$OutDir = $PSScriptRoot)

Add-Type -AssemblyName System.Drawing

$Tile  = [System.Drawing.Color]::FromArgb(0x17, 0x19, 0x1E)
$Green = [System.Drawing.Color]::FromArgb(0x2B, 0xD1, 0x6B)
$Amber = [System.Drawing.Color]::FromArgb(0xF4, 0xB4, 0x00)
$Clear = [System.Drawing.Color]::FromArgb(0, 0, 0, 0)

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return ,$p
}

function New-MarkBitmap([int]$size, [bool]$live) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $u = $size / 16.0

    $tilePath = New-RoundedPath (1 * $u) (1 * $u) (14 * $u) (14 * $u) (3.5 * $u)
    $tb = New-Object System.Drawing.SolidBrush($Tile)
    $g.FillPath($tb, $tilePath)

    # Proportion P3 on the 16 grid: long steep descent, short ascent, sharp miter corner.
    #
    # ⚠ FITTED 2026-08-06 — the mark used to be drawn at (4.8,2.8) (10.2,13.0) (12.4,8.6) with a
    # 2.6 stroke, and it DID NOT FIT ITS OWN TILE. Rendered and measured, the stroked outline
    # spanned y 2.219..15.844 against a tile interior of 1.000..15.000: the miter tip hung 0.844
    # grid units BELOW the tile (the tip extends 2.841 units past the vertex, because the interior
    # angle is 54.46 deg and the miter ratio is 2.1854 — GDI+ MiterLimit defaults to 10, so nothing
    # bevels it away). It was also off-centre: 2.656 units of clear tile on the left against 1.406
    # on the right. On a LIGHT taskbar the protruding tip read as one stray green pixel outside the
    # tile; on a dark one it was invisible, because the tile itself is invisible there (#17191E on a
    # dark panel measures 1.08:1).
    #
    # The fix moves and shrinks the PATH — points scaled by 0.8807 about the mark's own centre and
    # re-centred on the tile. Angles and the P3 proportion (long descent, short ascent) are
    # untouched, so the shape question that cost seven rounds is not reopened.
    #
    # THE STROKE WIDTH DELIBERATELY DOES NOT SCALE, and that is the one place this is not a pure
    # similarity transform. Scaling it too would have given 2.290, and measured at 16 px that drops
    # the mark from 37 solid-core pixels to 29 and leaves the bottom terminal with no solid pixel at
    # all — on the taskbar, which is the surface the icon exists for. A 2.6 stroke at 16 px was
    # already measured as surviving "with no margin to spare", so thinning it trades a defect
    # visible at 32 px and above for a worse one at 16. Rendered check at the new position: the full
    # 2.6 stroke leaves 2.500 units of clear tile left and right, 0.906 above and 0.656 below — it
    # fits with room. Re-run scratchpad\stroke-test.ps1 before changing either number.
    $pts = @(
        (New-Object System.Drawing.PointF((4.631 * $u), (2.512 * $u))),
        (New-Object System.Drawing.PointF((9.387 * $u), (11.495 * $u))),
        (New-Object System.Drawing.PointF((11.325 * $u), (7.620 * $u)))
    )
    $strokeColor = if ($live) { $Amber } else { $Green }
    $pen = New-Object System.Drawing.Pen($strokeColor, (2.6 * $u))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Miter
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Flat
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Flat
    $g.DrawLines($pen, [System.Drawing.PointF[]]$pts)
    $pen.Dispose()

    if ($live) {
        # Badge bump top-right; the separation ring is punched first so it reads on any taskbar.
        $cx = 12.6; $cy = 3.6; $ringR = 4.2; $badgeR = 3.2
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $cb = New-Object System.Drawing.SolidBrush($Clear)
        $g.FillEllipse($cb, (($cx - $ringR) * $u), (($cy - $ringR) * $u), (2 * $ringR * $u), (2 * $ringR * $u))
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $ab = New-Object System.Drawing.SolidBrush($Amber)
        $g.FillEllipse($ab, (($cx - $badgeR) * $u), (($cy - $badgeR) * $u), (2 * $badgeR * $u), (2 * $badgeR * $u))
        $cb.Dispose(); $ab.Dispose()
    }
    $tb.Dispose(); $tilePath.Dispose(); $g.Dispose()
    return ,$bmp
}

function ConvertTo-BmpIconEntry([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $maskStride = [int]([Math]::Ceiling($w / 32.0) * 4)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int32]40)
    $bw.Write([int32]$w)
    $bw.Write([int32]($h * 2))
    $bw.Write([int16]1)
    $bw.Write([int16]32)
    $bw.Write([int32]0)
    $bw.Write([int32]($w * $h * 4 + $maskStride * $h))
    $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([int32]0)
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $maskRow = New-Object byte[] $maskStride
    for ($y = 0; $y -lt $h; $y++) { $bw.Write($maskRow) }
    $bw.Flush()
    return ,$ms.ToArray()
}

function ConvertTo-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

function Write-Ico([string]$path, [bool]$live) {
    $entries = @()
    foreach ($s in 16, 24, 32, 48) {
        $bmp = New-MarkBitmap $s $live
        $entries += ,@{ Size = $s; Bytes = (ConvertTo-BmpIconEntry $bmp) }
        $bmp.Dispose()
    }
    $big = New-MarkBitmap 256 $live
    $entries += ,@{ Size = 256; Bytes = (ConvertTo-PngBytes $big) }
    $big.Dispose()

    $count = $entries.Count
    $offset = 6 + 16 * $count
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([int16]0)
    $bw.Write([int16]1)
    $bw.Write([int16]$count)
    foreach ($e in $entries) {
        $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
        $bw.Write([byte]$dim)
        $bw.Write([byte]$dim)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([int16]1)
        $bw.Write([int16]32)
        $bw.Write([int32]$e.Bytes.Length)
        $bw.Write([int32]$offset)
        $offset += $e.Bytes.Length
    }
    foreach ($e in $entries) { $bw.Write($e.Bytes) }
    $bw.Flush(); $fs.Close()
    Write-Output "Wrote $path"
}

Write-Ico (Join-Path $OutDir 'FlashDesk.ico') $false
Write-Ico (Join-Path $OutDir 'FlashDesk-live.ico') $true
