# tools/package/Build-Msix.ps1
# Builds a signed VideoShelf MSIX locally. Requires Windows SDK (makeappx/signtool).
param(
    [string]$Configuration = 'Release',
    [string]$Rid = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$pkg  = Join-Path $repo 'packaging'
$publish = Join-Path $pkg '_publish'
$staging = Join-Path $pkg '_staging'
$msix    = Join-Path $repo 'VideoShelf.msix'

foreach ($p in @($publish, $staging)) { if (Test-Path $p) { Remove-Item -Recurse -Force $p } }

# 1. Self-contained publish
Write-Host "Publishing self-contained ($Rid)..."
dotnet publish (Join-Path $repo 'src\VideoShelf.App\VideoShelf.App.csproj') `
    -c $Configuration -r $Rid --self-contained true `
    -p:PublishSingleFile=false -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# 2. Assert no external media tools shipped (libVLC allowed)
& (Join-Path $PSScriptRoot 'Assert-NoMediaTools.ps1') -Path $publish
if ($LASTEXITCODE -ne 0) { throw "media-tool assertion failed" }

# 3. Stage = publish output + manifest + assets
New-Item -ItemType Directory -Force -Path $staging | Out-Null
Copy-Item -Recurse -Force (Join-Path $publish '*') $staging
Copy-Item -Force (Join-Path $pkg 'AppxManifest.xml') $staging
Copy-Item -Recurse -Force (Join-Path $pkg 'Assets') (Join-Path $staging 'Assets')

# 4. Resolve Windows SDK tools
$sdkBin = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory |
    Where-Object { $_.Name -match '^10\.' } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $sdkBin) { throw "Windows SDK bin not found." }
$makeappx = Join-Path $sdkBin.FullName 'x64\makeappx.exe'
$signtool = Join-Path $sdkBin.FullName 'x64\signtool.exe'

# 5. Pack
Write-Host "Packing MSIX..."
& $makeappx pack /o /d $staging /p $msix
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

# 6. Sign with an ephemeral self-signed cert (subject must match manifest Publisher)
$cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=VideoShelf' `
    -KeyUsage DigitalSignature -FriendlyName 'VideoShelf Dev' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
$pfx = Join-Path $pkg 'videoshelf-dev.pfx'
$pwd = ConvertTo-SecureString -String 'videoshelf' -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pwd | Out-Null
& $signtool sign /fd SHA256 /a /f $pfx /p 'videoshelf' $msix
if ($LASTEXITCODE -ne 0) { throw "signtool failed" }

Write-Host "MSIX built + signed: $msix"
