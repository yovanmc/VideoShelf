# Run-ScaleBench.ps1 — launches the app against the stress fixture, asserts render-scale gates.
param(
  [string]$Spec = "500x200x5000",
  [int]$MaxBrowseNodes = 80,        # virtualization must keep realized creator containers bounded
  [int]$MaxSeriesNodes = 60,        # realized series containers on the biggest creator page
  [int]$MaxInitialRenderMs = 1500
)
$ErrorActionPreference = "Stop"
$exe = Resolve-Path "$PSScriptRoot\..\..\src\VideoShelf.App\bin\Release\net10.0-windows\VideoShelf.App.exe"
$scratch = Join-Path $env:TEMP "vs-scalebench"
Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $scratch | Out-Null

function Run-One($view, $maxNodes) {
  $metrics = Join-Path $scratch "$view.json"
  $done = Join-Path $scratch "$view.done"
  $dataDir = Join-Path $scratch "data"
  & $exe --stress $Spec --view $view --metrics-out $metrics --done-signal $done --data-dir $dataDir | Out-Null
  if (-not (Test-Path $metrics)) { throw "$view: no metrics written" }
  $m = (Get-Content $metrics -Raw | ConvertFrom-Json)[0]
  Write-Host "$view  nodes=$($m.RenderedNodeCount)  renderMs=$($m.InitialRenderMs)  heapMB=$([math]::Round($m.ManagedHeapBytes/1MB))"
  if ($m.RenderedNodeCount -gt $maxNodes)      { throw "$view: realized nodes $($m.RenderedNodeCount) > $maxNodes (virtualization regressed)" }
  if ($m.InitialRenderMs   -gt $MaxInitialRenderMs) { throw "$view: initial render $($m.InitialRenderMs)ms > $MaxInitialRenderMs" }
}

# SectionDetail opens the biggest creator (creator 0, which has biggestSeries series per the spec).
# The stress seeder places creator 0 as the biggest creator; NavigateAsync -> SectionDetail
# uses FindRichestSeriesAsync which walks all sections and picks the one with most episodes.
Run-One "Browse"        $MaxBrowseNodes
Run-One "SectionDetail" $MaxSeriesNodes
Write-Host "SCALE BENCH PASS" -ForegroundColor Green
