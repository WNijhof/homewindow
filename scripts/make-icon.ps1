# Draws the HomeyBar icon (a white house on a blue-teal rounded square) and writes
# HomeyBar\Assets\HomeyBar.ico with PNG frames of 16, 24, 32, 48, 64 and 256 pixels.
# Run with: powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\HomeyBar\Assets\HomeyBar.ico'
$sizes = 16, 24, 32, 48, 64, 256

function New-Frame([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.Clear([System.Drawing.Color]::Transparent)

  $s = $size / 64.0
  $r = 14 * $s
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $w = $size - 1
  $path.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
  $path.AddArc($w - 2 * $r, 0, 2 * $r, 2 * $r, 270, 90)
  $path.AddArc($w - 2 * $r, $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
  $path.AddArc(0, $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
  $path.CloseFigure()
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $size, $size), ([System.Drawing.Color]::FromArgb(255, 37, 99, 235)), ([System.Drawing.Color]::FromArgb(255, 20, 184, 166))
  $g.FillPath($brush, $path)

  # House: roof triangle and body, with a door cut out
  $white = [System.Drawing.Brushes]::White
  $roof = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF (32 * $s), (13 * $s)),
    (New-Object System.Drawing.PointF (53 * $s), (32 * $s)),
    (New-Object System.Drawing.PointF (11 * $s), (32 * $s)))
  $g.FillPolygon($white, $roof)
  $g.FillRectangle($white, 17 * $s, 30 * $s, 30 * $s, 21 * $s)
  $door = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 28, 140, 200))
  $g.FillRectangle($door, 28 * $s, 38 * $s, 8 * $s, 13 * $s)

  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  return , $ms.ToArray()
}

$frames = $sizes | ForEach-Object { , (New-Frame $_) }
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $sz = $sizes[$i]; $data = $frames[$i]
  $b = if ($sz -ge 256) { 0 } else { $sz }
  $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32)
  $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
  $offset += $data.Length
}
foreach ($data in $frames) { $bw.Write([byte[]]$data) }
$bw.Close()
Write-Output "Icon written to $out"
