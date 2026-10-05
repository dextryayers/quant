#Requires -Version 5.1
param([string]$Out = "perf/fixture")
$root = Join-Path $PSScriptRoot ".."
$dir = Join-Path $root $Out
New-Item -ItemType Directory -Path $dir -Force | Out-Null
1..20 | ForEach-Object {
  $sub = Join-Path $dir ("mod_{0:00}" -f $_)
  New-Item -ItemType Directory -Path $sub -Force | Out-Null
  1..100 | ForEach-Object {
    $ext = @(".rs", ".cs", ".ts", ".py", ".md")[$_ % 5]
    $file = Join-Path $sub ("file_{0:000}{1}" -f $_, $ext)
    "line 1 for $_`nline 2 sample`nfn sample() {}" | Set-Content -LiteralPath $file
  }
}
"unicode test" | Set-Content -LiteralPath (Join-Path $dir "spasi dan # file %.md")
"long line " + ("x" * 5000) | Set-Content -LiteralPath (Join-Path $dir "longfile.txt")
Write-Host "Fixture created at $dir"
