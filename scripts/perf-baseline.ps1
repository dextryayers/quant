#Requires -Version 5.1
$root = Split-Path -Parent $PSScriptRoot
$sw = [System.Diagnostics.Stopwatch]::StartNew()
dotnet build (Join-Path $root "Quant.slnx") -v quiet | Out-Null
$buildMs = $sw.ElapsedMilliseconds
$gui = Get-ChildItem (Join-Path $root "gui/Quant.Desktop/bin/Debug") -Recurse -ErrorAction SilentlyContinue | Measure-Object Length -Sum
$out = @{
  time = (Get-Date).ToString("o")
  dotnet_build_ms = $buildMs
  gui_bytes = $gui.Sum
  target_startup_ms = 1500
  target_ram_idle_mb = 350
} | ConvertTo-Json -Depth 4
New-Item -ItemType Directory -Path (Join-Path $root "perf") -Force | Out-Null
$out | Set-Content (Join-Path $root "perf/baseline.json")
Write-Host $out
