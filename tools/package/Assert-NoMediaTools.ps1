# tools/package/Assert-NoMediaTools.ps1
# Fails (exit 1) if any external media-tool executable is present in -Path.
# libVLC (libvlc.dll / libvlccore.dll / plugins) is the allowed bundled engine.
param([Parameter(Mandatory=$true)][string]$Path)

$ErrorActionPreference = 'Stop'
$denylist = @(
    'ffmpeg.exe','ffprobe.exe','ffplay.exe',
    'HandBrakeCLI.exe','HandBrake.exe',
    'mkvmerge.exe','mkvextract.exe','mkvinfo.exe',
    'mencoder.exe','mplayer.exe','avconv.exe','x264.exe','x265.exe'
)

$found = Get-ChildItem -Recurse -File -Path $Path |
    Where-Object { $denylist -contains $_.Name }

if ($found) {
    Write-Host "FAIL: bundled media tools detected:" -ForegroundColor Red
    $found | ForEach-Object { Write-Host "  $($_.FullName)" }
    exit 1
}

Write-Host "PASS: no external media tools in $Path (libVLC is the allowed bundled engine)."
exit 0
