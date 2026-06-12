# VideoShelf M6 — Harness + Release Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Written for Sonnet execution. If anything in the repo does not match what this plan states (a file, signature, property name, or path differs), STOP and report what you found instead of guessing or "fixing it up." The plan author verified these facts against the repo on 2026-06-12, but threading the data-dir override (Task A2) and the source-add/seed APIs (Task A3) touch code this plan describes by behavior rather than verbatim — those are the most likely mismatch points.**

**Goal:** Ship the final milestone — a deterministic visual-verification harness (fixtures + launch hooks + screenshot sweep), a retroactive eyes-on pass over **all** prior UI (shell / browse / player / PiP / discovery / section-detail / rename) with fixes, and MSIX packaging plus a CI `package` job that asserts no media tools are bundled.

**Architecture:** Three workstreams executed in order. **(A)** Add command-line launch hooks to the WPF app (`--folder`, `--autostart`, `--done-signal`, plus support flags `--data-dir`, `--view`, `--play`, `--seed-demo`) parsed by a unit-tested pure parser, driven by a `HarnessRunner` that scans an **isolated** throwaway library, navigates to a requested view, settles, and writes a done-signal file; plus PowerShell scripts that generate tiny ffmpeg-made fixture clips and capture each view via GDI screen-grab (mirroring the proven VideoTriage `Drive-AndCapture` pattern). **(B)** Run the sweep across every nav state, have a Sonnet subagent read the PNGs and return a text verdict, fix any visual defects (additive-only per the WPF-UI theming rule), re-verify. **(C)** Self-contained `dotnet publish`, a hand-authored `AppxManifest.xml` + logo assets packed with `makeappx`, signed with an ephemeral self-signed cert, and a CI `package` job that runs the build, asserts no media-tool executables are present in the publish output (libVLC is allowed), packs the MSIX, and uploads it as an artifact.

**Tech Stack:** .NET 10 WPF (`net10.0-windows`), WPF-UI 4.3, CommunityToolkit.Mvvm 8.4, LibVLCSharp 3.9.7.1 / VideoLAN.LibVLC.Windows 3.0.23.1, Microsoft.Data.Sqlite, xUnit, PowerShell 7, ffmpeg (dev-only — never shipped), Windows SDK `makeappx`/`signtool`, GitHub Actions (windows-latest).

---

## Conventions (from the runbook — obey exactly)

- **Build/test gate:** `dotnet test VideoShelf.slnx -c Release --nologo -v q` (run from repo root `C:\Agent Projects\VideoShelf`). Current suite: **200 tests (101 Core + 99 App), 0 failures** — never let it regress.
- **`gh` is NOT on PATH:** invoke as `& "C:\Program Files\GitHub CLI\gh.exe"`.
- **Worktrees:** `git worktree add ".worktrees/<branch>" -b "<branch>"`; run `gh pr merge` from the **main repo root**, not the worktree; remove the worktree before `git branch -d`.
- **Direct pushes to `main` are blocked** — every change ships via branch + PR. Merge style `--merge` (no squash).
- **Commit author** `yovanmc` + trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. **No Codex trailer.**
- **Theming rule (caused regressions in a sibling project):** never override/re-base a WPF-UI themed control's Style/ControlTemplate for cosmetics — additive (Opacity/RenderTransform/margins/added elements) only.
- **Safety (standing user discipline):** the harness MUST use an isolated `--data-dir` so it never reads or mutates the user's real VideoShelf library DB. Verify-before-destroy; the sweep is read-only against fixtures.

---

## The harness contract (what the launch hooks mean)

The user named the core three: `--folder`, `--autostart`, `--done-signal`. This plan adds three support flags (justified inline) so a single deterministic sweep can populate and screenshot every view safely. **Unknown args are ignored** (forward-compatible). Boolean flags take no value; the rest are `--key <value>`.

| Flag | Value | Meaning |
|------|-------|---------|
| `--folder` | path | Use this folder as the **single** library source; scan it on startup. (core) |
| `--autostart` | — | Run the scan+load pipeline automatically on startup (no user folder-pick); for `Player`/`PiP` views also begin playback of `--play`. (core) |
| `--done-signal` | path | Write this file once the requested view is fully rendered **and settled** (layout done; for video views, playback reached `Playing` + a short delay so the frame is non-black). The capture script waits on this before screenshotting. (core) |
| `--data-dir` | path | **Safety/isolation.** Use this directory for the SQLite DB + app data so the harness never touches the real library. Omitted ⇒ normal behavior. (support) |
| `--view` | state | Which nav state to show for the shot: `Home`, `Browse`, `SectionDetail`, `RenameTool`, `Player`, `PiP`, `Settings`. Default `Home`. (support) |
| `--play` | path | The clip to load/play for `Player`/`PiP` views. (support) |
| `--seed-demo` | — | After scan, mark one episode watched, set a resume position on one, and add a tag to one section — so Discovery rails / badges render non-empty for the Home shot. Writes only to the isolated `--data-dir` DB. (support) |

When **none** of `--folder`/`--view`/`--done-signal` are present, the app starts normally (no behavior change for real users).

---

## File structure (created / modified)

**Created (app):**
- `src/VideoShelf.App/Harness/HarnessOptions.cs` — pure arg parser (record + `Parse`).
- `src/VideoShelf.App/Harness/HarnessRunner.cs` — UI-thread driver: scan isolated lib → seed → navigate → settle → write signal.
- `tests/VideoShelf.App.Tests/Harness/HarnessOptionsTests.cs` — parser unit tests.

**Modified (app):**
- `src/VideoShelf.App/App.xaml.cs` — parse `e.Args`, build host with optional data-dir, run `HarnessRunner` after `Show()`.
- `src/VideoShelf.App/Services/ServiceCollectionExtensions.cs` — `AddVideoShelf(string? dataDirOverride = null)`.
- `src/VideoShelf.Core/...` (the DB-path source, located in Task A2) — honor the data-dir override.
- `src/VideoShelf.App/VideoShelf.App.csproj` — `<RuntimeIdentifiers>`, `<ApplicationIcon>`.

**Created (harness scripts):**
- `tools/harness/Generate-Fixtures.ps1` — ffmpeg → tiny clip tree.
- `tools/harness/Run-VisualSweep.ps1` — build → per-view launch+GDI-capture → list PNGs.

**Created (packaging):**
- `packaging/AppxManifest.xml` — MSIX manifest.
- `packaging/Assets/` — logo PNGs (Square44x44, Square150x150, Wide310x150, StoreLogo).
- `tools/package/Generate-Assets.ps1` — produces the placeholder logo PNGs (committed output).
- `tools/package/Assert-NoMediaTools.ps1` — fails if a denylisted media tool exe is in a folder.
- `tools/package/Build-Msix.ps1` — publish → stage → makeappx → sign (local).

**Modified (CI / repo):**
- `.github/workflows/ci.yml` — add `package` job.
- `.gitignore` — ignore harness/packaging scratch + `*.msix`/`*.pfx`.
- `ROADMAP.md` — flip M6 → ✅ Merged + decision log (final task, rides this branch).

---

# Workstream A — Launch hooks + harness scripts

### Task A1: `HarnessOptions` pure parser (TDD)

**Files:**
- Create: `src/VideoShelf.App/Harness/HarnessOptions.cs`
- Test: `tests/VideoShelf.App.Tests/Harness/HarnessOptionsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/VideoShelf.App.Tests/Harness/HarnessOptionsTests.cs
using VideoShelf.App.Harness;
using Xunit;

namespace VideoShelf.App.Tests.Harness;

public class HarnessOptionsTests
{
    [Fact]
    public void Parse_Empty_IsNotHarness()
    {
        var o = HarnessOptions.Parse(System.Array.Empty<string>());
        Assert.False(o.IsHarness);
        Assert.Null(o.Folder);
        Assert.Equal("Home", o.View);
        Assert.False(o.AutoStart);
        Assert.False(o.SeedDemo);
    }

    [Fact]
    public void Parse_KeyValuePairs_AreCaptured()
    {
        var o = HarnessOptions.Parse(new[]
        {
            "--folder", @"C:\fix", "--data-dir", @"C:\data",
            "--view", "Player", "--play", @"C:\fix\a.mp4",
            "--done-signal", @"C:\sig.txt"
        });
        Assert.Equal(@"C:\fix", o.Folder);
        Assert.Equal(@"C:\data", o.DataDir);
        Assert.Equal("Player", o.View);
        Assert.Equal(@"C:\fix\a.mp4", o.Play);
        Assert.Equal(@"C:\sig.txt", o.DoneSignal);
        Assert.True(o.IsHarness);
    }

    [Fact]
    public void Parse_BooleanFlags_NeedNoValue()
    {
        var o = HarnessOptions.Parse(new[] { "--autostart", "--seed-demo", "--folder", @"C:\fix" });
        Assert.True(o.AutoStart);
        Assert.True(o.SeedDemo);
        Assert.Equal(@"C:\fix", o.Folder);
    }

    [Fact]
    public void Parse_UnknownArgs_AreIgnored()
    {
        var o = HarnessOptions.Parse(new[] { "--bogus", "x", "--view", "Browse" });
        Assert.Equal("Browse", o.View);
    }

    [Fact]
    public void Parse_FlagsAreCaseInsensitive()
    {
        var o = HarnessOptions.Parse(new[] { "--FOLDER", @"C:\fix", "--AutoStart" });
        Assert.Equal(@"C:\fix", o.Folder);
        Assert.True(o.AutoStart);
    }

    [Fact]
    public void IsHarness_TrueWhenDoneSignalOnly()
    {
        var o = HarnessOptions.Parse(new[] { "--done-signal", @"C:\s.txt" });
        Assert.True(o.IsHarness);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~HarnessOptionsTests"`
Expected: FAIL — `HarnessOptions` does not exist (compile error).

- [ ] **Step 3: Write the parser**

```csharp
// src/VideoShelf.App/Harness/HarnessOptions.cs
using System;
using System.Collections.Generic;

namespace VideoShelf.App.Harness;

/// <summary>
/// Parsed command-line options for the visual-verification harness.
/// Unknown args are ignored so the contract is forward-compatible.
/// </summary>
public sealed record HarnessOptions
{
    public string? Folder { get; init; }
    public string? DataDir { get; init; }
    public bool AutoStart { get; init; }
    public string View { get; init; } = "Home";
    public string? Play { get; init; }
    public string? DoneSignal { get; init; }
    public bool SeedDemo { get; init; }

    /// <summary>True when the app was launched by the harness (any core hook present).</summary>
    public bool IsHarness => Folder is not null || DoneSignal is not null;

    public static HarnessOptions Parse(IReadOnlyList<string> args)
    {
        string? folder = null, dataDir = null, play = null, doneSignal = null;
        string view = "Home";
        bool autoStart = false, seedDemo = false;

        for (var i = 0; i < args.Count; i++)
        {
            var key = args[i].ToLowerInvariant();
            string? Next() => i + 1 < args.Count ? args[++i] : null;

            switch (key)
            {
                case "--folder": folder = Next(); break;
                case "--data-dir": dataDir = Next(); break;
                case "--view": view = Next() ?? view; break;
                case "--play": play = Next(); break;
                case "--done-signal": doneSignal = Next(); break;
                case "--autostart": autoStart = true; break;
                case "--seed-demo": seedDemo = true; break;
                default: break; // ignore unknown
            }
        }

        return new HarnessOptions
        {
            Folder = folder,
            DataDir = dataDir,
            View = view,
            Play = play,
            DoneSignal = doneSignal,
            AutoStart = autoStart,
            SeedDemo = seedDemo,
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~HarnessOptionsTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/VideoShelf.App/Harness/HarnessOptions.cs tests/VideoShelf.App.Tests/Harness/HarnessOptionsTests.cs
git commit -m "feat(app): HarnessOptions command-line parser for the visual harness"
```

---

### Task A2: Thread an optional data-dir override through DI and the DB path

**Goal:** Let the harness point the SQLite DB + app data at a throwaway directory so it never touches the real library.

**Files:**
- Modify: `src/VideoShelf.App/Services/ServiceCollectionExtensions.cs`
- Modify: the Core type that builds the DB path (LOCATE IT — see Step 1).

- [ ] **Step 1: Locate the DB path construction.**

Search for where the SQLite file path / connection string is built. Likely in `src/VideoShelf.Core/Storage/VideoShelfDb.cs` (a constructor taking a path, or a method computing a default under `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` / `ApplicationData`). Run:

Run: `Get-ChildItem -Recurse src\VideoShelf.Core -Filter *.cs | Select-String -Pattern "LocalApplicationData|ApplicationData|\.db|Data Source|VideoShelfDb\(" | Select-Object Path,LineNumber,Line`

Identify (a) the default-path expression and (b) how `AddVideoShelf` constructs/registers `VideoShelfDb`. **If `VideoShelfDb` already takes an explicit path argument from `AddVideoShelf`, you only need to make `AddVideoShelf` accept and forward an override (skip the Core change).** If the default path is hard-coded inside `VideoShelfDb` with no injection seam, add a constructor/factory overload that accepts an explicit directory. **If the wiring differs materially from this description, STOP and report what you found.**

- [ ] **Step 2: Add the override parameter to `AddVideoShelf`.**

Change the extension signature to accept an optional override and forward it to the DB registration. The exact body depends on Step 1; the shape is:

```csharp
// src/VideoShelf.App/Services/ServiceCollectionExtensions.cs
public static IServiceCollection AddVideoShelf(this IServiceCollection services, string? dataDirOverride = null)
{
    // ... existing registrations unchanged ...
    // Where VideoShelfDb is registered, resolve the data directory:
    //   var dataDir = dataDirOverride ?? <existing default expression>;
    //   ...register VideoShelfDb pointed at Path.Combine(dataDir, "videoshelf.db") (match the existing file name)...
    return services;
}
```

Keep the **existing default** when `dataDirOverride` is null — real users see no change. When set, create the directory if missing (`Directory.CreateDirectory(dataDir)`) and place the DB there.

- [ ] **Step 3: Build to verify it compiles.**

Run: `dotnet build VideoShelf.slnx -c Release --nologo -v q`
Expected: Build succeeded, 0 errors. (The default callers — `AddVideoShelf()` with no arg — still compile because the param is optional.)

- [ ] **Step 4: Confirm the full suite still passes.**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: 206 tests (200 prior + 6 new), 0 failures.

- [ ] **Step 5: Commit**

```bash
git add src/VideoShelf.App/Services/ServiceCollectionExtensions.cs src/VideoShelf.Core
git commit -m "feat(app): optional data-dir override so the harness uses an isolated DB"
```

---

### Task A3: `HarnessRunner` — drive the app to a requested view, then signal

**Files:**
- Create: `src/VideoShelf.App/Harness/HarnessRunner.cs`
- Modify: `src/VideoShelf.App/App.xaml.cs`

**Context for the executor — APIs to locate (do NOT guess; read the VMs):**
- Add a source programmatically: find how `SourcesViewModel` adds a folder source (a command/method, or it delegates to a `SourceRepository`/`LibraryRepository.AddSource`). The folder picker path in `MainViewModel`/`SourcesViewModel` shows the call. Use the **non-UI** repository/method path (no folder dialog).
- Scan + reload: `MainViewModel.ScanAndReload()` is the existing `private async Task` behind a `RelayCommand` — invoke the command (`vm.ScanAndReloadCommand.ExecuteAsync(null)` if it's an `AsyncRelayCommand`, else expose/await the method). Confirm the command type by reading `MainViewModel.cs`.
- Navigate: `MainViewModel.CurrentView` setter for `Home`/`Browse`; `OpenSectionAsync(long sectionId)` for `SectionDetail`; `OpenRenameToolAsync(SeriesViewModel series)` for `RenameTool`. To get a section id / series, read the loaded `LibraryViewModel` sections after scan (first section; its first series).
- Player/PiP: read `PlayerViewModel`'s play entry point (how a library row launches playback — Task M3 added "launch-from-library + episode-row Play button"; find the method, e.g. `PlayerViewModel.PlayAsync(path)` or `MainViewModel` routing) and the PiP toggle (`MiniPlayerWindow` open path). Use `--play` as the clip path.
- Seed-demo: `WatchRepository.SetWatched(videoId, true)`, `SetResumePosition(videoId, seconds)`, `TagRepository.AddTag(sectionId, "demo")` — confirm exact method names by reading the repos (the M4/M5 decision-log entries name them).

**If any of these APIs cannot be found or differ from the above, STOP and report — do not invent method names.**

- [ ] **Step 1: Write `HarnessRunner`.**

```csharp
// src/VideoShelf.App/Harness/HarnessRunner.cs
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Harness;

/// <summary>
/// Drives the running app into a deterministic, screenshot-ready state for the
/// visual harness, then writes the done-signal file. UI-thread only.
/// Test-only: gated behind HarnessOptions.IsHarness in App.OnStartup.
/// </summary>
public sealed class HarnessRunner
{
    private readonly MainViewModel _main;
    private readonly HarnessOptions _options;

    public HarnessRunner(MainViewModel main, HarnessOptions options)
    {
        _main = main;
        _options = options;
    }

    public async Task RunAsync()
    {
        try
        {
            // 1. Point the library at the fixture folder and scan it.
            if (_options.Folder is not null)
            {
                await AddSourceAsync(_options.Folder);          // <-- implement against located API
            }
            if (_options.AutoStart || _options.Folder is not null)
            {
                await ScanAndReloadAsync();                      // <-- invoke MainViewModel scan command
            }

            // 2. Optionally seed demo state so Discovery rails / badges render.
            if (_options.SeedDemo)
            {
                await SeedDemoAsync();                           // <-- watched + resume + tag via repos
                await ScanAndReloadAsync();                      // refresh rails after seeding
            }

            // 3. Navigate to the requested view.
            await NavigateAsync(_options.View);

            // 4. Settle: let bindings/layout flush, and for video views wait for a frame.
            await SettleAsync(isVideo: _options.View is "Player" or "PiP");

            // 5. Signal readiness for the capture script.
            WriteDoneSignal($"OK view={_options.View}");
        }
        catch (Exception ex)
        {
            // Never hang the harness — always signal, recording the error.
            WriteDoneSignal("ERROR: " + ex.Message);
        }
    }

    private async Task NavigateAsync(string view)
    {
        switch (view)
        {
            case "Home":
                _main.CurrentView = AppView.Home;
                break;
            case "Browse":
                _main.CurrentView = AppView.Browse;
                break;
            case "Settings":
                // Settings is shown via its existing entry point; if it is a flyout/section,
                // set whatever flag opens it. LOCATE the toggle in MainViewModel/SettingsViewModel.
                ShowSettings();
                break;
            case "SectionDetail":
                {
                    var sectionId = FirstSectionId();
                    await _main.OpenSectionAsync(sectionId);
                    break;
                }
            case "RenameTool":
                {
                    var series = FirstSeries();
                    await _main.OpenRenameToolAsync(series);
                    break;
                }
            case "Player":
                await PlayAsync(_options.Play!, pip: false);
                break;
            case "PiP":
                await PlayAsync(_options.Play!, pip: true);
                break;
            default:
                _main.CurrentView = AppView.Home;
                break;
        }
    }

    private async Task SettleAsync(bool isVideo)
    {
        // Flush bindings + layout at background priority.
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await Task.Delay(isVideo ? 2500 : 700);   // video: allow Playing + decoded frame
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    private void WriteDoneSignal(string message)
    {
        if (_options.DoneSignal is null) return;
        try
        {
            File.WriteAllText(_options.DoneSignal, message + Environment.NewLine);
        }
        catch
        {
            // best-effort; nothing to do if the signal path is unwritable
        }
    }

    // ---- Helpers below are implemented against the LOCATED APIs (see task context). ----
    // Replace each body with the real call; keep the signatures.
    private Task AddSourceAsync(string folder) => /* TODO: located source-add API */ Task.CompletedTask;
    private Task ScanAndReloadAsync() => /* TODO: invoke MainViewModel scan command */ Task.CompletedTask;
    private Task SeedDemoAsync() => /* TODO: watched + resume + tag via repos */ Task.CompletedTask;
    private long FirstSectionId() => /* TODO: first section id from loaded Library */ 0;
    private SeriesViewModel FirstSeries() => /* TODO: first series VM from first section */ throw new NotImplementedException();
    private void ShowSettings() { /* TODO: open settings entry point */ }
    private Task PlayAsync(string clip, bool pip) => /* TODO: launch playback (+ PiP toggle) */ Task.CompletedTask;
}
```

**Implement each `TODO` helper** against the APIs located in the task context. The control flow, settle logic, and signalling above are correct and must not change — only the helper bodies get real calls. Keep the try/catch-then-signal guarantee so the capture script never waits forever.

- [ ] **Step 2: Wire it into `App.xaml.cs`.**

Modify `OnStartup` to parse args, build the host with the data-dir override, and after `window.Show()` kick off the runner (fire-and-forget on the UI dispatcher; do not block startup). Replace the existing `OnStartup` body's host build + show with:

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    var options = VideoShelf.App.Harness.HarnessOptions.Parse(e.Args);

    try
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddVideoShelf(options.DataDir))
            .Build();

        _host.StartAsync().GetAwaiter().GetResult();
        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();

        if (options.IsHarness)
        {
            var main = _host.Services.GetRequiredService<MainViewModel>();
            var runner = new VideoShelf.App.Harness.HarnessRunner(main, options);
            _ = Dispatcher.InvokeAsync(async () => await runner.RunAsync(), DispatcherPriority.Background);
        }
    }
    catch (Exception exception)
    {
        // ... existing catch block unchanged ...
    }
}
```

Confirm `MainViewModel` is registered in DI (it is — `MainWindow` depends on it). Add `using System.Windows.Threading;` if not present. **If `MainViewModel` is not resolvable from the container directly, get it via `window.DataContext` instead.**

- [ ] **Step 3: Build + full suite.**

Run: `dotnet build VideoShelf.slnx -c Release --nologo -v q`
Expected: Build succeeded, 0 errors.

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: 206 tests, 0 failures (no new tests here — `HarnessRunner` is integration-only, exercised by the sweep in Workstream B; this matches the project's "concrete/UI is harness-verified, not unit-tested" pattern).

- [ ] **Step 4: Commit**

```bash
git add src/VideoShelf.App/Harness/HarnessRunner.cs src/VideoShelf.App/App.xaml.cs
git commit -m "feat(app): HarnessRunner drives the app to a view and writes the done-signal"
```

---

### Task A4: Fixture generator script

**Files:**
- Create: `tools/harness/Generate-Fixtures.ps1`

Produces a tiny library tree (two sections, a 3-episode series, two standalones) with ~6-second clips. Uses **ffmpeg** — a dev-time tool present on this machine and on GitHub windows runners — never bundled with the app. Output goes to a caller-supplied dir (the sweep uses a gitignored scratch dir).

- [ ] **Step 1: Write the script.**

```powershell
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
        -f lavfi -i "$Pattern=size=1280x720:rate=24:duration=6" `
        -f lavfi -i "sine=frequency=440:duration=6" `
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
```

- [ ] **Step 2: Smoke-test it.**

Run: `pwsh -File tools/harness/Generate-Fixtures.ps1 -OutDir "$env:TEMP\vs-fixtures" -Force`
Expected: 5 `.mp4` files printed, each non-zero bytes, under `Shows\Big Buck Bunny\` (3) and `Movies\` (2). If ffmpeg is missing, it throws a clear message — install ffmpeg and retry (it's a dev tool only).

- [ ] **Step 3: Commit**

```bash
git add tools/harness/Generate-Fixtures.ps1
git commit -m "test(harness): ffmpeg fixture generator (dev-only, never shipped)"
```

---

### Task A5: Visual-sweep capture script (GDI window grab per view)

**Files:**
- Create: `tools/harness/Run-VisualSweep.ps1`

Mirrors the proven VideoTriage `Drive-AndCapture` pattern: build the Debug app, then for each view launch the app with the right hooks, wait for the done-signal file, GDI-capture the foreground window to a PNG, and kill the process. One launch per view captures real pixels (including live libVLC video for Player/PiP).

- [ ] **Step 1: Write the script.**

```powershell
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
# view name -> extra hook args
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
```

- [ ] **Step 2: Do NOT run yet** — `HarnessRunner`'s helper bodies (Task A3) must be implemented first. This task only commits the script. (It runs in Workstream B.)

- [ ] **Step 3: Commit**

```bash
git add tools/harness/Run-VisualSweep.ps1
git commit -m "test(harness): per-view GDI screenshot sweep script"
```

---

### Task A6: gitignore scratch + packaging artifacts

**Files:**
- Modify: `.gitignore`

- [ ] **Step 1: Append ignore rules.**

Add to `.gitignore` (create the lines if absent):

```gitignore
# Visual harness scratch
/tests/screenshots/
.harness/

# Packaging artifacts
*.msix
*.pfx
/packaging/_staging/
/packaging/_publish/
```

- [ ] **Step 2: Commit**

```bash
git add .gitignore
git commit -m "chore: ignore harness screenshots and packaging artifacts"
```

---

# Workstream B — Retroactive visual sweep + fixes

> This pays the deferred-verification debt (see ROADMAP decision log: "Visual verification DEFERRED to Phase 6"). **Screenshots are read by a Sonnet subagent that returns a TEXT verdict — never load PNGs into the controller context.** Only surface an image to the user if they explicitly ask to see one. Apply the **additive-only theming rule** to every fix.

### Task B1: Capture the full sweep

- [ ] **Step 1: Run the sweep** (after Tasks A1–A5 are implemented and the build is green).

Run: `pwsh -File tools/harness/Run-VisualSweep.ps1`
Expected: a line `PNG_DIR=<...\tests\screenshots\YYYYMMDD-HHmmss>` and 7 PNGs listed: `home.png`, `browse.png`, `section-detail.png`, `rename-tool.png`, `player.png`, `pip.png`, `settings.png`. If a view "never signalled," read the captured `ready.signal` content — `ERROR:` text names the exception; fix the corresponding `HarnessRunner` helper (most likely a located-API mismatch) and re-run.

- [ ] **Step 2: Record the PNG directory path** (you'll hand it to the verification subagent verbatim).

### Task B2: Subagent visual verdict + fixes

- [ ] **Step 1: Dispatch a Sonnet subagent to read the PNGs and return a TEXT verdict.** Give it the absolute `PNG_DIR` and these per-view acceptance criteria. The subagent must `Read` each PNG, then return PASS/FAIL per view + specific observations + the file paths it viewed. It must NOT return the images.

Acceptance criteria:
- **home** — Discovery rails render with cards; at least Continue-watching / Recently-added / For-you / Pick-a-tag rails are present and labelled; seeded demo content makes ≥1 rail non-empty; a watched badge is visible somewhere; no overlapping text, no clipped cards, theme colors consistent (dark WPF-UI surface, readable contrast).
- **browse** — section/series grid renders; natural sort correct (`...E01`,`E02`,`E03` and movies alphabetical); unwatched badges present; missing-file dimming NOT shown (all fixtures exist); search/sort chrome visible and aligned.
- **section-detail** — section header, series card(s), tag editor (pills + add box) visible; the "Rename files…" entry button present on a series card; nothing clipped.
- **rename-tool** — preview table with current→proposed names for the 3-episode series; proposed names follow `"<Base Title> <NN>.ext"`; Apply + Undo controls visible and labelled; status flags column legible.
- **player** — the libVLC video surface shows a **non-black** decoded frame; transport bar (play/pause, seek, time), track/chapter pickers, and overlay controls render over the video without obscuring it; no airspace artifacts (controls not hidden behind the video).
- **pip** — the mini-player window shows a non-black frame + a "Return"/restore affordance; window chrome compact; video not frozen/blank (validates the PiP re-parenting gotcha).
- **settings** — settings surface renders (auto-advance checkbox at minimum); controls aligned, labels readable.

- [ ] **Step 2: Triage the verdict.** For each FAIL, fix the underlying XAML/VM. **Additive-only** for WPF-UI themed controls (no Style/Template re-base). Common likely fixes: spacing/margins, contrast on a custom brush, a binding that left a rail empty, an overlay z-order/airspace issue on the player. If a "FAIL" is actually a harness artifact (e.g., player frame black because settle was too short), fix the harness (lengthen the video settle) rather than the app.
- [ ] **Step 3: Re-run the sweep** (Task B1 Step 1) and re-dispatch the verdict subagent until every view is PASS.

- [ ] **Step 4: Commit any app fixes** (one commit per coherent fix).

```bash
git add <changed view/vm files>
git commit -m "fix(ui): <specific visual defect> found in the M6 retroactive sweep"
```

If the sweep found **zero** defects, record that explicitly (it's a real outcome): commit nothing here and note "M6 sweep: all 7 views PASS, no fixes needed" in the final ROADMAP update.

### Task B3: Optional — keep one reference screenshot set out of git

The screenshots dir is gitignored (Task A6). Do **not** commit PNGs. The verdict + the script are the durable artifacts.

---

# Workstream C — MSIX packaging + CI package job

### Task C1: App csproj — runtime identifier + icon

**Files:**
- Modify: `src/VideoShelf.App/VideoShelf.App.csproj`

- [ ] **Step 1: Add `RuntimeIdentifiers` and an app icon.** Add into the existing `<PropertyGroup>`:

```xml
    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
    <ApplicationIcon>..\..\packaging\Assets\app.ico</ApplicationIcon>
```

(The `.ico` is produced in Task C2. If you implement C2 first, this path will exist at build time.)

- [ ] **Step 2: Build to verify (icon file must exist first — do C2 Step 1 before building this).**

Run: `dotnet build VideoShelf.slnx -c Release --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 3: Commit** (bundle with C2 if you did them together).

```bash
git add src/VideoShelf.App/VideoShelf.App.csproj
git commit -m "build(app): win-x64 RID + application icon for MSIX packaging"
```

### Task C2: AppxManifest + logo assets

**Files:**
- Create: `tools/package/Generate-Assets.ps1`
- Create (committed output): `packaging/Assets/Square44x44Logo.png`, `Square150x150Logo.png`, `Wide310x150Logo.png`, `StoreLogo.png`, `app.ico`
- Create: `packaging/AppxManifest.xml`

- [ ] **Step 1: Write the asset generator** (produces deterministic solid-color "VS" placeholder logos using System.Drawing — no external tool).

```powershell
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
```

- [ ] **Step 2: Run it.**

Run: `pwsh -File tools/package/Generate-Assets.ps1`
Expected: 5 files in `packaging/Assets/` (`Square44x44Logo.png`, `Square150x150Logo.png`, `Wide310x150Logo.png`, `StoreLogo.png`, `app.ico`).

- [ ] **Step 3: Write the manifest.** `Publisher` MUST equal the signing cert subject (`CN=VideoShelf`).

```xml
<!-- packaging/AppxManifest.xml -->
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities">

  <Identity Name="VideoShelf"
            Publisher="CN=VideoShelf"
            Version="1.0.0.0"
            ProcessorArchitecture="x64" />

  <Properties>
    <DisplayName>VideoShelf</DisplayName>
    <PublisherDisplayName>VideoShelf</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.22621.0" />
  </Dependencies>

  <Resources>
    <Resource Language="en-us" />
  </Resources>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>

  <Applications>
    <Application Id="VideoShelf"
                 Executable="VideoShelf.App.exe"
                 EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="VideoShelf"
        Description="A local video library and player."
        BackgroundColor="#202124"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png">
        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />
      </uap:VisualElements>
    </Application>
  </Applications>
</Package>
```

- [ ] **Step 4: Commit**

```bash
git add tools/package/Generate-Assets.ps1 packaging/Assets packaging/AppxManifest.xml
git commit -m "build(package): MSIX manifest + placeholder logo assets"
```

### Task C3: "No media tools bundled" assertion

**Files:**
- Create: `tools/package/Assert-NoMediaTools.ps1`

The spec requires the package contain **no external media tools**. libVLC (`libvlc.dll`, `libvlccore.dll`, the `plugins/`/`libvlc/` tree) is the **bundled player engine and IS allowed** — the assertion targets external transcoder/CLI tools only, by exact executable name.

- [ ] **Step 1: Write the assertion.**

```powershell
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
```

- [ ] **Step 2: Self-test the assertion both ways.**

Run (negative — must PASS / exit 0): create an empty temp dir and assert.
`$d = Join-Path $env:TEMP 'nomedia-ok'; New-Item -ItemType Directory -Force $d | Out-Null; pwsh -File tools/package/Assert-NoMediaTools.ps1 -Path $d; echo "exit=$LASTEXITCODE"`
Expected: `PASS...` and `exit=0`.

Run (positive — must FAIL / exit 1): plant a fake ffmpeg.exe.
`$d2 = Join-Path $env:TEMP 'nomedia-bad'; New-Item -ItemType Directory -Force $d2 | Out-Null; Set-Content (Join-Path $d2 'ffmpeg.exe') 'x'; pwsh -File tools/package/Assert-NoMediaTools.ps1 -Path $d2; echo "exit=$LASTEXITCODE"`
Expected: `FAIL...` and `exit=1`.

- [ ] **Step 3: Commit**

```bash
git add tools/package/Assert-NoMediaTools.ps1
git commit -m "build(package): assert no external media tools are bundled (libVLC allowed)"
```

### Task C4: Local MSIX build script

**Files:**
- Create: `tools/package/Build-Msix.ps1`

Publishes self-contained, stages publish output + manifest + assets, packs with `makeappx`, signs with a local self-signed cert. Used for local verification; CI (Task C5) reuses the same steps inline.

- [ ] **Step 1: Write the script.**

```powershell
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
```

- [ ] **Step 2: Run it locally end-to-end.**

Run: `pwsh -File tools/package/Build-Msix.ps1`
Expected: ends with `MSIX built + signed: ...\VideoShelf.msix`; the media-tool assertion prints PASS; `VideoShelf.msix` exists at repo root. **If publish does not contain libVLC native dlls** (`libvlc.dll`/`libvlccore.dll` and a `libvlc\` or `plugins\` folder), STOP and report — the package would not play video. (Both are pulled in transitively by `VideoLAN.LibVLC.Windows`; they should land in `_publish`.)

- [ ] **Step 3: Commit** (the `.msix`/`.pfx` are gitignored — only the script is committed).

```bash
git add tools/package/Build-Msix.ps1
git commit -m "build(package): local self-contained MSIX build + sign script"
```

### Task C5: CI `package` job

**Files:**
- Modify: `.github/workflows/ci.yml`

Add a second job that runs after `build-and-test`, publishes self-contained, asserts no media tools, packs + signs the MSIX, and uploads it as an artifact.

- [ ] **Step 1: Append the job** (keep the existing `build-and-test` job unchanged; add under `jobs:`).

```yaml
  package:
    needs: build-and-test
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Publish (self-contained win-x64)
        shell: pwsh
        run: >
          dotnet publish src/VideoShelf.App/VideoShelf.App.csproj
          -c Release -r win-x64 --self-contained true
          -p:PublishSingleFile=false -o packaging/_publish --nologo

      - name: Assert no media tools bundled
        shell: pwsh
        run: ./tools/package/Assert-NoMediaTools.ps1 -Path packaging/_publish

      - name: Stage package layout
        shell: pwsh
        run: |
          New-Item -ItemType Directory -Force packaging/_staging | Out-Null
          Copy-Item -Recurse -Force packaging/_publish/* packaging/_staging
          Copy-Item -Force packaging/AppxManifest.xml packaging/_staging
          Copy-Item -Recurse -Force packaging/Assets packaging/_staging/Assets

      - name: Pack + sign MSIX
        shell: pwsh
        run: |
          $sdkBin = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory |
            Where-Object { $_.Name -match '^10\.' } | Sort-Object Name -Descending | Select-Object -First 1
          if (-not $sdkBin) { throw 'Windows SDK bin not found.' }
          $makeappx = Join-Path $sdkBin.FullName 'x64\makeappx.exe'
          $signtool = Join-Path $sdkBin.FullName 'x64\signtool.exe'
          & $makeappx pack /o /d packaging/_staging /p VideoShelf.msix
          if ($LASTEXITCODE -ne 0) { throw 'makeappx failed' }
          $cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=VideoShelf' `
            -KeyUsage DigitalSignature -FriendlyName 'VideoShelf CI' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
          $pwd = ConvertTo-SecureString -String 'videoshelf' -Force -AsPlainText
          Export-PfxCertificate -Cert $cert -FilePath cert.pfx -Password $pwd | Out-Null
          & $signtool sign /fd SHA256 /a /f cert.pfx /p 'videoshelf' VideoShelf.msix
          if ($LASTEXITCODE -ne 0) { throw 'signtool failed' }
          Remove-Item cert.pfx -Force

      - name: Upload MSIX artifact
        uses: actions/upload-artifact@v4
        with:
          name: VideoShelf-msix
          path: VideoShelf.msix
          if-no-files-found: error
```

- [ ] **Step 2: Validate the YAML locally** (parse check).

Run: `pwsh -NoProfile -Command "& { try { $null = (Get-Content .github/workflows/ci.yml -Raw); Write-Host 'read ok' } catch { throw } }"`
(There's no yamllint in-env; rely on CI to validate. The real validation is the PR run in Task D3.)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add package job (MSIX build + assert no media tools + artifact)"
```

---

# Workstream D — Finalize

### Task D1: Full verification gate

- [ ] **Step 1: Run the whole suite.**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: **206 tests, 0 failures** (200 prior + 6 HarnessOptions).

- [ ] **Step 2: Confirm the sweep is green** — every view PASSed in Task B2 (or re-run if any app fix landed after the last sweep).

- [ ] **Step 3: Confirm the MSIX builds locally** (Task C4 Step 2 succeeded and the media-tool assertion PASSed).

### Task D2: Flip ROADMAP to ✅ Merged + decision log

**Files:**
- Modify: `ROADMAP.md`

- [ ] **Step 1: Flip the M6 row** (column order `# | Phase | Status | Plan | PR | Notes`):

```markdown
| 6 | Harness + release polish | ✅ Merged | [M6](docs/superpowers/plans/2026-06-12-videoshelf-harness-release.md) | #<PR> | <one-line shipped summary: launch hooks (--folder/--autostart/--done-signal + --data-dir/--view/--play/--seed-demo), GDI screenshot sweep over all 7 views (retroactive visual debt paid), MSIX package + CI `package` job asserting no media tools (libVLC allowed). NNN tests. Sweep verdict: <result>.> |
```

- [ ] **Step 2: Append a decision-log entry** capturing durable M6 facts: the data-dir override location found in Task A2; the exact source-add / scan / seed APIs used by `HarnessRunner`; whether the player frame captured non-black (PiP re-parenting confirmed); any visual defects found+fixed in the sweep (or "all 7 views PASS, no fixes"); the libVLC-allowed denylist used by the assertion; and that the MSIX is self-signed (`CN=VideoShelf`, CI artifact, not store-submittable).

- [ ] **Step 3: Commit** (rides this branch).

```bash
git add ROADMAP.md
git commit -m "docs: flip M6 (harness + release polish) to Merged"
```

### Task D3: PR → CI watch → merge

- [ ] **Step 1: Push the branch and open the PR.**

```bash
git push -u origin <branch>
& "C:\Program Files\GitHub CLI\gh.exe" pr create --fill --base main
```

- [ ] **Step 2: Watch CI in the foreground** (sleep first to dodge "no checks reported"). **Both** `build-and-test` **and** `package` must be green.

```bash
Start-Sleep -Seconds 20
& "C:\Program Files\GitHub CLI\gh.exe" pr checks <PR#> --watch
```

Expected: all checks pass. If `package` fails on `makeappx`/`signtool` path resolution, read the log — the SDK bin glob may need adjusting for the runner's installed SDK version; fix and push. If it fails on the media-tool assertion, the publish output genuinely contains a denylisted exe — investigate (do NOT weaken the denylist to pass).

- [ ] **Step 3: Merge from the main repo root** (not the worktree) and clean up.

```bash
& "C:\Program Files\GitHub CLI\gh.exe" pr merge <PR#> --merge --delete-branch
git checkout main && git pull
git worktree remove .worktrees/<branch>
```

- [ ] **Step 4: Update the ROADMAP PR number** if you used a placeholder (`#<PR>` → actual). If the number was unknown at Task D2, do a tiny follow-up commit on main is blocked — instead edit before merge, or accept the placeholder and note the PR link is in the merge. Prefer: set the real number in Task D2 by creating the PR first (Step 1) then editing ROADMAP.md on the branch and pushing before merge.

---

## Self-review (author's pre-handoff check)

- **Spec coverage:** Fixtures (A4) ✓ · launch hooks `--folder`/`--autostart`/`--done-signal` (A1/A3) ✓ · screenshot harness (A5/B) ✓ · retroactive sweep of shell/browse/player/PiP/discovery/section-detail/rename (B2 criteria cover all seven) ✓ · MSIX packaging (C1–C4) ✓ · CI package job asserting no media tools (C3/C5) ✓.
- **Likely mismatch points flagged for STOP-and-report:** data-dir wiring (A2), source-add/scan/seed/play APIs (A3) — these are described by behavior because the digest didn't capture their verbatim signatures; the executor must read the VMs/repos and stop if they differ.
- **Theming/safety rules carried in:** additive-only fixes (B2), isolated `--data-dir` so the real library is never touched (A3/A5), libVLC explicitly allowed by the media-tool assertion (C3).
- **Test count math:** 200 → 206 (only HarnessOptions adds unit tests; everything else is harness/CI-verified per the project's established "concrete + UI is integration-verified" pattern).
