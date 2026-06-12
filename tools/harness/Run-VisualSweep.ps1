# tools/harness/Run-VisualSweep.ps1
# Drives VideoShelf through every nav state and screenshots each via GDI.
# Local dev/verification tool (needs an interactive desktop). Not run in CI.
param(
    [string]$OutDir   = (Join-Path $PSScriptRoot '..\..\tests\screenshots'),
    [string]$Fixtures = (Join-Path $env:TEMP 'vs-fixtures'),
    [int]$TimeoutSec  = 120
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$shotDir = Join-Path $OutDir $stamp
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null

# 1. Fixtures
& (Join-Path $PSScriptRoot 'Generate-Fixtures.ps1') -OutDir $Fixtures
$playClip = Join-Path $Fixtures 'Movies\Sintel (2010).mp4'

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
}
"@
[Win32]::SetProcessDPIAware() | Out-Null

function Capture-Window {
    param([System.Diagnostics.Process]$Proc, [string]$PngPath)
    $Proc.Refresh()
    $h = $Proc.MainWindowHandle
    if ($h -eq [IntPtr]::Zero) { Write-Warning "No main window handle for $PngPath"; return $false }
    [Win32]::ShowWindow($h, 9) | Out-Null          # SW_RESTORE
    [Win32]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 600
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
    'browse'        = @('--view','Browse')
    'section-detail'= @('--view','SectionDetail')
    'rename-tool'   = @('--view','RenameTool')
    'player'        = @('--view','Player','--play',$playClip)
    'pip'           = @('--view','PiP','--play',$playClip)
    'settings'      = @('--view','Settings')
}

$results = @()
foreach ($name in $views.Keys) {
    $dataDir = Join-Path $env:TEMP "vs-harness-$name"
    if (Test-Path $dataDir) { Remove-Item -Recurse -Force $dataDir }
    $signal  = Join-Path $dataDir 'ready.signal'
    New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

    $args = @('--folder',$Fixtures,'--data-dir',$dataDir,'--autostart',
              '--done-signal',$signal) + $views[$name]
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
