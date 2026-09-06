<#
Convert a source image (PNG/JPG, ideally square) into the multi-size app icon (.ico).
High-quality downscale to the standard icon sizes, packed as a PNG-based .ico.
Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\png-to-icon.ps1 -Src <image> -Out src\QuickReplyKit\Assets\app.ico
#>
param(
  [Parameter(Mandatory=$true)][string]$Src,
  [Parameter(Mandatory=$true)][string]$Out
)
Add-Type -AssemblyName System.Drawing
if (-not (Test-Path $Src)) { throw "Source not found: $Src" }

$srcImg = [System.Drawing.Image]::FromFile((Resolve-Path $Src))
$sizes = @(16,24,32,48,64,128,256)
$pngList = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap($s, $s)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.DrawImage($srcImg, (New-Object System.Drawing.Rectangle(0,0,$s,$s)))
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  $pngList.Add($ms.ToArray())
}
$srcImg.Dispose()

$fs = New-Object System.IO.FileStream($Out, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i=0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $len = $pngList[$i].Length
  $wb = if ($s -ge 256) { 0 } else { $s }
  $bw.Write([byte]$wb); $bw.Write([byte]$wb); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
  $offset += $len
}
for ($i=0; $i -lt $sizes.Count; $i++) { $bw.Write($pngList[$i]) }
$bw.Flush(); $fs.Close()
Write-Host "Wrote $Out ($($sizes.Count) sizes from $Src)"
