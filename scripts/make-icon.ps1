# Draws the HomeyBar icon and writes HomeyBar\Assets\HomeyBar.ico with PNG frames of
# 16, 24, 32, 48, 64 and 256 pixels: a roof chevron above an on/off switch, on an indigo-violet
# rounded square. With -Preview it also writes a PNG with every size next to each other.
# Run with: powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1 [-Preview file.png]
param([string]$Preview)
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\HomeyBar\Assets\HomeyBar.ico'
$sizes = 16, 24, 32, 48, 64, 256

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = 2 * $r
  $p.AddArc($x, $y, $d, $d, 180, 90)
  $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
  $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure()
  return $p
}

function New-Bitmap([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.PixelOffsetMode = 'HighQuality'
  $g.Clear([System.Drawing.Color]::Transparent)
  $s = $size / 64.0

  # Background
  $bg = New-RoundedPath 0 0 ($size - 0.5) ($size - 0.5) (15 * $s)
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $size, $size), ([System.Drawing.Color]::FromArgb(255, 79, 70, 229)), ([System.Drawing.Color]::FromArgb(255, 168, 85, 247))
  $g.FillPath($brush, $bg)

  $white = [System.Drawing.Color]::White
  # Small sizes get thicker strokes so they stay readable in the tray
  $stroke = if ($size -le 24) { 7.5 } else { 6 }

  # Roof chevron
  $pen = New-Object System.Drawing.Pen $white, ($stroke * $s)
  $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
  $g.DrawLines($pen, [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF (15 * $s), (29 * $s)),
    (New-Object System.Drawing.PointF (32 * $s), (14 * $s)),
    (New-Object System.Drawing.PointF (49 * $s), (29 * $s))))

  # Switch: a white pill with the knob on the right ("on")
  $pill = New-RoundedPath (13 * $s) (36 * $s) (38 * $s) (17 * $s) (8.5 * $s)
  $g.FillPath((New-Object System.Drawing.SolidBrush $white), $pill)
  $knob = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 124, 58, 237))
  $k = 11 * $s
  $g.FillEllipse($knob, (51 * $s) - 3 * $s - $k, (36 * $s) + (17 * $s - $k) / 2, $k, $k)

  $g.Dispose()
  return $bmp
}

function Get-Png([int]$size) {
  $bmp = New-Bitmap $size
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  return , $ms.ToArray()
}

$frames = $sizes | ForEach-Object { , (Get-Png $_) }
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

if ($Preview) {
  # Every size on a light and a dark strip, the small ones also enlarged to see the pixels
  $sheet = New-Object System.Drawing.Bitmap 760, 400
  $g = [System.Drawing.Graphics]::FromImage($sheet)
  $g.Clear([System.Drawing.Color]::FromArgb(255, 243, 243, 243))
  $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 32, 32, 32))), 0, 200, 760, 200)
  foreach ($row in 0, 1) {
    $x = 20
    foreach ($sz in 16, 24, 32, 48, 64, 128) {
      $bmp = New-Bitmap $sz
      $g.DrawImage($bmp, $x, 20 + $row * 200, $sz, $sz)
      $x += $sz + 20
      $bmp.Dispose()
    }
    $g.InterpolationMode = 'NearestNeighbor'
    foreach ($sz in 16, 24) {
      $bmp = New-Bitmap $sz
      $g.DrawImage($bmp, $x, 20 + $row * 200, $sz * 5, $sz * 5)
      $x += $sz * 5 + 20
      $bmp.Dispose()
    }
    $g.InterpolationMode = 'HighQualityBicubic'
  }
  $sheet.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
  Write-Output "Preview written to $Preview"
}
