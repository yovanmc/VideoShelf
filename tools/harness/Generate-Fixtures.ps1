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

function New-Clip {
    param([string]$Path, [string]$Pattern)
    $dir = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    & $ffmpeg -y -loglevel error `
        -f lavfi -i "${Pattern}=size=1280x720:rate=24" `
        -f lavfi -i "sine=frequency=440:duration=6" `
        -t 6 `
        -c:v libx264 -pix_fmt yuv420p -preset ultrafast `
        -c:a aac -movflags +faststart `
        "$Path"
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed for $Path" }
}

# Section 1: Shows -> a 3-episode series (exercises grouping, section-detail, rename, episodes)
New-Clip -Path (Join-Path $OutDir 'Shows\Big Buck Bunny\Big Buck Bunny S01E01.mp4') -Pattern 'testsrc2'
New-Clip -Path (Join-Path $OutDir 'Shows\Big Buck Bunny\Big Buck Bunny S01E02.mp4') -Pattern 'smptebars'
New-Clip -Path (Join-Path $OutDir 'Shows\Big Buck Bunny\Big Buck Bunny S01E03.mp4') -Pattern 'mandelbrot'

# Section 2: Movies -> two standalones (exercises standalone cards, For-you/Recently-added rails)
New-Clip -Path (Join-Path $OutDir 'Movies\Sintel (2010).mp4')         -Pattern 'testsrc2'
New-Clip -Path (Join-Path $OutDir 'Movies\Tears of Steel (2012).mp4') -Pattern 'mandelbrot'

Write-Host "Fixtures written to $OutDir"
Get-ChildItem -Recurse -File $OutDir | ForEach-Object { Write-Host ("  {0} ({1:N0} bytes)" -f $_.FullName, $_.Length) }
