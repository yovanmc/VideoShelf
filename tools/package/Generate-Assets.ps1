# tools/package/Generate-Assets.ps1
# Generates placeholder MSIX logo assets + app.ico. Run once; output is committed.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$dir = Join-Path $PSScriptRoot '..\..\packaging\Assets'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$bg = [System.Drawing.Color]::FromArgb(255, 32, 33, 36)     # WPF-UI dark surface
$fg = [System.Drawing.Color]::FromArgb(255, 120, 170, 255)  # accent

function New-Logo {
    param([int]$W, [int]$H, [string]$File)
    $bmp = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear($bg)
    $fontSize = [Math]::Max(8, [int]($H * 0.5))
    $font = New-Object System.Drawing.Font 'Segoe UI', $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object System.Drawing.SolidBrush $fg
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $g.DrawString('VS', $font, $brush, (New-Object System.Drawing.RectangleF 0, 0, $W, $H), $fmt)
    $bmp.Save((Join-Path $dir $File), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

New-Logo -W 44  -H 44  -File 'Square44x44Logo.png'
New-Logo -W 150 -H 150 -File 'Square150x150Logo.png'
New-Logo -W 310 -H 150 -File 'Wide310x150Logo.png'
New-Logo -W 50  -H 50  -File 'StoreLogo.png'

# app.ico (256-px square)
$ico = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($ico)
$g.SmoothingMode = 'AntiAlias'; $g.Clear($bg)
$font = New-Object System.Drawing.Font 'Segoe UI', 128, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$brush = New-Object System.Drawing.SolidBrush $fg
$fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment='Center'; $fmt.LineAlignment='Center'
$g.DrawString('VS', $font, $brush, (New-Object System.Drawing.RectangleF 0,0,256,256), $fmt)
$hicon = $ico.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hicon)
$fs = [System.IO.File]::Create((Join-Path $dir 'app.ico'))
$icon.Save($fs); $fs.Close()
$g.Dispose(); $ico.Dispose()

Write-Host "Assets written to $dir"
Get-ChildItem $dir | ForEach-Object { Write-Host "  $($_.Name)" }
