# tools/harness/Generate-Fixtures.ps1
# Generates a tiny VideoShelf fixture library using ffmpeg (dev-only tool).
# Never shipped with the app. Usage: .\Generate-Fixtures.ps1 -OutDir <path>
param(
    [Parameter(Mandatory = $true)][string]$OutDir,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue)?.Source
if (-not $ffmpeg) {
    throw "ffmpeg not found on PATH. Install it (dev-only) to generate fixtures. The app itself never uses ffmpeg."
}

if (Test-Path $OutDir) {
    if ($Force) { Remove-Item -Recurse -Force $OutDir }
    else { Write-Host "Fixtures already present at $OutDir (use -Force to regenerate)."; return }
}

# 30-second clips: long enough that the harness's Player/PiP shots capture a live
# decoded frame (the sweep launch+settle takes several seconds before capture).
function New-Clip {
    param([string]$Path, [string]$Pattern)
    $dir = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    & $ffmpeg -y -loglevel error `
        -f lavfi -i "${Pattern}=size=1280x720:rate=24" `
        -f lavfi -i "sine=frequency=440:duration=30" `
        -t 30 `
        -c:v libx264 -pix_fmt yuv420p -preset ultrafast `
        -c:a aac -movflags +faststart `
        "$Path"
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed for $Path" }
}

# Section 1: Shows -> a 3-episode series. Episodes sit DIRECTLY under the section
# folder (the scanner is flat: one level deep per section; series are grouped from
# filenames by TitleParser/SectionGrouper, NOT from a nested per-series subfolder).
# TitleParser groups on the first INTEGER token: "Big Buck Bunny 1" => (title="Big
# Buck Bunny", episode=1). Single-digit episode numbers group into one series AND
# differ from the zero-padded canonical form ("Big Buck Bunny 01"), so the rename
# tool shows a meaningful "Ready" preview (1 -> 01, 2 -> 02, 3 -> 03).
New-Clip -Path (Join-Path $OutDir 'Shows\Big Buck Bunny 1.mp4') -Pattern 'testsrc2'
New-Clip -Path (Join-Path $OutDir 'Shows\Big Buck Bunny 2.mp4') -Pattern 'smptebars'
New-Clip -Path (Join-Path $OutDir 'Shows\Big Buck Bunny 3.mp4') -Pattern 'mandelbrot'

# Section 2: Movies -> two standalones (exercises standalone cards, For-you/Recently-added rails)
New-Clip -Path (Join-Path $OutDir 'Movies\Sintel (2010).mp4')         -Pattern 'testsrc2'
New-Clip -Path (Join-Path $OutDir 'Movies\Tears of Steel (2012).mp4') -Pattern 'mandelbrot'

Write-Host "Fixtures written to $OutDir"
Get-ChildItem -Recurse -File $OutDir | ForEach-Object { Write-Host ("  {0} ({1:N0} bytes)" -f $_.FullName, $_.Length) }
