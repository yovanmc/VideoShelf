# Generate-StressClips.ps1 — dev-only; needs ffmpeg on the dev machine PATH (the APP never uses ffmpeg).
# Generates small synthetic MP4 clips for benchmarking the scan probe at degree=1 vs degree=3.
# Usage:
#   pwsh -File tools/harness/Generate-StressClips.ps1 [-Out <dir>] [-Creators <n>] [-ClipsPerCreator <n>]
param(
    [string]$Out = "$env:TEMP\vs-stress-clips",
    [int]$Creators = 20,
    [int]$ClipsPerCreator = 15
)
$ErrorActionPreference = "Stop"
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) { throw "ffmpeg not found (dev tool only)" }
New-Item -ItemType Directory -Force $Out | Out-Null
for ($c=0; $c -lt $Creators; $c++) {
  $dir = Join-Path $Out ("Creator{0:D3}" -f $c); New-Item -ItemType Directory -Force $dir | Out-Null
  for ($i=1; $i -le $ClipsPerCreator; $i++) {
    $f = Join-Path $dir ("Show {0:D3}.mp4" -f $i)
    if (-not (Test-Path $f)) {
      ffmpeg -y -f lavfi -i "testsrc=duration=2:size=320x240:rate=10" -pix_fmt yuv420p $f 2>$null | Out-Null
    }
  }
}
Write-Host "Generated $($Creators*$ClipsPerCreator) clips under $Out"
