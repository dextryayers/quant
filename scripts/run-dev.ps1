#Requires -Version 5.1
param(
  [int]$Port = 3737
)

$root = Split-Path -Parent $PSScriptRoot
$engineDir = Join-Path $root "engine/quant-engine"
$guiProj = Join-Path $root "gui/Quant.Desktop/Quant.Desktop.csproj"

Write-Host "Starting quant-engine on port $Port..."
$engine = Start-Process -FilePath "cargo" -ArgumentList "run -p quant-engine -- --port $Port" -WorkingDirectory $engineDir -PassThru

try {
  $tries = 0
  while ($tries -lt 30) {
    try {
      $h = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 2
      Write-Host ("Engine OK version " + $h.version)
      break
    } catch {
      Start-Sleep -Seconds 1
      $tries++
    }
  }
  Write-Host "Starting GUI..."
  dotnet run --project $guiProj -- --engine-port $Port
} finally {
  if ($engine -and !$engine.HasExited) {
    Stop-Process -Id $engine.Id -Force -ErrorAction SilentlyContinue
  }
}
