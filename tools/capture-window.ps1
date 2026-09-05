<#
Capture a window (by title substring or process name) to a PNG so the WPF UI can be
visually validated. Falls back to full-screen capture if no match is found.

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\capture-window.ps1 -TitleContains "QuickReplyKit" -Out out.png
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\capture-window.ps1 -ProcessName QuickReplyKit -Out out.png
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\capture-window.ps1 -FullScreen -Out out.png
#>
param(
  [string]$TitleContains,
  [string]$ProcessName,
  [switch]$FullScreen,
  [Parameter(Mandatory=$true)][string]$Out
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinCap {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int n);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT r, int size);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

function Get-TargetHandle {
  $procs = @()
  if ($ProcessName) { $procs = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue }
  else { $procs = Get-Process | Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle } }
  if ($TitleContains) { $procs = $procs | Where-Object { $_.MainWindowTitle -like "*$TitleContains*" } }
  $p = $procs | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  if ($p) { return $p.MainWindowHandle } else { return [IntPtr]::Zero }
}

$bounds = $null
if (-not $FullScreen) {
  $h = Get-TargetHandle
  if ($h -ne [IntPtr]::Zero) {
    # WPF windows are DirectX-composited, so PrintWindow renders them black. Instead force the
    # window genuinely on top (SetWindowPos TOPMOST bypasses the foreground-lock that blocks
    # SetForegroundWindow), then read the pixels off the screen.
    $HWND_TOPMOST = [IntPtr](-1)
    $HWND_NOTOPMOST = [IntPtr](-2)
    $SWP = 0x0040   # SWP_SHOWWINDOW ; keep move/size so it stays where it is
    [WinCap]::ShowWindow($h, 5) | Out-Null   # SW_SHOW
    [WinCap]::SetWindowPos($h, $HWND_TOPMOST, 0, 0, 0, 0, (0x0001 -bor 0x0002 -bor $SWP)) | Out-Null
    [WinCap]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 400
    $r = New-Object WinCap+RECT
    # Prefer DWM extended frame bounds (excludes drop shadow); fall back to GetWindowRect
    $ok = [WinCap]::DwmGetWindowAttribute($h, 9, [ref]$r, [System.Runtime.InteropServices.Marshal]::SizeOf($r))
    if ($ok -ne 0) { [WinCap]::GetWindowRect($h, [ref]$r) | Out-Null }
    $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
    if ($w -gt 0 -and $ht -gt 0) {
      $bounds = New-Object System.Drawing.Rectangle($r.Left, $r.Top, $w, $ht)
    }
    # Leave it topmost only long enough to capture; restore normal z-order afterwards.
    [WinCap]::SetWindowPos($h, $HWND_NOTOPMOST, 0, 0, 0, 0, (0x0001 -bor 0x0002 -bor 0x0040)) | Out-Null
  }
}
if (-not $bounds) {
  Write-Host "No matching window; capturing full virtual screen."
  $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
}

$bmp = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "Saved $Out ($($bounds.Width)x$($bounds.Height))"
