<#
Launch the built app, wait, screenshot its window, then kill it. Reusable UI-validation helper.
Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\shoot.ps1 -Out shot.png [-Args "--show"] [-Wait 3] [-Exe path]
#>
param(
  [Parameter(Mandatory=$true)][string]$Out,
  [string]$Exe = "src\QuickReplyKit\bin\Debug\net10.0-windows\QuickReplyKit.exe",
  [string]$AppArgs = "--show",
  [int]$Wait = 3
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $root $Exe
if (-not (Test-Path $exePath)) { throw "Exe not found: $exePath" }

# clear any prior instance (single-instance mutex would otherwise make us just signal + exit)
Get-Process QuickReplyKit -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

$argList = @()
if ($AppArgs) { $argList = $AppArgs.Split(' ') }
$p = Start-Process -FilePath $exePath -ArgumentList $argList -PassThru
Start-Sleep -Seconds $Wait

& (Join-Path $PSScriptRoot 'capture-window.ps1') -TitleContains 'QuickReplyKit' -Out $Out
Start-Sleep -Milliseconds 200

Get-Process QuickReplyKit -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host "Captured $Out"
