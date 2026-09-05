<#
Generate the QuickReplyKit app icon (multi-size PNG-based .ico).
Draws an indigo rounded square with a white speech bubble + reply arrow.
Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1 -Out src\QuickReplyKit\Assets\app.ico
#>
param([Parameter(Mandatory=$true)][string]$Out)

Add-Type -AssemblyName System.Drawing

$sizes = @(16,24,32,48,64,128,256)
$pngList = New-Object 'System.Collections.Generic.List[byte[]]'

foreach ($size in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)

  # Rounded-square background with a vertical indigo gradient
  $pad = [Math]::Max(1, [int]($size * 0.06))
  $rectF = New-Object System.Drawing.RectangleF($pad, $pad, ($size-2*$pad), ($size-2*$pad))
  $d = [single]($size * 0.22)
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc($rectF.X, $rectF.Y, $d, $d, 180, 90)
  $path.AddArc($rectF.Right-$d, $rectF.Y, $d, $d, 270, 90)
  $path.AddArc($rectF.Right-$d, $rectF.Bottom-$d, $d, $d, 0, 90)
  $path.AddArc($rectF.X, $rectF.Bottom-$d, $d, $d, 90, 90)
  $path.CloseFigure()
  $c1 = [System.Drawing.Color]::FromArgb(99,102,241)
  $c2 = [System.Drawing.Color]::FromArgb(67,56,202)
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rectF, $c1, $c2, [single]90)
  $g.FillPath($brush, $path)

  # White speech bubble
  $wb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
  $bx = [single]($size*0.24); $by = [single]($size*0.28); $bw2 = [single]($size*0.52); $bh = [single]($size*0.34)
  $brect = New-Object System.Drawing.RectangleF($bx, $by, $bw2, $bh)
  $bd = [single]($size*0.14)
  $bpath = New-Object System.Drawing.Drawing2D.GraphicsPath
  $bpath.AddArc($brect.X, $brect.Y, $bd, $bd, 180, 90)
  $bpath.AddArc($brect.Right-$bd, $brect.Y, $bd, $bd, 270, 90)
  $bpath.AddArc($brect.Right-$bd, $brect.Bottom-$bd, $bd, $bd, 0, 90)
  $bpath.AddArc($brect.X, $brect.Bottom-$bd, $bd, $bd, 90, 90)
  $bpath.CloseFigure()
  $g.FillPath($wb, $bpath)
  # tail
  $p1 = New-Object System.Drawing.PointF(($bx+$size*0.14), ($by+$bh-1))
  $p2 = New-Object System.Drawing.PointF(($bx+$size*0.10), ($by+$bh+$size*0.10))
  $p3 = New-Object System.Drawing.PointF(($bx+$size*0.28), ($by+$bh-1))
  $g.FillPolygon($wb, @($p1,$p2,$p3))

  # Reply arrow (indigo) inside the bubble
  $pen = New-Object System.Drawing.Pen($c2, [single]($size*0.06))
  $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
  $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
  $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
  $ay = [single]($by + $bh*0.5)
  $ax0 = [single]($bx + $bw2*0.64); $ax1 = [single]($bx + $bw2*0.30)
  $g.DrawLine($pen, $ax0, $ay, $ax1, $ay)
  $g.DrawLine($pen, $ax1, $ay, [single]($ax1+$bw2*0.18), [single]($ay-$bh*0.24))
  $g.DrawLine($pen, $ax1, $ay, [single]($ax1+$bw2*0.18), [single]($ay+$bh*0.24))

  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  $pngList.Add($ms.ToArray())
}

$fs = New-Object System.IO.FileStream($Out, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i=0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $len = $pngList[$i].Length
  $wbyte = if ($s -ge 256) { 0 } else { $s }
  $bw.Write([byte]$wbyte); $bw.Write([byte]$wbyte)
  $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32)
  $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
  $offset += $len
}
for ($i=0; $i -lt $sizes.Count; $i++) { $bw.Write($pngList[$i]) }
$bw.Flush(); $fs.Close()
Write-Host "Wrote $Out ($($sizes.Count) sizes, total $offset bytes)"
