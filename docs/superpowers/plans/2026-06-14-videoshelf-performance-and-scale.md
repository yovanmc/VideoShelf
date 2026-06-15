# M22 — Performance & Scale Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Written for Sonnet execution.** This plan touches existing files this author did NOT read line-by-line (only digested signatures). For every task that EDITS an existing file: **read the current file first.** If its actual shape does not match what this plan describes, **STOP and report** rather than guessing — do not force a change onto code that has drifted. New files include complete code; edit sites give the exact target shape + the fragment to add.

**Goal:** Make VideoShelf hold up at large-library scale (~500 creators / ~200 series in the biggest creator / ~5,000 videos) — virtualize the creator-page series grid (the deferred M17 "E2" bottleneck), bound thumbnail memory, parallelize the scan probe, tune the hot DB read, and ship a stress-fixture + metrics harness so all of this is measurable and gated.

**Architecture:** Six stacked PRs split at GROUP seams (the M16–M21 model), **Group A first** because it is the verification backbone — nothing else is falsifiable without it. App-layer + a few Core read-path/index changes; **no `user_version` runner** (additive `CREATE INDEX IF NOT EXISTS` only — the M8→M21 no-runner streak holds). Render-scale (B, C, E) is verified by a deterministic **DB-seed** stress fixture; scan-throughput (D) is verified by a smaller **real-clip** fixture timed sequential-vs-parallel.

**Tech Stack:** .NET 10 · WPF + WPF-UI · `VirtualizingWrapPanel 2.5.1` (WpfToolkit, already a dependency) · LibVLCSharp · `Microsoft.Data.Sqlite` · xUnit. `gh` is **not on PATH** → `& "C:\Program Files\GitHub CLI\gh.exe"`. Solution: `VideoShelf.slnx`.

---

## Conventions (read once, apply to every task)

- **Test gate (run after every group, and before every PR):**
  `dotnet test VideoShelf.slnx -c Release --nologo -v q`
  Expected tail: `Passed!  - Failed: 0` with the running total climbing from the **939** baseline.
- **Build quietly:** `dotnet build VideoShelf.slnx -c Release -v minimal`.
- **Worktrees:** work in `.worktrees/m22-<group>`; **`gh pr merge` from the main repo root**, never the worktree. Direct pushes to `main` are blocked — every change ships via branch + PR.
- **Commits:** author `yovanmc`, trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. **No Codex trailer.** Merge `--merge` (no squash). This ROADMAP flip rides Group F's branch.
- **CI:** after pushing each group's branch + opening its PR, `& "C:\Program Files\GitHub CLI\gh.exe" pr checks <PR#> --watch` in the **foreground** (sleep ~20s first to dodge "no checks reported"); merge only when green.
- **Theming rule (load-bearing):** additive only — **never** retemplate a `ui:*` WPF-UI control, never edit the `DesignTokens` palette. No `AutomationProperties`/screen-reader anything (owner removed it in PR #77; it stays removed). [[wpfui-theming-and-visual-verification]]
- **Verification reality:** over-video / libVLC `VideoView` HwndHost content is GDI-uncapturable (verify-by-proxy: unit tests + smoke). The render-scale gates use the **metrics JSON** (node counts + timings), not pixel diffs.

---

## File Structure (decomposition map)

**New files**
- `src/VideoShelf.App/Scale/VisualNodeCounter.cs` — pure visual-tree node counter (testable shape via an injectable tree-walk seam).
- `src/VideoShelf.App/Scale/ScaleMetrics.cs` — metrics record + JSON serializer (System.Text.Json).
- `src/VideoShelf.App/Scale/StressLibrarySpec.cs` — pure, deterministic spec generator (creators/series/episodes counts → in-memory plan).
- `src/VideoShelf.App/Scale/StressLibrarySeeder.cs` — writes a `StressLibrarySpec` straight into the DB (render-scale fixture; no files on disk).
- `src/VideoShelf.App/Services/PooledBitmapLoader.cs` — `IImageLoader` impl: decode-at-size + bounded frozen-`BitmapImage` LRU.
- `src/VideoShelf.App/Services/IImageLoader.cs` — loader interface.
- `src/VideoShelf.Core/Scanning/ProbeScheduler.cs` — pure bounded-concurrency batching helper (degree, cancellation) — testable without libVLC.
- `tools/harness/Generate-StressClips.ps1` — dev-only ffmpeg generator for the real-clip scan fixture (D).
- `tools/harness/Run-ScaleBench.ps1` — launches the app against the stress fixture, collects `--metrics-out`, asserts gates.
- `tests/VideoShelf.App.Tests/Scale/StressLibrarySpecTests.cs`, `ScaleMetricsTests.cs`, `VisualNodeCounterTests.cs`, `PooledBitmapLoaderTests.cs`
- `tests/VideoShelf.Core.Tests/Scanning/ProbeSchedulerTests.cs`
- `tests/VideoShelf.Core.Tests/Storage/SectionSummaryQueryPlanTests.cs`

**Modified files**
- `src/VideoShelf.App/Harness/HarnessOptions.cs` — add `--stress <spec>`, `--metrics-out <path>`.
- `src/VideoShelf.App/Harness/HarnessRunner.cs` — seed stress library, capture + write metrics on the done-signal.
- `src/VideoShelf.App/Views/SectionDetailView.xaml` — **E2 restructure**: series grid → virtualized `ListBox`/`VirtualizingWrapPanel` with a definite-height parent; hero becomes a non-scrolling header.
- `src/VideoShelf.App/Views/SectionDetailView.xaml.cs` — only if the restructure needs a code-behind hook (avoid if possible).
- `src/VideoShelf.App/ViewModels/CreatorCardViewModel.cs`, `SeriesViewModel.cs`, `VideoCardViewModel.cs` (whichever expose `ImagePath`) — route cover loads through `IImageLoader`.
- `src/VideoShelf.App/Views/MainWindow.xaml` + `CreatorCard`/`VideoCard` controls — bind covers to the pooled `ImageSource`.
- `src/VideoShelf.App/Services/MediaBackfillService.cs`, `ResolutionBackfillService.cs` — bounded-parallel probe via `ProbeScheduler`; consolidate to a single probe pass if two exist.
- `src/VideoShelf.Core/Storage/LibraryRepository.cs` — rewrite `GetSectionSummaries` seed-path subquery; add index if EXPLAIN shows a scan.
- `src/VideoShelf.Core/Storage/VideoShelfDb.cs` — additive `CREATE INDEX IF NOT EXISTS` in `Migrate()`.
- `src/VideoShelf.App/App.xaml.cs` (or the DI composition root) — register `IImageLoader`.
- `tools/harness/Run-VisualSweep.ps1` — add the stress `--view` states; reference `Run-ScaleBench.ps1`.
- `ROADMAP.md` — flip M22 to ✅ Merged (Group F only).

---

## Group A — Stress fixture + metrics harness (verification backbone) — PR #1

> Ship this FIRST. B/C/E gates all read its metrics JSON. No production-UX change in this group — it adds dev/test-only harness surface.

### Task A1: Pure stress-library spec generator

**Files:**
- Create: `src/VideoShelf.App/Scale/StressLibrarySpec.cs`
- Test: `tests/VideoShelf.App.Tests/Scale/StressLibrarySpecTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using VideoShelf.App.Scale;
using Xunit;

public class StressLibrarySpecTests
{
    [Fact]
    public void Generates_requested_creator_and_video_totals_deterministically()
    {
        var spec = StressLibrarySpec.Generate(creators: 500, biggestSeries: 200, totalVideos: 5000, seed: 1234);

        Assert.Equal(500, spec.Creators.Count);
        Assert.Equal(5000, spec.Creators.Sum(c => c.Series.Sum(s => s.EpisodeCount)));
        Assert.Equal(200, spec.Creators.Max(c => c.Series.Count));   // the biggest creator has the target series count

        // Determinism: same seed → identical shape
        var spec2 = StressLibrarySpec.Generate(500, 200, 5000, seed: 1234);
        Assert.Equal(spec.Creators.Select(c => c.Name), spec2.Creators.Select(c => c.Name));
        Assert.Equal(spec.Creators[0].Series.Count, spec2.Creators[0].Series.Count);
    }

    [Fact]
    public void Every_creator_series_and_episode_has_a_stable_unique_name()
    {
        var spec = StressLibrarySpec.Generate(10, 5, 50, seed: 7);
        var names = spec.Creators.SelectMany(c => c.Series.SelectMany(s => s.Episodes)).Select(e => e.RelativePath);
        Assert.Equal(names.Count(), names.Distinct().Count());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test VideoShelf.slnx -c Release --nologo --filter StressLibrarySpecTests`
Expected: FAIL — `StressLibrarySpec` does not exist.

- [ ] **Step 3: Implement**

```csharp
namespace VideoShelf.App.Scale;

/// <summary>Deterministic synthetic-library plan. No I/O — turned into rows by the seeder.</summary>
public sealed record StressLibrarySpec(IReadOnlyList<StressCreator> Creators)
{
    public static StressLibrarySpec Generate(int creators, int biggestSeries, int totalVideos, int seed)
    {
        if (creators <= 0 || biggestSeries <= 0 || totalVideos < creators)
            throw new ArgumentException("totalVideos must be >= creators and counts must be positive.");

        var rng = new Random(seed);
        var list = new List<StressCreator>(creators);

        // Distribute series so exactly one creator hits `biggestSeries`; the rest taper.
        for (int c = 0; c < creators; c++)
        {
            int seriesCount = c == 0 ? biggestSeries : 1 + rng.Next(0, Math.Max(1, biggestSeries / 8));
            var series = new List<StressSeries>(seriesCount);
            for (int s = 0; s < seriesCount; s++)
                series.Add(new StressSeries($"C{c:D4}S{s:D3}", new List<StressEpisode>()));
            list.Add(new StressCreator($"Creator {c:D4}", series));
        }

        // Spread the remaining episodes round-robin across all series until totalVideos is hit.
        int placed = 0;
        var flatSeries = list.SelectMany(c => c.Series).ToList();
        while (placed < totalVideos)
        {
            var s = flatSeries[placed % flatSeries.Count];
            int epNo = s.Episodes.Count + 1;
            s.Episodes.Add(new StressEpisode(epNo, $"{s.BaseTitle}/{s.BaseTitle} {epNo:D3}.mp4"));
            placed++;
        }
        return new StressLibrarySpec(list);
    }
}

public sealed record StressCreator(string Name, List<StressSeries> Series);
public sealed record StressSeries(string BaseTitle, List<StressEpisode> Episodes)
{
    public int EpisodeCount => Episodes.Count;
}
public sealed record StressEpisode(int EpisodeNo, string RelativePath);
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test VideoShelf.slnx -c Release --nologo --filter StressLibrarySpecTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/VideoShelf.App/Scale/StressLibrarySpec.cs tests/VideoShelf.App.Tests/Scale/StressLibrarySpecTests.cs
git commit -m "feat(scale): deterministic stress-library spec generator"
```

### Task A2: DB-seed the stress library

**Files:**
- Create: `src/VideoShelf.App/Scale/StressLibrarySeeder.cs`
- Test: extend `tests/VideoShelf.App.Tests/Scale/StressLibrarySpecTests.cs` (or a new `StressLibrarySeederTests.cs`)

> **Read first:** `LibraryRepository` for the exact upsert/insert entry points (`UpsertVideo`, section/series creation) and `VideoShelfDb` for how a test DB is opened. Match the real signatures — if `UpsertVideo` differs from what's used below, adapt and note it.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Seeder_writes_all_rows_and_is_idempotent()
{
    using var db = TestDb.CreateInMemoryOrTemp();          // mirror existing repo-test setup
    var repo = new LibraryRepository(db);
    var spec = StressLibrarySpec.Generate(20, 10, 200, seed: 5);

    var seeder = new StressLibrarySeeder(repo);
    seeder.Seed(spec, sourceRoot: @"C:\stress");

    Assert.Equal(20, repo.GetSectionSummaries().Count);
    var biggest = repo.GetSectionSummaries().OrderByDescending(s => s.VideoCount).First();
    Assert.True(biggest.VideoCount > 0);

    // Idempotent: re-seeding the same spec does not duplicate rows.
    seeder.Seed(spec, sourceRoot: @"C:\stress");
    Assert.Equal(20, repo.GetSectionSummaries().Count);
}
```

- [ ] **Step 2: Run — expect FAIL** (`StressLibrarySeeder` missing).

- [ ] **Step 3: Implement** (adapt the repo calls to the real `LibraryRepository` surface you read)

```csharp
namespace VideoShelf.App.Scale;

using VideoShelf.Core.Storage;

/// <summary>Writes a StressLibrarySpec straight into the DB for render/DB-scale benchmarking
/// (no files on disk → these videos read as "missing" for playback, which is fine: we only
/// exercise browse/grid/query/thumbnail-placeholder paths). Idempotent via path-keyed upsert.</summary>
public sealed class StressLibrarySeeder(LibraryRepository repo)
{
    public void Seed(StressLibrarySpec spec, string sourceRoot)
    {
        // Use the same source/section/series/video write path the real scan uses so the
        // read-models (GetSectionSummaries etc.) see identical row shapes. Wrap in one tx.
        repo.RunInTransaction(() =>
        {
            var sourceId = repo.UpsertSource(sourceRoot, "Stress");
            foreach (var creator in spec.Creators)
            {
                var sectionId = repo.UpsertSection(sourceId, creator.Name);
                foreach (var s in creator.Series)
                {
                    var seriesId = repo.UpsertSeries(sectionId, s.BaseTitle, sortKey: s.BaseTitle);
                    foreach (var ep in s.Episodes)
                    {
                        var fullPath = System.IO.Path.Combine(sourceRoot, creator.Name, ep.RelativePath);
                        repo.UpsertVideo(seriesId, fullPath, episodeNo: ep.EpisodeNo);
                    }
                }
            }
        });
    }
}
```

> If `LibraryRepository` has **no** `RunInTransaction`/`UpsertSource`/`UpsertSection`/`UpsertSeries` helpers exposed, **STOP and report** — do not invent a parallel write path. Either (a) add thin internal upsert helpers mirroring `ScanService`'s existing inserts, or (b) call `ScanService` against a generated on-disk tree. Prefer (a); flag the choice in the PR.

- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit** `feat(scale): DB seeder for the stress library`.

### Task A3: Visual-tree node counter

**Files:**
- Create: `src/VideoShelf.App/Scale/VisualNodeCounter.cs`
- Test: `tests/VideoShelf.App.Tests/Scale/VisualNodeCounterTests.cs`

> Counting real `Visual`s needs an STA WPF test or a harness walk. Keep the **counting algorithm** pure by walking an injectable tree abstraction; the harness supplies the real `VisualTreeHelper` adapter.

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public void Counts_nodes_under_a_named_subtree()
{
    // Fake tree: root → [a → [a1,a2], b]
    var tree = new FakeNode("root", new FakeNode("a", new FakeNode("a1"), new FakeNode("a2")), new FakeNode("b"));
    int count = VisualNodeCounter.Count(tree, n => n.Children);
    Assert.Equal(5, count);
}

private sealed record FakeNode(string Name, params FakeNode[] Children);
```

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement**

```csharp
namespace VideoShelf.App.Scale;

public static class VisualNodeCounter
{
    /// <summary>Counts a node + all descendants via a caller-supplied child accessor.
    /// The harness passes a VisualTreeHelper-backed accessor; tests pass a fake.</summary>
    public static int Count<T>(T root, Func<T, IEnumerable<T>> children)
    {
        int n = 1;
        foreach (var c in children(root)) n += Count(c, children);
        return n;
    }
}
```

- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit** `feat(scale): pure visual-tree node counter`.

### Task A4: Metrics record + JSON

**Files:**
- Create: `src/VideoShelf.App/Scale/ScaleMetrics.cs`
- Test: `tests/VideoShelf.App.Tests/Scale/ScaleMetricsTests.cs`

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public void Serializes_round_trips_the_metric_fields()
{
    var m = new ScaleMetrics
    {
        View = "Browse",
        CreatorCount = 500,
        RenderedNodeCount = 38,
        InitialRenderMs = 220,
        ManagedHeapBytes = 123_456_789,
        ScanProbeMs = null,
    };
    var json = ScaleMetrics.ToJson(new[] { m });
    var back = ScaleMetrics.FromJson(json);
    Assert.Single(back);
    Assert.Equal("Browse", back[0].View);
    Assert.Equal(38, back[0].RenderedNodeCount);
}
```

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement**

```csharp
namespace VideoShelf.App.Scale;

using System.Text.Json;

public sealed class ScaleMetrics
{
    public string View { get; set; } = "";
    public int CreatorCount { get; set; }
    public int RenderedNodeCount { get; set; }
    public long InitialRenderMs { get; set; }
    public long ManagedHeapBytes { get; set; }
    public long? ScanProbeMs { get; set; }

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    public static string ToJson(IEnumerable<ScaleMetrics> items) => JsonSerializer.Serialize(items, Opts);
    public static IReadOnlyList<ScaleMetrics> FromJson(string json) =>
        JsonSerializer.Deserialize<List<ScaleMetrics>>(json) ?? new();
}
```

- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit** `feat(scale): ScaleMetrics record + JSON`.

### Task A5: Harness flags `--stress` and `--metrics-out`

**Files:**
- Modify: `src/VideoShelf.App/Harness/HarnessOptions.cs`
- Test: extend the existing `HarnessOptions` parse tests (find them under `tests/VideoShelf.App.Tests/Harness/` — match their style).

> **Read `HarnessOptions.cs` first.** Add two fields + parse cases mirroring the existing `--folder`/`--view` parsing exactly.

- [ ] **Step 1: Failing test** (mirror existing option-parse test naming)

```csharp
[Fact]
public void Parses_stress_and_metrics_out()
{
    var o = HarnessOptions.Parse(new[]
        { "--stress", "500x200x5000", "--metrics-out", @"C:\tmp\metrics.json", "--view", "Browse", "--done-signal", @"C:\tmp\done" });
    Assert.Equal("500x200x5000", o.StressSpec);
    Assert.Equal(@"C:\tmp\metrics.json", o.MetricsOut);
    Assert.True(o.IsHarness);
}
```

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement** — add to `HarnessOptions`:

```csharp
public string? StressSpec { get; private set; }   // "<creators>x<biggestSeries>x<totalVideos>"
public string? MetricsOut { get; private set; }
```

and in the arg-parse switch (matching the existing pattern):

```csharp
case "--stress":      StressSpec = Next(args, ref i); break;
case "--metrics-out": MetricsOut = Next(args, ref i); break;
```

Ensure `IsHarness` already returns true when `MetricsOut`/`StressSpec`/`DoneSignal` is set (extend its predicate if needed). Add a `(int creators,int biggest,int total) ParseStressSpec()` helper that splits on `x`.

- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit** `feat(harness): --stress and --metrics-out flags`.

### Task A6: Harness seeds stress + writes metrics

**Files:**
- Modify: `src/VideoShelf.App/Harness/HarnessRunner.cs`
- (No new unit test — exercised by the bench script in A7 + the existing harness smoke. The pure pieces are already unit-tested.)

> **Read `HarnessRunner.cs` first** to see how it opens the DB, navigates to `--view`, and writes the done-signal. Insert: (1) if `StressSpec` set → parse it, `StressLibrarySpec.Generate(...)` + `StressLibrarySeeder.Seed(...)` BEFORE the initial load; (2) after the target view has rendered (hook the same point the sweep uses to know a view settled), capture metrics and, if `MetricsOut` set, write `ScaleMetrics.ToJson(...)` to it BEFORE writing the done-signal.

- [ ] **Step 1: Implement the seed hook**

In the harness startup path, after the DB is open and before the first `ScanAndReload`/load:

```csharp
if (options.StressSpec is { } spec)
{
    var (creators, biggest, total) = options.ParseStressSpec();
    var plan = StressLibrarySpec.Generate(creators, biggest, total, seed: 20260614);
    new StressLibrarySeeder(libraryRepository).Seed(plan, sourceRoot: Path.Combine(dataDir, "stress"));
}
```

- [ ] **Step 2: Implement the metrics capture**

Capture after the target view is visible and idle (reuse the existing "settled" wait the sweep relies on; if there is a `WaitForRenderAsync`/dispatcher-idle helper, await it). Compute:

```csharp
if (options.MetricsOut is { } metricsPath)
{
    var sw = Stopwatch.StartNew();            // started at view-navigation; stop after settle
    // ... await settle ...
    sw.Stop();

    int nodes = CountRenderedContainers(targetItemsControl);   // see below
    var metric = new ScaleMetrics
    {
        View = options.View ?? "",
        CreatorCount = libraryRepository.GetSectionSummaries().Count,
        RenderedNodeCount = nodes,
        InitialRenderMs = sw.ElapsedMilliseconds,
        ManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: true),
    };
    File.WriteAllText(metricsPath, ScaleMetrics.ToJson(new[] { metric }));
}
```

`CountRenderedContainers` walks the realized `ItemContainerGenerator` of the on-screen `ListBox` (Browse grid for `--view Browse`; the series `ListBox` for `--view SectionDetail`) using `VisualNodeCounter.Count` with a `VisualTreeHelper`-backed child accessor, counting only realized `ListBoxItem` containers (this is the number virtualization is supposed to keep bounded). **STOP and report** if you cannot reach the on-screen `ListBox` from the harness without a hack — note it and fall back to counting `ItemsControl.Items` actually realized via `ItemContainerGenerator.ContainerFromIndex != null`.

- [ ] **Step 3: Build + run a manual smoke**

```bash
dotnet build VideoShelf.slnx -c Release -v minimal
# from the App output dir:
VideoShelf.App.exe --stress 50x20x300 --view Browse --metrics-out .\m.json --done-signal .\done.txt --data-dir .\scratch
```
Expected: `done.txt` written `OK`, `m.json` contains one Browse metric with `CreatorCount=50`.

- [ ] **Step 4: Commit** `feat(harness): seed stress library + emit scale metrics`.

### Task A7: `Run-ScaleBench.ps1` + gates

**Files:**
- Create: `tools/harness/Run-ScaleBench.ps1`

- [ ] **Step 1: Implement** the bench script (no unit test — it IS the gate):

```powershell
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
  & $exe --stress $Spec --view $view --metrics-out $metrics --done-signal $done --data-dir (Join-Path $scratch "data") | Out-Null
  if (-not (Test-Path $metrics)) { throw "$view: no metrics written" }
  $m = (Get-Content $metrics -Raw | ConvertFrom-Json)[0]
  Write-Host "$view  nodes=$($m.RenderedNodeCount)  renderMs=$($m.InitialRenderMs)  heapMB=$([math]::Round($m.ManagedHeapBytes/1MB))"
  if ($m.RenderedNodeCount -gt $maxNodes)      { throw "$view: realized nodes $($m.RenderedNodeCount) > $maxNodes (virtualization regressed)" }
  if ($m.InitialRenderMs   -gt $MaxInitialRenderMs) { throw "$view: initial render $($m.InitialRenderMs)ms > $MaxInitialRenderMs" }
}

Run-One "Browse"       $MaxBrowseNodes
Run-One "SectionDetail" $MaxSeriesNodes   # harness must open the biggest creator for this view
Write-Host "SCALE BENCH PASS" -ForegroundColor Green
```

> The `SectionDetail` view must open the **biggest** creator (creator 0). Ensure the harness, when `--view SectionDetail` + `--stress` are both set, navigates to the creator with the most series. **STOP and report** if `--view SectionDetail` currently needs an id it cannot derive — add a `--stress-open-biggest` convenience or pick section index 0 (the spec makes creator 0 the biggest).

- [ ] **Step 2: Run the bench**

Run: `pwsh -File tools/harness/Run-ScaleBench.ps1 -Spec 500x200x5000`
Expected: two lines + `SCALE BENCH PASS`. **At this point Browse is already virtualized (M17) so it should pass; `SectionDetail` will FAIL the node gate — that is expected and is exactly what Group B fixes.** Record the failing baseline number in the PR description.

- [ ] **Step 3: Commit** `feat(scale): Run-ScaleBench harness gate`.

### Group A close-out
- [ ] Full gate: `dotnet test VideoShelf.slnx -c Release --nologo -v q` → green, total up by the new tests.
- [ ] Push branch `feat/m22-a-scale-harness`, open PR, watch CI green, merge `--merge --delete-branch`, sync main.

---

## Group B — Series-grid virtualization (the E2 restructure) — PR #2

> **The headline.** Today `SectionDetailView.xaml` is `ScrollViewer > StackPanel > [hero] + [ItemsControl/WrapPanel of series tiles]`. The `WrapPanel` is inside a `StackPanel` inside the page `ScrollViewer`, so it is measured with **infinite height** and never virtualizes — every series tile (and every expanded accordion panel) is realized at once. Fix: give the series grid a **definite-height** parent and a **virtualizing** panel, mirroring the working M17 Browse grid host.

### Task B1: Restructure the page layout for a definite-height virtualized grid

**Files:**
- Modify: `src/VideoShelf.App/Views/SectionDetailView.xaml`
- Reference (do not change behavior): the M17 Browse host in `src/VideoShelf.App/Views/MainWindow.xaml` (`CreatorsGridListBox` + `VirtualizingWrapPanel`).

> **Read both files first.** Replicate the Browse host's virtualization settings exactly.

- [ ] **Step 1: Replace the page-level scroll + StackPanel with a Grid**

Change the root from `ScrollViewer > StackPanel` to a `Grid` with two rows:

```xml
<Grid>
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto"/>   <!-- hero header: scrolls away is acceptable; if hero must scroll with the list, make it the ListBox header instead (see B2) -->
    <RowDefinition Height="*"/>      <!-- DEFINITE height for the list → virtualization works -->
  </Grid.RowDefinitions>

  <!-- existing hero banner content moves here, Grid.Row=0 -->

  <ListBox x:Name="SeriesGridListBox" Grid.Row="1"
           ItemsSource="{Binding SeriesList}"
           Background="Transparent" BorderThickness="0"
           ScrollViewer.HorizontalScrollBarVisibility="Disabled"
           ScrollViewer.VerticalScrollBarVisibility="Auto"
           ScrollViewer.CanContentScroll="True"
           VirtualizingPanel.IsVirtualizing="True"
           VirtualizingPanel.VirtualizationMode="Recycling"
           VirtualizingPanel.ScrollUnit="Pixel">
    <ListBox.ItemsPanel>
      <ItemsPanelTemplate>
        <wpftk:VirtualizingWrapPanel SpacingMode="Uniform" Orientation="Horizontal"/>
      </ItemsPanelTemplate>
    </ListBox.ItemsPanel>
    <ListBox.ItemContainerStyle>
      <!-- strip ListBox selection chrome; the tile keeps its own visuals.
           Copy the M17 Browse ItemContainerStyle so focus/hover/selection match. -->
    </ListBox.ItemContainerStyle>
    <ListBox.ItemTemplate>
      <DataTemplate>
        <!-- the EXISTING series tile Border + accordion content moves here verbatim -->
      </DataTemplate>
    </ListBox.ItemTemplate>
  </ListBox>
</Grid>
```

Notes:
- `wpftk` is the existing `VirtualizingWrapPanel` xmlns used in `MainWindow.xaml` — reuse the same prefix/namespace declaration.
- Use **`ScrollUnit="Pixel"`** (not item) because items have **variable height** when an accordion expands — pixel scrolling + recycling tolerates variable heights; item-scrolling does not.
- Keep the series tile `DataTemplate` content (header button, expand animation, episodes `ItemsControl`) **unchanged** — only its hosting panel changes.

- [ ] **Step 2: Build + smoke the biggest creator**

```bash
dotnet build VideoShelf.slnx -c Release -v minimal
VideoShelf.App.exe --stress 500x200x5000 --view SectionDetail --metrics-out .\sd.json --done-signal .\done.txt --data-dir .\scratch
```
Expected: `sd.json` `RenderedNodeCount` now bounded (tens, not ~200). If it is still ~200, the list is still being measured with infinite height → **the hero/Grid row is wrong; STOP and report** (likely the Grid is itself inside another auto-sized container up the tree).

- [ ] **Step 3: Commit** `feat(scale): virtualize creator-page series grid (definite-height ListBox + VWP)`.

### Task B2: Decide hero scroll behavior + bound expanded-accordion height

**Files:**
- Modify: `src/VideoShelf.App/Views/SectionDetailView.xaml`

> Two interaction risks from B1: (a) the hero no longer scrolls with the grid (it's a fixed top row); (b) an expanded accordion tile can be very tall (200-episode series), and a single very-tall realized item defeats the point.

- [ ] **Step 1: Hero** — if the owner-visible behavior must keep the hero scrolling away with content, move the hero into the `ListBox` header instead of Grid.Row 0:

```xml
<ListBox ...>
  <ListBox.Template>
    <ControlTemplate TargetType="ListBox">
      <ScrollViewer CanContentScroll="True">
        <DockPanel>
          <ContentPresenter DockPanel.Dock="Top" Content="{Binding HeroHeader, RelativeSource={RelativeSource AncestorType=UserControl}}"/>
          <ItemsPresenter/>
        </DockPanel>
      </ScrollViewer>
    </ControlTemplate>
  </ListBox.Template>
</ListBox>
```

This is fiddly with `VirtualizingWrapPanel`. **Simplest acceptable shipping choice (default): keep the hero as a fixed `Grid.Row=0` header that does NOT scroll** — it reads as a stable creator banner (common in media apps). Only attempt the scroll-with-content header if the owner asks. Document the choice in the PR.

- [ ] **Step 2: Bound expanded height** — cap an expanded tile's episode list with a max-height inner scroll so one expanded 200-episode series cannot blow up the realized item:

In the series tile's expanded `StackPanel`/episodes `ItemsControl`, wrap the episodes in:

```xml
<ScrollViewer MaxHeight="360" VerticalScrollBarVisibility="Auto"
              HorizontalScrollBarVisibility="Disabled">
  <!-- existing episodes ItemsControl -->
</ScrollViewer>
```

Keep the existing opacity-expand animation (reduced-motion-gated) intact. This preserves the M21 motion + the M16/M17 per-episode affordances.

- [ ] **Step 3: Re-run the bench** — `pwsh -File tools/harness/Run-ScaleBench.ps1` → expect `SCALE BENCH PASS` (both views now under their node caps).

- [ ] **Step 4: Commit** `feat(scale): fixed creator hero header + bounded expanded-accordion height`.

### Task B3: Regression unit test for the series VM under load

**Files:**
- Test: extend `tests/VideoShelf.App.Tests/.../SectionDetailViewModelTests.cs`

- [ ] **Step 1: Add a test** asserting the VM still lazy-loads episodes only on activate, at scale:

```csharp
[Fact]
public void Biggest_creator_page_loads_series_without_eager_episode_load()
{
    using var db = TestDb.CreateInMemoryOrTemp();
    var repo = new LibraryRepository(db);
    new StressLibrarySeeder(repo).Seed(StressLibrarySpec.Generate(5, 200, 1000, seed: 9), @"C:\stress");
    var biggest = repo.GetSectionSummaries().OrderByDescending(s => s.VideoCount).First();

    var vm = NewSectionDetailViewModel(repo);          // mirror the existing test factory
    vm.Load(biggest.Id);

    Assert.Equal(200, vm.SeriesList.Count);
    Assert.All(vm.SeriesList, s => Assert.False(s.EpisodesLoaded));   // none eagerly loaded
}
```

> If `SeriesViewModel` exposes the loaded flag under a different name than `EpisodesLoaded`, match it. If it's private, assert via `s.Episodes.Count == 0` before activation instead.

- [ ] **Step 2: Run — expect PASS** (this validates that virtualization didn't accidentally eager-activate tiles). **If it FAILS because the ListBox realization triggers `Activate`, STOP and report** — the tile's `Loaded`/visibility must not call `EnsureEpisodesLoadedAsync` until the user expands it.

- [ ] **Step 3: Commit** `test(scale): creator page stays lazy at 200 series`.

### Group B close-out
- [ ] Full test gate green.
- [ ] **Screenshot sweep** on the normal (non-stress) library: dispatch a Sonnet subagent to run `Run-VisualSweep.ps1`, view the `SectionDetail` PNGs, and return a TEXT verdict that the creator page (hero + series tiles + an expanded accordion) renders unchanged from M21. [[feedback-screenshot-verify-in-subagent]]
- [ ] Push `feat/m22-b-series-virtualization`, PR, CI green, merge, sync.

---

## Group C — Thumbnail decode-size + bounded LRU — PR #3

> Covers currently bind a path string to `Image.Source` with `IsAsync=True` → WPF decodes each at **full resolution** and holds it; at 500+ cards this is large managed+unmanaged memory and slow decode. Fix: decode at display size (`DecodePixelWidth`) and share frozen `BitmapImage`s through a bounded LRU.

### Task C1: `IImageLoader` + `PooledBitmapLoader`

**Files:**
- Create: `src/VideoShelf.App/Services/IImageLoader.cs`, `src/VideoShelf.App/Services/PooledBitmapLoader.cs`
- Test: `tests/VideoShelf.App.Tests/Scale/PooledBitmapLoaderTests.cs`

- [ ] **Step 1: Failing test** (the LRU eviction logic is pure and testable without real images by injecting a decode delegate)

```csharp
[Fact]
public void Lru_caps_entries_and_evicts_least_recently_used()
{
    int decodes = 0;
    var loader = new PooledBitmapLoader(maxEntries: 2, decode: (path, w) => { decodes++; return new object(); });

    var a1 = loader.GetOrDecode("a", 200);   // miss → decode (1)
    var b1 = loader.GetOrDecode("b", 200);   // miss → decode (2)
    var a2 = loader.GetOrDecode("a", 200);   // hit  → no decode
    Assert.Same(a1, a2);
    Assert.Equal(2, decodes);

    var c1 = loader.GetOrDecode("c", 200);   // miss → evicts LRU ("b"); decode (3)
    var b2 = loader.GetOrDecode("b", 200);   // miss again (was evicted); decode (4)
    Assert.Equal(4, decodes);
}

[Fact]
public void Key_includes_decode_width()
{
    int decodes = 0;
    var loader = new PooledBitmapLoader(maxEntries: 10, decode: (p, w) => { decodes++; return new object(); });
    loader.GetOrDecode("a", 200);
    loader.GetOrDecode("a", 400);   // different width → separate entry
    Assert.Equal(2, decodes);
}
```

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement**

`IImageLoader.cs`:

```csharp
namespace VideoShelf.App.Services;

using System.Windows.Media;

public interface IImageLoader
{
    /// <summary>Returns a frozen ImageSource decoded at ~decodePixelWidth, or null on failure.
    /// Never throws into the UI (fail-safe placeholder).</summary>
    ImageSource? Load(string? path, int decodePixelWidth);
}
```

`PooledBitmapLoader.cs` (production decode + the testable LRU core):

```csharp
namespace VideoShelf.App.Services;

using System.Windows.Media;
using System.Windows.Media.Imaging;

public sealed class PooledBitmapLoader : IImageLoader
{
    private readonly int _maxEntries;
    private readonly Func<string, int, object> _decode;          // seam for tests
    private readonly LinkedList<string> _order = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, object Value)> _map = new();
    private readonly object _gate = new();

    // Production ctor.
    public PooledBitmapLoader(int maxEntries = 600)
        : this(maxEntries, DecodeFrozen) { }

    // Test ctor.
    public PooledBitmapLoader(int maxEntries, Func<string, int, object> decode)
    {
        _maxEntries = Math.Max(1, maxEntries);
        _decode = decode;
    }

    public object GetOrDecode(string path, int width)
    {
        var key = $"{path}|{width}";
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var hit))
            {
                _order.Remove(hit.Node);
                _order.AddFirst(hit.Node);
                return hit.Value;
            }
            var value = _decode(path, width);
            var node = new LinkedListNode<string>(key);
            _order.AddFirst(node);
            _map[key] = (node, value);
            while (_map.Count > _maxEntries)
            {
                var lru = _order.Last!;
                _order.RemoveLast();
                _map.Remove(lru.Value);
            }
            return value;
        }
    }

    public ImageSource? Load(string? path, int decodePixelWidth)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
        try { return (ImageSource)GetOrDecode(path, decodePixelWidth); }
        catch { return null; }   // fail-safe — caller shows placeholder
    }

    private static object DecodeFrozen(string path, int width)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;      // decode now, release the file handle
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.DecodePixelWidth = width;                    // decode at display size, not full-res
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();                                    // shareable across the UI thread, no per-use copy
        return bmp;
    }
}
```

- [ ] **Step 4: Run — expect PASS** (3 tests).
- [ ] **Step 5: Commit** `feat(scale): pooled decode-at-size bitmap loader`.

### Task C2: Route card covers through the loader

**Files:**
- Modify: the cover-exposing VMs (`CreatorCardViewModel`, `SeriesViewModel`, and any `VideoCardViewModel`) + their XAML bindings + DI registration.

> **Read each VM first.** Today they expose `ImagePath` (string) loaded async, and XAML binds `Image.Source` to it with `IsAsync=True`. Change to expose an `ImageSource? Cover` produced by `IImageLoader` at the card's known thumb width.

- [ ] **Step 1: Register the loader** in the composition root (`App.xaml.cs`/DI):

```csharp
services.AddSingleton<IImageLoader>(_ => new PooledBitmapLoader(maxEntries: 600));
```

- [ ] **Step 2: In each card VM**, inject `IImageLoader` and replace the path-load with:

```csharp
private ImageSource? _cover;
public ImageSource? Cover { get => _cover; private set => SetProperty(ref _cover, value); }

// where the old code resolved ImagePath (override art → seed → thumbnail cache),
// keep resolving the PATH the same way, then:
public async Task LoadImageAsync()
{
    var path = await ResolveThumbnailPathAsync();        // existing resolution logic, unchanged
    // CardThumbHeight token ≈ 158px; width ≈ CardWidth token. Decode at card width in device px.
    Cover = _imageLoader.Load(path, decodePixelWidth: 280);
}
```

> Decode width = the `CardWidth` design token in device pixels (use the literal the token resolves to — read `DesignTokens.xaml`; if `CardWidth` ≈ 280, pass 280). The series tile may use a different width — pass its own.

- [ ] **Step 3: Update XAML** — bind `Image.Source="{Binding Cover}"` (drop `IsAsync=True`; the loader already returns a ready frozen source). Keep the placeholder visual shown when `Cover` is null (a `Cover`-null → placeholder trigger; reuse the existing placeholder).

- [ ] **Step 4: Build + sweep** — dispatch a Sonnet subagent to run `Run-VisualSweep.ps1` and confirm covers still render on Browse + creator page + Home rails (TEXT verdict). **If covers vanish, STOP and report** — likely the placeholder trigger now needs `Cover` (ImageSource) not `ImagePath` (string).

- [ ] **Step 5: Run the bench with a memory check** — `pwsh -File tools/harness/Run-ScaleBench.ps1`; note `heapMB` dropped vs the Group A baseline (record both numbers in the PR). Add an assertion to the bench:

```powershell
# in Run-One, optionally:
# if ($view -eq "Browse" -and $m.ManagedHeapBytes -gt 700MB) { throw "Browse heap $([math]::Round($m.ManagedHeapBytes/1MB))MB too high" }
```
Set the ceiling from the measured post-fix number + headroom (do NOT hardcode blindly — measure first, then gate ~30% above observed).

- [ ] **Step 6: Commit** `feat(scale): decode card covers at display size via pooled loader`.

### Group C close-out
- [ ] Test gate green; sweep verdict PASS; bench shows reduced heap.
- [ ] Push `feat/m22-c-thumbnail-memory`, PR, CI green, merge, sync.

---

## Group D — Scan throughput: bounded-parallel probe — PR #4

> The sequential libVLC duration/resolution probe is the scan bottleneck (1–3s/file; ~16–50 min for a 1000-file first scan). Parallelize with **bounded** concurrency. **Risk:** multiple concurrent libVLC `MediaPlayer` decoders can destabilize. The default degree is conservative and the sequential path (degree=1) must remain a safe fallback.

### Task D1: Pure bounded-concurrency scheduler

**Files:**
- Create: `src/VideoShelf.Core/Scanning/ProbeScheduler.cs`
- Test: `tests/VideoShelf.Core.Tests/Scanning/ProbeSchedulerTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using VideoShelf.Core.Scanning;

public class ProbeSchedulerTests
{
    [Fact]
    public async Task Runs_all_items_with_bounded_concurrency()
    {
        int current = 0, peak = 0;
        var gate = new object();
        var items = Enumerable.Range(0, 50).ToList();

        await ProbeScheduler.RunAsync(items, degree: 4, async (i, ct) =>
        {
            lock (gate) { current++; peak = Math.Max(peak, current); }
            await Task.Delay(5, ct);
            lock (gate) { current--; }
        }, CancellationToken.None);

        Assert.True(peak <= 4, $"peak concurrency {peak} exceeded degree 4");
    }

    [Fact]
    public async Task Honors_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var started = 0;
        var task = ProbeScheduler.RunAsync(Enumerable.Range(0, 1000).ToList(), degree: 2, async (i, ct) =>
        {
            Interlocked.Increment(ref started);
            if (started == 3) cts.Cancel();
            await Task.Delay(10, ct);
        }, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(started < 1000);
    }
}
```

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement**

```csharp
namespace VideoShelf.Core.Scanning;

public static class ProbeScheduler
{
    /// <summary>Runs <paramref name="work"/> over items with at most <paramref name="degree"/> in flight.
    /// degree=1 is exactly sequential. Cancellation propagates as OperationCanceledException.</summary>
    public static async Task RunAsync<T>(
        IReadOnlyList<T> items, int degree, Func<T, CancellationToken, Task> work, CancellationToken ct)
    {
        degree = Math.Max(1, degree);
        using var sem = new SemaphoreSlim(degree);
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            await sem.WaitAsync(ct);
            tasks.Add(Task.Run(async () =>
            {
                try { await work(item, ct); }
                finally { sem.Release(); }
            }, ct));
        }
        await Task.WhenAll(tasks);
    }
}
```

- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit** `feat(scan): pure bounded-concurrency probe scheduler`.

### Task D2: Parallelize the backfill services

**Files:**
- Modify: `src/VideoShelf.App/Services/MediaBackfillService.cs` (+ `ResolutionBackfillService.cs` — **consolidate**: the M18 note says the duration pass already reads resolution; if so, fold resolution into the one pass and reduce `ResolutionBackfillService` to a thin caller or delete it. **Read both first** and STOP-and-report if they are genuinely two probes — do not silently change probe semantics.)

> **Crash-safety invariant (must hold):** each file's `SetDuration`/`SetResolution` commits independently and the pass remains resumable (only `WHERE duration IS NULL`). Parallelism must NOT batch commits into one transaction that loses progress on crash.

- [ ] **Step 1: Wrap the per-file probe loop** in `ProbeScheduler`:

```csharp
var pending = _repo.GetVideosNeedingDuration();          // existing query
int degree = _settings.GetProbeConcurrency(defaultValue: 3);   // new setting, see D3
await ProbeScheduler.RunAsync(pending, degree, async (video, ct) =>
{
    try
    {
        var probe = await _probe.ProbeAsync(video.FilePath, ct);   // existing IMediaProbe call
        if (probe.Duration is { } d)   _repo.SetDuration(video.Id, d);          // independent commit
        if (probe.Width is { } w && probe.Height is { } h) _repo.SetResolution(video.Id, w, h);
    }
    catch (OperationCanceledException) { throw; }
    catch { /* per-file failure: skip, retried next scan (existing behavior) */ }
}, ct);
```

> Confirm `IMediaProbe.ProbeAsync` is thread-safe to call concurrently with distinct media. **If `IMediaProbe`/`LibVlcMediaProbe` shares a single `MediaPlayer`/`LibVLC` instance that is NOT safe for concurrent `ProbeAsync`, STOP and report** — either create one `MediaPlayer` per probe call (preferred; libVLC supports many players on one `LibVLC`) or keep degree=1. Do not ship a data race.

- [ ] **Step 2: Repo thread-safety** — `Microsoft.Data.Sqlite` connections are NOT thread-safe. **If the repo holds one shared `SqliteConnection`, the concurrent `SetDuration` calls will race.** Options, in order of preference: (a) serialize only the DB writes behind a lock while probes run in parallel (probes are the slow part, writes are µs); (b) one connection per worker. Implement (a):

```csharp
private readonly object _writeGate = new();
// inside the worker:
lock (_writeGate) { _repo.SetDuration(video.Id, d); if (...) _repo.SetResolution(...); }
```

**STOP and report** if the repo's connection model makes even serialized writes unsafe from a non-UI thread (e.g. it's affinitized to the dispatcher) — then keep writes marshalled but probes parallel.

- [ ] **Step 3: Test** — add a service-level test using a **fake** `IMediaProbe` (no real libVLC) that records concurrency and asserts all rows get a duration:

```csharp
[Fact]
public async Task Backfill_probes_all_pending_in_parallel_and_persists_each()
{
    using var db = TestDb.CreateInMemoryOrTemp();
    var repo = new LibraryRepository(db);
    new StressLibrarySeeder(repo).Seed(StressLibrarySpec.Generate(3, 5, 60, seed: 2), @"C:\s");

    var fakeProbe = new FakeMediaProbe(durationSeconds: 100, width: 1280, height: 720);
    var svc = new MediaBackfillService(repo, fakeProbe, settings: FakeSettings.WithProbeConcurrency(4));
    await svc.BackfillAsync(CancellationToken.None);

    Assert.Equal(60, repo.CountVideosWithDuration());      // add this count helper if absent
    Assert.True(fakeProbe.MaxObservedConcurrency > 1);     // proves parallelism engaged
}
```

- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit** `feat(scan): bounded-parallel duration/resolution backfill`.

### Task D3: Probe-concurrency setting + safe default

**Files:**
- Modify: `SettingsRepository` (add `GetProbeConcurrency`/key `probe_concurrency`) — additive `settings` key, **no migration**.

- [ ] **Step 1: Add the getter** (default 3, clamp 1–8):

```csharp
public int GetProbeConcurrency(int defaultValue = 3)
    => Math.Clamp(GetInt("probe_concurrency", defaultValue), 1, 8);
```

- [ ] **Step 2: Test** the clamp + default (mirror existing settings tests).
- [ ] **Step 3: Commit** `feat(scan): configurable probe concurrency (default 3, clamp 1-8)`.

### Task D4: Real-clip scan-throughput bench (dev-only)

**Files:**
- Create: `tools/harness/Generate-StressClips.ps1` (ffmpeg, dev-only — NOT shipped, NOT on the app's runtime PATH).

> This proves the parallel speedup on REAL files (the DB-seed fixture can't, since it has no files to probe). Generate a few hundred tiny real clips and time a scan at degree=1 vs degree=3.

- [ ] **Step 1: Implement the generator**

```powershell
# Generate-StressClips.ps1 — dev-only; needs ffmpeg on the dev machine PATH (the APP never uses ffmpeg).
param([string]$Out = "$env:TEMP\vs-stress-clips", [int]$Creators = 20, [int]$ClipsPerCreator = 15)
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
```

- [ ] **Step 2: Manual timing** (record results in the PR; not a CI gate — CI has no ffmpeg/clips):

```powershell
pwsh -File tools/harness/Generate-StressClips.ps1 -Creators 20 -ClipsPerCreator 15   # 300 clips
# Time a first scan at degree=1 then degree=3 against $Out via --folder, comparing total scan time.
```
Expected: degree=3 materially faster than degree=1 (target ≥2× on a multi-core dev box), identical final DB row counts/durations. **If degree=3 is NOT faster or the DB state differs, STOP and report.**

- [ ] **Step 3: Commit** `feat(scan): dev-only real-clip generator for throughput benchmarking`.

### Group D close-out
- [ ] Test gate green; manual throughput timing recorded in PR; DB state identical degree-1 vs degree-3.
- [ ] Push `feat/m22-d-scan-throughput`, PR, CI green, merge, sync.

---

## Group E — DB read-path tuning — PR #5

> `GetSectionSummaries` runs a **per-row scalar subquery** for the seed thumbnail path; at 500 creators that's 500 subqueries. Replace it and confirm index usage with EXPLAIN-QUERY-PLAN tests. Additive indexes only — **no `user_version` runner**.

### Task E1: EXPLAIN-QUERY-PLAN test (characterize current, then assert improved)

**Files:**
- Test: `tests/VideoShelf.Core.Tests/Storage/SectionSummaryQueryPlanTests.cs`

- [ ] **Step 1: Write the test** that runs `EXPLAIN QUERY PLAN` on the summaries SQL and asserts no full `SCAN` of `videos` for the seed lookup:

```csharp
[Fact]
public void Section_summaries_use_indexes_not_full_scans()
{
    using var db = TestDb.CreateInMemoryOrTemp();
    var repo = new LibraryRepository(db);
    new StressLibrarySeeder(repo).Seed(StressLibrarySpec.Generate(50, 20, 500, seed: 3), @"C:\s");

    var plan = repo.ExplainSectionSummaries();   // new test-only helper returning the EXPLAIN rows as text
    // No unindexed table scan of videos for the per-section seed path.
    Assert.DoesNotContain("SCAN videos", plan, StringComparison.OrdinalIgnoreCase);
}
```

> Add `ExplainSectionSummaries()` as an `internal` helper (exposed to tests via `InternalsVisibleTo`, which this project already uses for Core.Tests — confirm) that runs `EXPLAIN QUERY PLAN ` + the exact summaries SQL and returns concatenated `detail` rows.

- [ ] **Step 2: Run — expect FAIL** (current seed subquery scans). Record the actual plan text in the PR.

### Task E2: Rewrite the seed-path lookup + add the index

**Files:**
- Modify: `src/VideoShelf.Core/Storage/LibraryRepository.cs` (`GetSectionSummaries`)
- Modify: `src/VideoShelf.Core/Storage/VideoShelfDb.cs` (`Migrate()`)

- [ ] **Step 1: Add an additive index** in `Migrate()` (guarded, no version runner):

```csharp
cmd.CommandText = "CREATE INDEX IF NOT EXISTS ix_videos_section ON videos(section_id);";
cmd.ExecuteNonQuery();
```

> Only add this if `videos` actually has a `section_id` column used by the seed lookup (the digest's summaries SQL references `videos WHERE section_id=sc.id`). **If the seed path is instead keyed via `series.section_id` join, index `videos(series_id)` already exists** — then the win is in rewriting the subquery, and the index may be unnecessary. Confirm against the real SQL; add the index only if EXPLAIN still scans.

- [ ] **Step 2: Rewrite the seed-path** from a correlated per-row subquery to a single grouped pass — e.g. a `LEFT JOIN` to a per-section "first video" derived table, or `MIN(v.file_path)`-style aggregation already in the existing `GROUP BY sc.id`. Keep the returned `SectionSummary` shape **identical** (same columns, same order) so no caller changes. Read the current SQL and refactor in place; do not change the public method signature.

- [ ] **Step 3: Run the E1 test — expect PASS** (no `SCAN videos`). Run the full Core suite to confirm summaries still return correct counts/seeds.

- [ ] **Step 4: Commit** `perf(db): single-pass section-summary seed lookup + section index`.

### Task E3: Summaries timing assertion in the bench (optional, soft gate)

- [ ] Add to `Run-ScaleBench.ps1` a check that `InitialRenderMs` for Browse at 500 creators stays under `MaxInitialRenderMs` (already gated in A7) — E should keep or improve it. No new code if A7's gate already covers it; just confirm Browse render didn't regress.

### Group E close-out
- [ ] Test gate green; EXPLAIN test passes; Browse render time ≤ Group A baseline.
- [ ] Push `feat/m22-e-db-tuning`, PR, CI green, merge, sync.

---

## Group F — Sweep wiring, consolidation & ROADMAP flip — PR #6

### Task F1: Wire stress states into the standard sweep

**Files:**
- Modify: `tools/harness/Run-VisualSweep.ps1`

- [ ] **Step 1:** Add an optional `-Scale` switch to `Run-VisualSweep.ps1` that invokes `Run-ScaleBench.ps1` after the normal view sweep (so one command runs both functional + scale gates). Keep them separable (scale bench needs the bigger fixture).
- [ ] **Step 2:** Document in the runbook (`docs/superpowers/WORKFLOW-execution.md`) the two new harness flags + the bench command + the node/render gates. Append a short "M22 scale verification" subsection.
- [ ] **Step 3: Commit** `chore(harness): fold scale bench into the visual sweep`.

### Task F2: Final consolidation + full verification

- [ ] **Step 1:** Full test gate: `dotnet test VideoShelf.slnx -c Release --nologo -v q` → green; record the new total (baseline 939 + the tasks above).
- [ ] **Step 2:** Run `pwsh -File tools/harness/Run-ScaleBench.ps1 -Spec 500x200x5000` → `SCALE BENCH PASS` (Browse + SectionDetail under node caps, render under the ms gate).
- [ ] **Step 3:** Dispatch a Sonnet subagent to run the normal `Run-VisualSweep.ps1` and return a TEXT verdict that all views render unchanged (no scale fixture) — Browse, creator page (hero + virtualized series grid + expanded accordion), Home rails, player. [[feedback-screenshot-verify-in-subagent]]
- [ ] **Step 4:** Real-app launch smoke (a render-crash backstop — build+unit tests miss XAML template crashes, per the M21 KeyTime lesson): `VideoShelf.App.exe --view SectionDetail --folder <small real lib> --done-signal .\d.txt` → `OK`. [[wpfui-theming-and-visual-verification]]

### Task F3: Flip the ROADMAP

**Files:**
- Modify: `ROADMAP.md`

- [ ] **Step 1:** Add the M22 row under a new **v5** banner, status ✅ Merged, with the PR list (#1–#6 actual numbers), the final test count, and a one-line shipped summary.
- [ ] **Step 2:** Append a decision-log entry capturing the durable gotchas discovered (the real ones found during execution — e.g. the exact E2 layout fix that worked, whether scan parallelism shipped at degree 3 or was clamped, the measured heap drop, any STOP-and-report resolutions).
- [ ] **Step 3: Commit** `docs(roadmap): M22 Performance & scale shipped (v5)` and let this ride the Group F PR.

### Group F close-out
- [ ] Push `feat/m22-f-sweep-roadmap`, PR, CI green, merge `--merge --delete-branch`, sync main.
- [ ] **Ping the owner** (Phase B handoff): M22 merged & CI-green; next session plans the next milestone.

---

## Acceptance criteria (the whole milestone)

1. **Series-grid virtualization:** at the 500×200×5000 fixture, the biggest creator page realizes ≤ `MaxSeriesNodes` (≈60) series containers, not ~200 (the bench `SectionDetail` gate passes; it FAILED at the Group A baseline).
2. **Browse stays bounded:** Browse realizes ≤ `MaxBrowseNodes` (≈80) at 500 creators (already true from M17; bench keeps it from regressing).
3. **Thumbnail memory:** managed heap at a Browse scroll over 500 creators is materially lower post-Group-C than the Group-A baseline (numbers recorded in the PR; bench heap ceiling set ~30% above the measured post-fix value).
4. **Scan throughput:** a real-clip scan at degree 3 is ≥2× faster than degree 1 on a multi-core box, with **identical** final DB state (durations/resolutions/row counts). Crash-safety/resumability preserved (per-file independent commit; `WHERE duration IS NULL`).
5. **DB read:** `GetSectionSummaries` no longer full-scans `videos` for the seed path (EXPLAIN test); Browse initial render at 500 creators ≤ `MaxInitialRenderMs`.
6. **No regressions:** full test suite green (≥ 939 + new tests); functional screenshot sweep unchanged; real-app SectionDetail launch smoke `OK`.
7. **Invariants held:** no `user_version` runner (additive `CREATE INDEX IF NOT EXISTS` + `settings` keys only); no `ui:*` retemplate / palette edit; no `AutomationProperties`/screen-reader reintroduced; library files never written.

## STOP-and-report triggers (flagged inline above, collected here)
- `LibraryRepository` lacks transaction/upsert helpers the seeder needs (A2).
- Harness cannot reach the on-screen `ListBox` to count realized containers (A6).
- After B1, `SectionDetail` realized nodes are still ~200 → the list is still infinite-height up the tree (B1/B2).
- ListBox realization eager-activates tiles → episodes load before expand (B3).
- Covers vanish after switching to `ImageSource` binding (C2).
- `IMediaProbe`/`LibVlcMediaProbe` shares non-concurrent libVLC state, or the repo connection is unsafe for off-thread/serialized writes (D2).
- `ResolutionBackfillService` is a genuinely separate second probe (don't silently change probe semantics) (D2).
- degree-3 scan not faster, or DB state differs from degree-1 (D4).

## Self-review notes (author)
- **Spec coverage:** every owner-locked scope item maps to a group — virtualization→B, thumbnail memory→C, bounded-parallel scan→D, DB tuning→E, stress-fixture+metrics harness→A, sweep/flip→F. Single milestone, stacked PRs, Group A first. ✓
- **Type consistency:** `StressLibrarySpec.Generate` / `StressLibrarySeeder.Seed` / `ScaleMetrics` / `VisualNodeCounter.Count` / `IImageLoader.Load` / `PooledBitmapLoader.GetOrDecode` / `ProbeScheduler.RunAsync` / `GetProbeConcurrency` names are used consistently across tasks. ✓
- **No silent caps:** the E2 accordion-height bound (B2) and the conservative default probe degree (D3) are both logged choices, not hidden truncations. ✓
