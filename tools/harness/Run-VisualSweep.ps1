# tools/harness/Run-VisualSweep.ps1
# Drives VideoShelf through every nav state and screenshots each via GDI.
# Local dev/verification tool (needs an interactive desktop). Not run in CI.
param(
    [string]$OutDir   = (Join-Path $PSScriptRoot '..\..\tests\screenshots'),
    [string]$Fixtures = (Join-Path $env:TEMP 'vs-fixtures'),
    [int]$TimeoutSec  = 120,
    [int]$SettleSeconds = 5    # post-foreground wait so the WPF-UI Mica/Fluent surface composes (capturing too early yields an all-black frame)
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$shotDir = Join-Path $OutDir $stamp
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null

# 1. Fixtures
& (Join-Path $PSScriptRoot 'Generate-Fixtures.ps1') -OutDir $Fixtures
$playClip = Join-Path $Fixtures 'Movies\Sintel (2010).mp4'

# Seed a matching .srt next to EVERY .mp4 fixture so whichever clip the player
# view opens (currently the first episode of the richest series, i.e.
# "Shows\Big Buck Bunny 1.mp4", determined by HarnessRunner.PlayAsync ->
# FindRichestSeriesAsync) always has a sidecar present to auto-load.
# Seeding is idempotent: skips any .srt that already exists.
$srtContent = @"
1
00:00:00,500 --> 00:00:04,000
VideoShelf sidecar subtitle test.
"@
Get-ChildItem -Path $Fixtures -Recurse -Filter '*.mp4' | ForEach-Object {
    $srt = [System.IO.Path]::ChangeExtension($_.FullName, '.srt')
    if (-not (Test-Path $srt)) {
        $srtContent | Set-Content -Path $srt -Encoding UTF8
    }
}

# 2. Build Debug app
Write-Host "Building VideoShelf.App (Debug)..."
dotnet build (Join-Path $repo 'src\VideoShelf.App\VideoShelf.App.csproj') -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$exe = Join-Path $repo 'src\VideoShelf.App\bin\Debug\net10.0-windows\VideoShelf.App.exe'
if (-not (Test-Path $exe)) { throw "App exe not found at $exe" }

# 3. GDI capture helper
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
"@
[Win32]::SetProcessDPIAware() | Out-Null

function Capture-Window {
    param([System.Diagnostics.Process]$Proc, [string]$PngPath)
    # Wait for a real, visible main-window handle (not just a non-zero handle).
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        $Proc.Refresh()
        $h = $Proc.MainWindowHandle
        if ($h -ne [IntPtr]::Zero -and [Win32]::IsWindowVisible($h)) { break }
        Start-Sleep -Milliseconds 300
    }
    $h = $Proc.MainWindowHandle
    if ($h -eq [IntPtr]::Zero) { Write-Warning "No main window handle for $PngPath"; return $false }
    [Win32]::ShowWindow($h, 9) | Out-Null          # SW_RESTORE
    # Bring the window to the very top of the Z-order, then immediately drop it back out
    # of the always-on-top band. SetForegroundWindow alone is unreliable from a background
    # script (focus-stealing prevention) and lets another app occlude the capture; a
    # *permanent* TOPMOST, however, breaks LibVLCSharp.WPF's separate foreground/overlay
    # window (it whites out the player). The TOPMOST -> NOTOPMOST toggle gives a reliable
    # bring-to-front for every view without leaving the window topmost.
    $HWND_TOPMOST = New-Object IntPtr(-1)
    $HWND_NOTOPMOST = New-Object IntPtr(-2)
    $SWP_NOMOVE_NOSIZE = 0x0001 -bor 0x0002        # SWP_NOSIZE | SWP_NOMOVE
    [Win32]::SetWindowPos($h, $HWND_TOPMOST,   0, 0, 0, 0, $SWP_NOMOVE_NOSIZE) | Out-Null
    [Win32]::SetWindowPos($h, $HWND_NOTOPMOST, 0, 0, 0, 0, $SWP_NOMOVE_NOSIZE) | Out-Null
    [Win32]::SetForegroundWindow($h) | Out-Null
    # Let the WPF-UI Mica/Fluent backdrop compose — CopyFromScreen before DWM
    # composition completes captures an all-black frame (the proven settle from
    # the VideoTriage capture harness).
    Start-Sleep -Seconds $SettleSeconds
    $r = New-Object Win32+RECT
    [Win32]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
    if ($w -le 0 -or $hh -le 0) { Write-Warning "Bad rect for $PngPath"; return $false }
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $hh))
    $bmp.Save($PngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    return $true
}

# 4. Per-view launch + capture
$views = [ordered]@{
    'home'          = @('--view','Home','--seed-demo')
    'search'        = @('--view','Search','--seed-demo')
    'browse'        = @('--view','Browse')
    'section-detail'= @('--view','SectionDetail','--seed-demo')
    'smart-views'   = @('--view','SmartViews','--seed-demo')
    'playlists'     = @('--view','Playlists','--seed-demo')
    'watchlist'     = @('--view','Watchlist','--seed-demo')
    'favorites'     = @('--view','Favorites','--seed-demo')
    'history'       = @('--view','History','--seed-demo')
    'rename-tool'   = @('--view','RenameTool')
    'player'        = @('--view','Player','--play',$playClip)
    'pip'           = @('--view','PiP','--play',$playClip)
    'settings'      = @('--view','Settings')
    'empty'         = @('--view','Home','--no-folder')   # first-run empty-library CTA (no source configured)
    'queue'         = @('--view','Queue','--seed-demo')
    'player-queue'  = @('--view','PlayerQueue','--seed-demo')
    # M17 (Power & scale) surfaces:
    'browse-scale'    = @('--view','Browse','--seed-demo')          # 30+ creators -> A-Z jump-list + virtualized grid + density/list toggles
    'browse-selection'= @('--view','BrowseSelection','--seed-demo') # selection mode + bulk-action bar ("N selected")
    'browse-filter'   = @('--view','BrowseFilter','--seed-demo')    # in-page filter bar open + Compact density + List mode
    'command-palette' = @('--view','CommandPalette','--seed-demo')  # Ctrl+K palette open with a query
    'multi-rename'    = @('--view','MultiRename','--seed-demo')     # cross-series template rename preview
    # M18 (Library health) surfaces:
    'maintenance'         = @('--view','Maintenance','--seed-demo')       # dashboard tiles + per-source cards + scan-diff banner
    'duplicate-resolve'   = @('--view','DuplicateResolve','--seed-demo')  # compare screen with 2 candidates (size/duration/resolution + Keep)
    'section-edit-mode'   = @('--view','SectionEditMode','--seed-demo')    # creator page in Edit mode — shows split/merge/reorder affordances (M18-H)
    # M19 (Player depth) surfaces — each launches the player into a specific sub-state:
    'player-more'         = @('--view','PlayerMore','--seed-demo')        # ⋯ More flyout open: screenshot/set-cover, speed row, aspect row, A-B row
    'player-tracks'       = @('--view','PlayerTracks','--seed-demo')      # Tracks flyout open: audio list, subtitle list, + Sub, normalize toggle
    'player-volume'       = @('--view','PlayerVolume','--seed-demo')      # Volume flyout open: slider + mute button
    'player-speed'        = @('--view','PlayerSpeed','--seed-demo')       # Speed set to 1.5× — RateLabel shows "1.5×" in More flyout
    'player-aspect'       = @('--view','PlayerAspect','--seed-demo')      # Aspect cycled to 16:9 — SelectedAspect.Label shows "16:9"
    'player-ab-repeat'    = @('--view','PlayerAbRepeat','--seed-demo')    # A-B repeat active — on-bar chip lit, A+B positions set
    'player-skip-feedback'= @('--view','PlayerSkipFeedback','--seed-demo')# Skip-feedback badge visible — shows "−10s" badge
    'player-up-next'      = @('--view','PlayerUpNext','--seed-demo')      # Up-Next countdown card visible — title + 10-second countdown
    # M21 (Delight & motion) surfaces:
    'toast'               = @('--view','Toast','--seed-demo')              # toast overlay in bottom-right corner — confirms toast renders over Home
    'favorites-loading'   = @('--view','FavoritesLoading','--seed-demo')  # Favorites page with IsLoading=true — skeleton placeholder visible
}

$results = @()
foreach ($name in $views.Keys) {
    $dataDir = Join-Path $env:TEMP "vs-harness-$name"
    if (Test-Path $dataDir) { Remove-Item -Recurse -Force $dataDir }
    $signal  = Join-Path $dataDir 'ready.signal'
    New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

    # '--no-folder' is a harness-only token (stripped before launch): omit '--folder' so the
    # app starts with no source configured, exercising the first-run empty-library overlay.
    $viewArgs = $views[$name]
    if ($viewArgs -contains '--no-folder') {
        $viewArgs = @($viewArgs | Where-Object { $_ -ne '--no-folder' })
        $args = @('--data-dir',$dataDir,'--autostart','--done-signal',$signal) + $viewArgs
    } else {
        $args = @('--folder',$Fixtures,'--data-dir',$dataDir,'--autostart',
                  '--done-signal',$signal) + $viewArgs
    }

    Write-Host "Launching '$name'..."
    $proc = Start-Process -FilePath $exe -ArgumentList $args -PassThru

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while (-not (Test-Path $signal) -and -not $proc.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path $signal)) {
        Write-Warning "'$name' never signalled (exited=$($proc.HasExited))."
    } else {
        $msg = (Get-Content $signal -Raw).Trim()
        if ($msg -like 'ERROR*') { Write-Warning "'$name' signalled: $msg" }
        Start-Sleep -Milliseconds 800   # let the foregrounded window paint
        $png = Join-Path $shotDir "$name.png"
        if (Capture-Window -Proc $proc -PngPath $png) { $results += $png }
    }
    if (-not $proc.HasExited) { $proc.Kill() | Out-Null; $proc.WaitForExit(5000) | Out-Null }
}

Write-Host "`n=== Screenshots written to $shotDir ==="
$results | ForEach-Object { Write-Host "  $_" }
Write-Host "`nPNG_DIR=$shotDir"
