#Requires -Version 5.1
<#
  Quant IDE uninstaller. Removes install dir, shortcuts, registry entry.
  Settings in %AppData%\Quant are kept. Delete them manually to wipe all.
#>
$ErrorActionPreference = "SilentlyContinue"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$defaultDir = Join-Path $env:LocalAppData "QuantIDE\V0.1.0"
$dir = if ((Test-Path -LiteralPath (Join-Path $here "QuantIDE.exe"))) { $here } else { $defaultDir }

Get-Process -Name "QuantIDE", "Quant.Desktop", "quant-engine" -ErrorAction SilentlyContinue | Stop-Process -Force

Remove-Item -LiteralPath $dir -Recurse -Force
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath("Programs")) "Quant IDE") -Recurse -Force
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath("Desktop")) "Quant IDE.lnk") -Force
Remove-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\QuantIDE" -Recurse -Force

Write-Host "Quant IDE uninstalled. %AppData%\Quant settings kept."
