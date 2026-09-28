<#
.SYNOPSIS
  Regenerates the app icon (.ico) and MSIX logo PNGs in src/WdiCheck.App/Assets.
  Windows PowerShell 5.1 (System.Drawing) is enough; no external tools.
#>
[CmdletBinding()]
param(
    [string]$OutDir
)
$ErrorActionPreference = 'Stop'
# $PSScriptRoot is not populated inside param() defaults on Windows PowerShell 5.1.
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot '..\src\WdiCheck.App\Assets' }
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Draws the mark (a chip with a check) centred in a box of side $s at ($ox,$oy).
function Draw-Mark([System.Drawing.Graphics]$g, [float]$ox, [float]$oy, [float]$s, [bool]$tile) {
    if ($tile) {
        $bg = New-RoundedPath $ox $oy $s $s ($s * 0.22)
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF($ox, $oy)), (New-Object System.Drawing.PointF(($ox + $s), ($oy + $s))),
            [System.Drawing.Color]::FromArgb(255, 57, 135, 229), [System.Drawing.Color]::FromArgb(255, 16, 66, 129))
        $g.FillPath($brush, $bg)
    }
    $white = [System.Drawing.Color]::White
    $pinPen = New-Object System.Drawing.Pen($white, [Math]::Max(1, $s * 0.045))
    $chipPen = New-Object System.Drawing.Pen($white, [Math]::Max(1, $s * 0.06))
    $chip = $s * 0.46; $cx = $ox + ($s - $chip) / 2; $cy = $oy + ($s - $chip) / 2
    # pins
    foreach ($i in 0..2) {
        $t = $cx + $chip * (0.25 + 0.25 * $i)
        $g.DrawLine($pinPen, $t, $cy - $s * 0.09, $t, $cy)
        $g.DrawLine($pinPen, $t, $cy + $chip, $t, $cy + $chip + $s * 0.09)
        $u = $cy + $chip * (0.25 + 0.25 * $i)
        $g.DrawLine($pinPen, $cx - $s * 0.09, $u, $cx, $u)
        $g.DrawLine($pinPen, $cx + $chip, $u, $cx + $chip + $s * 0.09, $u)
    }
    $body = New-RoundedPath $cx $cy $chip $chip ($chip * 0.14)
    $g.DrawPath($chipPen, $body)
    # check mark
    $checkPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 98, 214, 98), [Math]::Max(1.5, $s * 0.075))
    $checkPen.StartCap = 'Round'; $checkPen.EndCap = 'Round'; $checkPen.LineJoin = 'Round'
    $pts = @(
        (New-Object System.Drawing.PointF(($cx + $chip * 0.24), ($cy + $chip * 0.52))),
        (New-Object System.Drawing.PointF(($cx + $chip * 0.43), ($cy + $chip * 0.70))),
        (New-Object System.Drawing.PointF(($cx + $chip * 0.78), ($cy + $chip * 0.30)))
    )
    $g.DrawLines($checkPen, $pts)
}

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

function Save-Png([int]$w, [int]$h, [string]$name, [float]$markScale = 1.0, [bool]$tile = $true) {
    $bmp, $g = New-Canvas $w $h
    $s = [Math]::Min($w, $h) * $markScale
    Draw-Mark $g (($w - $s) / 2) (($h - $s) / 2) $s $tile
    $bmp.Save((Join-Path $OutDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

Save-Png 44 44 'Square44x44Logo.png'
Save-Png 24 24 'Square44x44Logo.targetsize-24_altform-unplated.png'
Save-Png 48 48 'Square44x44Logo.targetsize-48_altform-unplated.png'
Save-Png 150 150 'Square150x150Logo.png' 0.6
Save-Png 310 150 'Wide310x150Logo.png' 0.6
Save-Png 50 50 'StoreLogo.png'
Save-Png 620 300 'SplashScreen.png' 0.6
Save-Png 24 24 'LockScreenLogo.png'

# Multi-resolution ICO with PNG-compressed entries (Vista+ format).
$sizes = 16, 24, 32, 48, 64, 256
$pngs = foreach ($sz in $sizes) {
    $bmp, $g = New-Canvas $sz $sz
    Draw-Mark $g 0 0 $sz $true
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    , $ms.ToArray()
}
$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ico)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'AppIcon.ico'), $ico.ToArray())
Write-Host "Assets written to $OutDir"
