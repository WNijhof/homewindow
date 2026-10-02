# Draws the HomeWindow icon and writes HomeWindow\Assets\HomeWindow.ico with PNG frames of
# 16, 24, 32, 48, 64 and 256 pixels: a roof chevron above a window with one lit pane, on an indigo-violet
# rounded square. With -Preview it also writes a PNG with every size next to each other.
# Run with: powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1 [-Preview file.png]
param([string]$Preview)
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\HomeWindow\Assets\HomeWindow.ico'
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

  # Window: a white frame with four panes; the top left one is lit, as if someone is home
  $fx = 16; $fy = 33; $fw = 32; $fh = 24
  $frame = New-RoundedPath ($fx * $s) ($fy * $s) ($fw * $s) ($fh * $s) (4.5 * $s)
  $g.FillPath((New-Object System.Drawing.SolidBrush $white), $frame)
  $border = if ($size -le 24) { 4 } else { 3.5 }
  $bar = if ($size -le 24) { 4 } else { 3 }
  $pw = ($fw - 2 * $border - $bar) / 2
  $ph = ($fh - 2 * $border - $bar) / 2
  $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 109, 40, 217))
  $lit = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 252, 211, 77))
  foreach ($col in 0, 1) {
    foreach ($row in 0, 1) {
      $px = $fx + $border + $col * ($pw + $bar)
      $py = $fy + $border + $row * ($ph + $bar)
      $pane = New-RoundedPath ($px * $s) ($py * $s) ($pw * $s) ($ph * $s) (1.5 * $s)
      $g.FillPath($(if ($col -eq 0 -and $row -eq 0) { $lit } else { $dark }), $pane)
    }
  }

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
