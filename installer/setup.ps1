#Requires -Version 5.1
<#
  Quant IDE V0.1.0 installer for Windows (no admin required).
  Installs to %LocalAppData%\QuantIDE\V0.1.0, creates Start Menu
  and Desktop shortcuts, registers Add/Remove Programs entry.

  Usage:
    powershell -ExecutionPolicy Bypass -File installer\setup.ps1
    powershell -ExecutionPolicy Bypass -File installer\setup.ps1 -Launch
    powershell -ExecutionPolicy Bypass -File installer\setup.ps1 -Source "D:\path\to\QuantIDE-V0.1.0-win-x64" -Desktop:$false
#>
param(
  [string]$Source = "",
  [string]$InstallDir = (Join-Path $env:LocalAppData "QuantIDE\V0.1.0"),
  [bool]$Desktop = $true,
  [switch]$Launch
)

$ErrorActionPreference = "Stop"
$version = "0.1.0"

function Find-Source {
  param([string]$Hint)
  if ($Hint -ne "" -and (Test-Path -LiteralPath (Join-Path $Hint "QuantIDE.exe"))) { return $Hint }
  $candidates = @(
    (Join-Path $PSScriptRoot "..\dist\QuantIDE-V0.1.0-win-x64"),
    $PSScriptRoot,
    (Get-Location).Path
  )
  foreach ($c in $candidates) {
    if ($c -ne "" -and (Test-Path -LiteralPath (Join-Path $c "QuantIDE.exe"))) { return $c }
  }
  throw "QuantIDE.exe not found. Pass -Source <folder with QuantIDE.exe>."
}

$src = Find-Source $Source
Write-Host "Source : $src"
Write-Host "Target : $InstallDir"

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item (Join-Path $src "QuantIDE.exe") -Destination (Join-Path $InstallDir "QuantIDE.exe") -Force
Copy-Item (Join-Path $src "quant-engine.exe") -Destination (Join-Path $InstallDir "quant-engine.exe") -Force
if (Test-Path -LiteralPath (Join-Path $src "quant.json.example")) {
  Copy-Item (Join-Path $src "quant.json.example") -Destination (Join-Path $InstallDir "quant.json.example") -Force
}
if (Test-Path -LiteralPath (Join-Path $src "README.txt")) {
  Copy-Item (Join-Path $src "README.txt") -Destination (Join-Path $InstallDir "README.txt") -Force
}
New-Item -ItemType Directory -Path (Join-Path $InstallDir "models") -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot "uninstall.ps1") -Destination (Join-Path $InstallDir "uninstall.ps1") -Force -ErrorAction SilentlyContinue

$ws = New-Object -ComObject WScript.Shell
$exe = Join-Path $InstallDir "QuantIDE.exe"

$startDir = Join-Path ([Environment]::GetFolderPath("Programs")) "Quant IDE"
New-Item -ItemType Directory -Path $startDir -Force | Out-Null
$sc = $ws.CreateShortcut((Join-Path $startDir "Quant IDE.lnk"))
$sc.TargetPath = $exe
$sc.WorkingDirectory = $InstallDir
$sc.Description = "Quant IDE V$version - local first AI IDE"
$sc.Save()

if ($Desktop) {
  $desk = [Environment]::GetFolderPath("Desktop")
  $sc2 = $ws.CreateShortcut((Join-Path $desk "Quant IDE.lnk"))
  $sc2.TargetPath = $exe
  $sc2.WorkingDirectory = $InstallDir
  $sc2.Description = "Quant IDE V$version"
  $sc2.Save()
}

$unkey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\QuantIDE"
New-Item -Path $unkey -Force | Out-Null
Set-ItemProperty -Path $unkey -Name "DisplayName" -Value "Quant IDE"
Set-ItemProperty -Path $unkey -Name "DisplayVersion" -Value $version
Set-ItemProperty -Path $unkey -Name "Publisher" -Value "Quant"
Set-ItemProperty -Path $unkey -Name "InstallLocation" -Value $InstallDir
Set-ItemProperty -Path $unkey -Name "UninstallString" -Value "powershell -ExecutionPolicy Bypass -File `"$InstallDir\uninstall.ps1`""
Set-ItemProperty -Path $unkey -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $unkey -Name "NoRepair" -Value 1 -Type DWord

Write-Host ""
Write-Host "Installed Quant IDE V$version to $InstallDir"
Write-Host "Start Menu: Quant IDE. Add/Remove Programs entry registered."
Write-Host "Note: unsigned build. SmartScreen may ask once; choose Run anyway."

if ($Launch) {
  Start-Process -FilePath $exe -WorkingDirectory $InstallDir
}
