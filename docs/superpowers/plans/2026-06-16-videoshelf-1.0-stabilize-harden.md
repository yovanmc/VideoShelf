# VideoShelf M25 — "1.0: Stabilize & Harden" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Written for Sonnet execution. If something in the codebase does not match what this plan describes (a method signature, a file path, a table name), STOP and report rather than guess.** This is a *stabilization* milestone: several tasks are "audit → fix what you find → prove it with a test." For those, READ the named file(s) first, work the checklist, and for every defect found follow the TDD loop (failing test → fix → passing test → commit). If a checklist item is already correct, note it in the PR body and move on — do not invent a fix.

**Goal:** Take VideoShelf to a confident, feature-frozen **1.0**: a real-app crash/invisible-feature sweep + a global exception net, a destructive-path safety audit with tests, fail-path hardening, the project's first guarded schema-version migration to drop dead tables, and release prep (version 1.0.0 + CHANGELOG + README + signed-MSIX CI verify).

**Architecture:** App-layer + Core read/maintenance-path only. **One deliberate exception:** Group D introduces VideoShelf's first `PRAGMA user_version` migration runner (breaks the M8→M24 no-runner streak, by owner decision). Delivered as **5 stacked PRs at the GROUP seams (A→B→C→D→E), Group A first** (the verification backbone), mirroring the M16–M24 model. The ROADMAP flip rides the final (Group E) PR.

**Tech Stack:** .NET 10 WPF + WPF-UI + LibVLCSharp + `Microsoft.Data.Sqlite`. Tests: xUnit in `tests/VideoShelf.Core.Tests` + `tests/VideoShelf.App.Tests`. Solution: `VideoShelf.slnx`.

**Invariants (hold throughout — a violation is a STOP-and-report):**
- No `ui:*` control retemplate; **no palette/`DesignTokens` brush change** (the M24 black-glass Ice-Cyan theme stays exactly as-is).
- No `AutomationProperties`/screen-reader/`LiveRegion`/`--a11y-dump` reintroduced (PR #77 stands).
- **Library files are never written** (mutations are DB-only + the `%LOCALAPPDATA%\VideoShelf\covers\` art dir). Group B adds a test that *proves* this.
- Additive-only EXCEPT Group D's single guarded migration (which only DROPs tables verified to have zero readers).

**Conventions (from `docs/superpowers/WORKFLOW-execution.md`):**
- `gh` is not on PATH → `& "C:\Program Files\GitHub CLI\gh.exe"`.
- Test gate: `dotnet test VideoShelf.slnx -c Release --nologo -v q`.
- Work in a worktree under `.worktrees/`; **`gh pr merge` from the main repo root**, not the worktree. Direct pushes to `main` are blocked — every change ships via branch + PR.
- Commits: author `yovanmc` + `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. No Codex trailer. Merge `--merge` (no squash).
- Build quiet: `dotnet build -v minimal`. Don't enumerate the tree without excludes.
- **Baseline test count: ~890 (from M24).** Re-confirm by running the gate at the start of Group A; record the real number in the Group A PR body and treat THAT as the baseline for "+N tests" in later groups.

---

## File Structure

**Group A — real-app sweep + global handler**
- Modify: `src/VideoShelf.App/App.xaml.cs` (add `DispatcherUnhandledException` + `AppDomain.CurrentDomain.UnhandledException` handlers)
- Create: `tests/VideoShelf.App.Tests/CrashHandlerTests.cs` (unit-test the pure log-formatting/handler-decision helper)
- Create (if helper extraction needed): `src/VideoShelf.App/Diagnostics/CrashReporter.cs` (pure formatter + handler glue)
- Modify: `tools/sweep/Run-VisualSweep.ps1` (ensure every current view+player sub-state is enumerated; add any missing M24 views: `Insights`)
- (No production behavior change beyond the handlers; this group is mostly *find + fix* whatever the sweep surfaces.)

**Group B — destructive-path safety audit + tests**
- Test: `tests/VideoShelf.App.Tests/Safety/RecycleBinKeeperGateTests.cs`
- Test: `tests/VideoShelf.Core.Tests/Renaming/RenameExecutorCrashSafetyTests.cs`
- Test: `tests/VideoShelf.App.Tests/Safety/RemoveSourceUndoTests.cs`
- Test: `tests/VideoShelf.App.Tests/Safety/FramePickerWriteScopeTests.cs`
- Test: `tests/VideoShelf.Core.Tests/Safety/LibraryNeverWrittenAuditTests.cs` (source-grep assertion)
- Modify (only if the audit finds a missing guard): the relevant service/VM.

**Group C — fail-path hardening**
- Test: `tests/VideoShelf.App.Tests/FailPaths/PlayerErrorSurfaceTests.cs`
- Test: `tests/VideoShelf.App.Tests/FailPaths/ThumbnailLoaderFallbackTests.cs`
- Test: `tests/VideoShelf.Core.Tests/FailPaths/MissingLockedFileTests.cs`
- Test: `tests/VideoShelf.Core.Tests/FailPaths/EmptyAndCorruptDbTests.cs`
- Modify: whichever paths the audit shows are not fail-safe (player error state, IO catch sites, `PooledBitmapLoader` fallback).

**Group D — first schema-version migration (orphan cleanup)**
- Modify: `src/VideoShelf.Core/Storage/VideoShelfDb.cs` (add `RunVersionedMigrations(conn)` reading/writing `PRAGMA user_version`, called at the end of `Migrate()`)
- Modify: `src/VideoShelf.Core/.../LibraryRepository.cs` (delete the dead `SELECT * FROM smart_views` reader)
- Test: `tests/VideoShelf.Core.Tests/Storage/UserVersionMigrationTests.cs`

**Group E — release prep + close**
- Modify: `src/VideoShelf.App/VideoShelf.App.csproj` (add `<Version>1.0.0</Version>` + `<FileVersion>`/`<AssemblyVersion>`)
- Modify: `.github/workflows/ci.yml` (confirm the `package` job version source; wire the manifest `Identity Version` from the csproj version if it is currently hardcoded)
- Create: `CHANGELOG.md` (repo root)
- Modify/Create: `README.md` (repo root)
- Modify: `ROADMAP.md` (flip M25 → ✅ Merged + decision-log entry + mark v5 COMPLETE / 1.0)
- Modify: `tools/sweep/Run-VisualSweep.ps1` only if a new harness state is needed (none expected — owner verifies the 2 manual checks live).

---

## GROUP A — Real-app crash & invisible-feature sweep + global exception net

> **PR #1 of 5.** Goal: prove the app launches and renders *every* surface on a populated library with zero crashes / zero silently-Collapsed features, and add the global runtime-exception safety net that today's startup-only guard lacks. This group is FIRST because render-crash / invisible-feature bugs are this project's recurring failure mode and are invisible to build + unit tests (M17 crash-on-launch, M18 invisible feature, M21 toast render-crash, M22 creator-page render-crash were all caught only here).

### Task A1: Confirm the baseline + enumerate every sweep surface

**Files:**
- Read: `tools/sweep/Run-VisualSweep.ps1`
- Read: `src/VideoShelf.App/` for the `AppView` enum (search `enum AppView`) and the harness `--view` switch (search `HarnessRunner` / `--view`).

- [ ] **Step 1: Run the test gate to record the real baseline.**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: all green. **Record the exact total** (e.g. "890 tests") — this is the baseline for the whole milestone.

- [ ] **Step 2: Diff the `AppView` enum against the sweep's view list.**

Read the `AppView` enum and the `$views`/`$states` array in `Run-VisualSweep.ps1`. Build a checklist of EVERY enum member + every player sub-state (`Player`, `PiP`, `PlayerQueue`, `PlayerMore`, `PlayerTracks`, `PlayerVolume`, `PlayerSpeed`, `PlayerAbRepeat`, `PlayerSkipFeedback`, `PlayerUpNext`) + Browse variants (`Browse`, `BrowseSelection`, `BrowseFilter`) + `Maintenance`, `DuplicateResolve`, `SectionEditMode`, `Insights`, `Toast`. **If the sweep is missing any member that still exists (e.g. `Insights` added in M24), add it to the sweep array.** If the sweep still lists a view CUT in M24 (Smart Views builder/page, command-palette state), remove it.

- [ ] **Step 3: Commit the sweep enumeration fix (if any).**

```
git add tools/sweep/Run-VisualSweep.ps1
git commit -m "chore(sweep): enumerate all current views (add Insights, drop M24-cut surfaces)"
```
If no change was needed, skip the commit and note "sweep already complete" in the PR body.

### Task A2: Run the populated-library sweep and triage

**Files:**
- Use: `tools/sweep/Run-VisualSweep.ps1`, the harness flags (`--seed-demo`, `--folder <real-clips>`, `--view`, `--done-signal`).

- [ ] **Step 1: Run the sweep on a populated library.**

Run the sweep with seed-demo AND a small real-clip folder so libVLC actually probes frames (so video cards show real thumbnails, not just the placeholder — invisible-thumbnail bugs only show with real frames). Use the existing ffmpeg fixture folder if one exists (search `tests/` for generated clips / `Generate-Fixtures.ps1`); otherwise `--seed-demo` alone is acceptable but note the limitation. Output lands in `tests/screenshots/<stamp>/`.

- [ ] **Step 2: Dispatch a Sonnet subagent to read the PNGs and return a per-screen TEXT verdict.**

Do **not** load the PNGs into the controller. Dispatch a subagent: "Read every PNG in `tests/screenshots/<stamp>/`. For each, return PASS/FAIL + one line: does the surface render its intended content (not blank/stacked/clipped), is any expected element missing or `Collapsed`, any obvious black-glass-theme contrast break (text invisible on `#070707`/`#141414`)? Report the absolute paths you viewed." Act on the text verdict.

- [ ] **Step 3: Build the defect list.**

From the verdict, list every FAIL as a concrete defect (screen + symptom). Each becomes a fix task below (A3 pattern). If ALL screens PASS, record that and skip to A4 (still ship the global handler).

### Task A3 (repeat per defect found): Fix one render/invisible-feature defect (TDD)

> Repeat this task for each FAIL from A2. For a render *crash* (app dies launching a `--view`), the "test" is the real-app launch itself; for an *invisible feature* (a converter/binding wrong), add a unit test that pins the converter/VM output. Use the project's known patterns: a Visibility converter must match the bound value's TYPE (M18 lesson); `ScrollMemory.ViewKey` only accepts `AppView` members (M22 lesson); `KeyTime` is not a `Duration` (M21 lesson); never alias a WPF-UI brush via nested `<StaticResource>` (VideoShelf I-group lesson).

**Files:**
- Read: the view/VM/converter named in the defect.
- Test: the matching `tests/VideoShelf.App.Tests/...` file.

- [ ] **Step 1: Write a failing test that pins the correct behavior** (e.g. for an invisible feature: assert the VM property the binding reads is non-null/visible under the seeded condition; for a converter-type mismatch: assert the converter returns `Visible` for the real bound value type).

- [ ] **Step 2: Run it — verify it FAILS** for the right reason.

Run: `dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q --filter <TestName>`

- [ ] **Step 3: Apply the minimal fix** in the view/VM/converter.

- [ ] **Step 4: Run the test — verify PASS.** Then re-run the affected `--view <X>` via the harness `--done-signal` to confirm the real app no longer crashes / now shows the feature.

- [ ] **Step 5: Commit.**

```
git commit -am "fix(<area>): <symptom> on <screen> (sweep-caught)"
```

### Task A4: Add the global exception net

**Files:**
- Read: `src/VideoShelf.App/App.xaml.cs` (the `OnStartup` try/catch is the only guard today).
- Create: `src/VideoShelf.App/Diagnostics/CrashReporter.cs`
- Modify: `src/VideoShelf.App/App.xaml.cs`
- Test: `tests/VideoShelf.App.Tests/CrashHandlerTests.cs`

- [ ] **Step 1: Write the failing test for the pure formatter.**

```csharp
using VideoShelf.App.Diagnostics;
using Xunit;

public class CrashHandlerTests
{
    [Fact]
    public void FormatReport_IncludesExceptionTypeAndMessage()
    {
        var ex = new InvalidOperationException("boom");
        string report = CrashReporter.FormatReport("UI thread", ex);
        Assert.Contains("UI thread", report);
        Assert.Contains("InvalidOperationException", report);
        Assert.Contains("boom", report);
    }

    [Fact]
    public void FormatReport_NullException_DoesNotThrow()
    {
        string report = CrashReporter.FormatReport("AppDomain", null);
        Assert.Contains("AppDomain", report);
        Assert.Contains("Unknown error", report);
    }
}
```

- [ ] **Step 2: Run it — verify it fails** (`CrashReporter` undefined).

Run: `dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q --filter CrashHandlerTests`
Expected: FAIL (type not found).

- [ ] **Step 3: Implement `CrashReporter`.**

```csharp
using System;
using System.IO;
using System.Text;

namespace VideoShelf.App.Diagnostics;

/// <summary>
/// Pure crash-report formatting + best-effort persistence. The WPF handlers
/// (App.xaml.cs) call FormatReport for the dialog text and WriteToDisk for a log.
/// </summary>
public static class CrashReporter
{
    public static string FormatReport(string source, Exception? ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"VideoShelf unexpected error ({source}).");
        if (ex is null)
        {
            sb.AppendLine("Unknown error (no exception object).");
            return sb.ToString();
        }
        sb.AppendLine($"{ex.GetType().Name}: {ex.Message}");
        sb.AppendLine(ex.StackTrace ?? "(no stack trace)");
        return sb.ToString();
    }

    /// <summary>Best-effort: write the report under %LOCALAPPDATA%\VideoShelf\logs\. Never throws.</summary>
    public static void WriteToDisk(string dataDir, string report)
    {
        try
        {
            var dir = Path.Combine(dataDir, "logs");
            Directory.CreateDirectory(dir);
            // Stable-ish unique name without Date.Now in tests: caller passes a stamp via report content.
            var file = Path.Combine(dir, $"crash-{Guid.NewGuid():N}.log");
            File.WriteAllText(file, report);
        }
        catch { /* logging must never crash the crash handler */ }
    }
}
```

- [ ] **Step 4: Run the test — verify PASS.**

- [ ] **Step 5: Wire the handlers in `App.xaml.cs`.**

In `OnStartup` (after `base.OnStartup(e)`, BEFORE building the host), subscribe both handlers. Match the existing data-dir resolution the app already uses (search for `LOCALAPPDATA`/`VideoShelf` in App.xaml.cs and reuse that path; if a `--data-dir` override exists, use it).

```csharp
// in OnStartup, after base.OnStartup(e):
DispatcherUnhandledException += (s, args) =>
{
    var report = Diagnostics.CrashReporter.FormatReport("UI thread", args.Exception);
    Diagnostics.CrashReporter.WriteToDisk(ResolveDataDir(), report);
    System.Windows.MessageBox.Show(report, "VideoShelf — unexpected error",
        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    args.Handled = true; // keep the app alive after a non-fatal UI-thread exception
};

AppDomain.CurrentDomain.UnhandledException += (s, args) =>
{
    var report = Diagnostics.CrashReporter.FormatReport("AppDomain", args.ExceptionObject as Exception);
    Diagnostics.CrashReporter.WriteToDisk(ResolveDataDir(), report);
    // AppDomain exceptions are terminal; we can only log + show, not recover.
};
```

`ResolveDataDir()` must return the SAME directory the rest of the app uses (reuse the existing helper if present; otherwise `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` + `"VideoShelf"`). **If the app already resolves a data dir in a service, call that instead of duplicating the logic — STOP and report if it is not obvious where.**

- [ ] **Step 6: Build + launch smoke.** `dotnet build -v minimal`, then launch via `--view Home --done-signal <tmp>` and confirm it still starts cleanly. Verify a crash log directory is created if you temporarily throw (optional manual check; do not leave the throw in).

- [ ] **Step 7: Commit.**

```
git commit -am "feat(app): global UI-thread + AppDomain exception net with logged crash report"
```

### Task A5: Group A green gate + open PR

- [ ] **Step 1: Full gate.** `dotnet test VideoShelf.slnx -c Release --nologo -v q` — all green; record the new count.
- [ ] **Step 2: Re-run the sweep; subagent verdict PASS on every screen.**
- [ ] **Step 3: Push branch, open PR** (`& "C:\Program Files\GitHub CLI\gh.exe" pr create`), body = baseline count, defects found+fixed (or "all PASS"), the global-handler addition. **Sleep ~20s then** `gh pr checks <PR#> --watch` (foreground). Merge `--merge --delete-branch` from the repo root, sync `main`.

---

## GROUP B — Destructive-path safety audit + tests

> **PR #2 of 5.** Goal: for each of the 4 disk/DB-mutating paths, confirm the safety guard exists and add a test that *proves* it; close any gap. Plus a capstone test that no path writes into a library folder. (No behavior change expected unless the audit finds a missing guard.)

### Task B1: Recycle-Bin keeper-gate test

**Files:**
- Read: `src/VideoShelf.App/Services/IRecycleBinService.cs` + the M18 duplicate-resolve VM that calls it (search `SendToRecycleBin`).
- Test: `tests/VideoShelf.App.Tests/Safety/RecycleBinKeeperGateTests.cs`

- [ ] **Step 1: Read the keeper flow.** Confirm the call site checks the KEEPER exists & is non-zero BEFORE recycling the loser. Identify the smallest unit that encodes that decision (a method on the VM or a helper). If the check is inline and untestable, extract a pure `static bool CanRecycleLoser(FileInfoLike keeper, FileInfoLike loser)` helper.

- [ ] **Step 2: Write the failing test.**

```csharp
// Asserts the loser is NEVER recycled unless the keeper exists and is non-empty.
[Fact]
public void Recycle_Refused_WhenKeeperMissing() { /* keeper path absent → CanRecycleLoser false */ }

[Fact]
public void Recycle_Refused_WhenKeeperZeroBytes() { /* keeper exists, 0 bytes → false */ }

[Fact]
public void Recycle_Allowed_WhenKeeperPresentAndNonEmpty() { /* keeper ok → true */ }
```
Fill in with the real helper signature you found/extracted. Use a fake recycle service that RECORDS calls and a temp-dir for real `FileInfo`.

- [ ] **Step 2b: Run — verify it fails** (helper missing, or the gate is absent → the "refused" tests fail). If the gate is ALREADY correct and you only added tests, they may pass immediately — that is acceptable for a safety-pinning test; note it.

- [ ] **Step 3: Add the gate** if missing; otherwise no production change.

- [ ] **Step 4: Run — verify PASS.**

- [ ] **Step 5: Commit.** `git commit -am "test(safety): pin Recycle-Bin keeper-exists gate"` (or `fix(safety):` if a gate was added).

### Task B2: Rename crash-mid-apply resumability test

**Files:**
- Read: `src/VideoShelf.Core/Renaming/RenameExecutor.cs` (`Apply`, `Undo`, the manifest write at the top).
- Test: `tests/VideoShelf.Core.Tests/Renaming/RenameExecutorCrashSafetyTests.cs`

- [ ] **Step 1: Write a failing test that simulates a crash between manifest-write and the Move**, then proves `Undo(manifestPath)` restores all already-moved files and tolerates the not-yet-moved ones.

Use the existing `IFileSystem` seam — inject a fake that throws on the Nth `Move` to simulate a mid-batch crash, then run `Undo` against the manifest and assert every successfully-moved file is back at its original path and no data is lost.

```csharp
[Fact]
public void Apply_CrashesMidBatch_Undo_RestoresAllMovedFiles()
{
    // arrange a 3-item plan; fake fs throws on the 2nd Move
    // act: Apply throws; then Undo(manifestPath)
    // assert: item 1 restored to original; items 2,3 untouched at original; no overwrite, no loss
}
```

- [ ] **Step 2: Run — verify it fails** if there is a gap; if the M5 safety already holds, it passes (pin it). 
- [ ] **Step 3: Fix only if a gap is found** (manifest must be flushed before the first Move; Undo must be tolerant).
- [ ] **Step 4: Run — PASS.**
- [ ] **Step 5: Commit.** `git commit -am "test(safety): rename crash-mid-apply resumes via manifest Undo"`.

### Task B3: Remove-source undo test

**Files:**
- Read: the remove-source command (search `RemoveSource` / `UpsertSource` / the `IConfirmService` gate).
- Test: `tests/VideoShelf.App.Tests/Safety/RemoveSourceUndoTests.cs`

- [ ] **Step 1: Write a failing test:** removing a source then invoking Undo re-adds the same source (idempotent `UpsertSource`) and a rescan restores its rows; assert **no disk delete ever occurs** (the fake fs records zero deletes).
- [ ] **Step 2: Run — verify fail/pin.**
- [ ] **Step 3: Fix only if a gap.**
- [ ] **Step 4: PASS.**
- [ ] **Step 5: Commit.** `git commit -am "test(safety): remove-source is DB-only + undo re-adds idempotently"`.

### Task B4: Frame-picker write-scope test

**Files:**
- Read: `src/VideoShelf.App/ViewModels/CreatorFramePickerViewModel.cs` + `IThumbnailSnapshotter` (search `TrySnapshotAtAsync`) + where it composes the `%LOCALAPPDATA%\VideoShelf\covers\` path.
- Test: `tests/VideoShelf.App.Tests/Safety/FramePickerWriteScopeTests.cs`

- [ ] **Step 1: Extract (if needed) a pure path composer** `static string ComposeCoverPath(string dataDir, long sectionId, Guid id)` returning `<dataDir>/covers/creator_<sectionId>_<id:N>.png`, so the destination is unit-testable.
- [ ] **Step 2: Write the failing test:** the composed cover path is always under `<dataDir>/covers/` and never equals or sits under any source/library root; the source video is opened read-only (the snapshotter must not request write/modify). 
```csharp
[Fact]
public void CoverPath_IsUnderDataDir_Covers() { /* StartsWith(Path.Combine(dataDir,"covers")) */ }
[Fact]
public void CoverPath_NeverUnderLibraryRoot() { /* given a libraryRoot, assert !cover.StartsWith(libraryRoot) */ }
```
- [ ] **Step 3: Run — fail; implement the composer/guard; PASS.**
- [ ] **Step 4: Commit.** `git commit -am "test(safety): creator-frame cover writes stay under app data dir, never library"`.

### Task B5: "Library never written" capstone audit test

**Files:**
- Test: `tests/VideoShelf.Core.Tests/Safety/LibraryNeverWrittenAuditTests.cs`

- [ ] **Step 1: Write a source-grep audit test** that scans the `src/` tree for file-mutating APIs (`File.Delete`, `File.Move`, `File.WriteAllText`, `File.Create`, `Directory.Delete`, `FileStream(... FileMode.Create/Open with Write)`, `SendToRecycleBin`) and asserts each hit is in an ALLOWLISTED file (rename executor, recycle-bin service, frame-picker/covers writer, crash logger, settings/db file, harness). Any new unlisted hit fails the test → forces a human to classify it.

```csharp
[Fact]
public void NoUnreviewedFileMutationOutsideAllowlist()
{
    var srcRoot = TestPaths.RepoSrc(); // resolve ../../src from the test assembly
    var allow = new[] {
        "RenameExecutor.cs", "RecycleBinService.cs", "ThumbnailSnapshotter", "CrashReporter.cs",
        "VideoShelfDb.cs", "SettingsRepository.cs", "HarnessRunner.cs", "PooledBitmapLoader.cs",
        // add the exact filenames you find during this task; each must be a justified writer
    };
    var mutators = new[] { "File.Delete(", "File.Move(", "File.WriteAllText(", "File.Create(",
        "Directory.Delete(", "SendToRecycleBin", "FileMode.Create", "FileMode.OpenOrCreate" };
    var offenders = /* enumerate *.cs under srcRoot, find any mutator hit whose filename is not in allow */;
    Assert.True(offenders.Count == 0,
        "Unreviewed file-mutating call(s) outside the allowlist:\n" + string.Join("\n", offenders));
}
```
Resolve `RepoSrc()` from the test assembly location (walk up to the repo root, then `src`). **Populate the allowlist with the ACTUAL writer files you discover** — if you find a writer that should NOT exist (writes into a source folder), STOP and report; do not allowlist it.

- [ ] **Step 2: Run — it will fail listing every current writer; classify each, add the justified ones to the allowlist, fix/report any illegitimate one.**
- [ ] **Step 3: Run — PASS.**
- [ ] **Step 4: Commit.** `git commit -am "test(safety): audit gate — no file mutation outside the reviewed allowlist"`.

### Task B6: Group B gate + PR

- [ ] Full gate green; push; PR body lists each path + "guard already held / guard added"; foreground CI watch; merge `--merge --delete-branch`; sync main.

---

## GROUP C — Fail-path hardening

> **PR #3 of 5.** Goal: every unhappy path is fail-safe AND surfaced — never a silent black screen, silent hang, or crash.

### Task C1: Player surfaces a libVLC load/probe error

**Files:**
- Read: `src/VideoShelf.App/.../LibVlcPlaybackEngine.cs` (`Load` catches → raises `EncounteredError`) + `PlayerViewModel` (does it subscribe `EncounteredError` and show a visible error state?).
- Test: `tests/VideoShelf.App.Tests/FailPaths/PlayerErrorSurfaceTests.cs`

- [ ] **Step 1: Write a failing test** with `FakePlaybackEngine` raising `EncounteredError`: assert `PlayerViewModel.HasError == true` and a user-facing `ErrorMessage` is set (and the spinner/loading state clears). If `HasError` already exists (M3 wired an engine-error guard), assert the message is non-empty + the view's error element is not `Collapsed`.
- [ ] **Step 2: Run — fail (or pin).**
- [ ] **Step 3: Wire the error surface** if missing (subscribe `EncounteredError` → set `HasError`/`ErrorMessage` on the dispatcher; ensure `PlayerView` has a visible error panel bound to `HasError`).
- [ ] **Step 4: PASS** + a `--view Player --play <nonexistent.mp4> --done-signal` smoke confirming it does not hang or show a permanent black frame.
- [ ] **Step 5: Commit.** `git commit -am "fix(player): surface libVLC load/probe errors as a visible error state"`.

### Task C2: Thumbnail loader falls back, never throws

**Files:**
- Read: `src/VideoShelf.App/.../PooledBitmapLoader.cs` / `IImageLoader`.
- Test: `tests/VideoShelf.App.Tests/FailPaths/ThumbnailLoaderFallbackTests.cs`

- [ ] **Step 1: Failing test:** loading a corrupt/zero-byte/missing image path returns null/placeholder and does not throw; an oversized/garbage file is handled. Use temp files (0-byte, random-bytes, missing path).
- [ ] **Step 2: Run — fail/pin.**
- [ ] **Step 3: Wrap the decode in try/catch → return null (caller already shows the neutral glyph).**
- [ ] **Step 4: PASS.**
- [ ] **Step 5: Commit.** `git commit -am "fix(images): bitmap loader fails safe to placeholder on corrupt/missing files"`.

### Task C3: Missing/locked file on scan/relink/rename/recycle

**Files:**
- Read: `FolderScanner` / `ScanService`, `LibraryRepository.RelinkVideo`, `RenameExecutor`, `RecycleBinService`.
- Test: `tests/VideoShelf.Core.Tests/FailPaths/MissingLockedFileTests.cs`

- [ ] **Step 1: Failing tests:** (a) a scan over a folder containing a path that throws `IOException`/`UnauthorizedAccessException` on access skips that entry and continues (no abort); (b) recycle of a locked file returns false (already try/catch in the service — pin it) and the keeper is untouched; (c) rename where the target became occupied between plan and apply is reported as a conflict, not an overwrite (M5 re-verify — pin it).
- [ ] **Step 2: Run — fail/pin.**
- [ ] **Step 3: Add the missing catch/continue** only where a gap exists.
- [ ] **Step 4: PASS.**
- [ ] **Step 5: Commit.** `git commit -am "fix(scan/relink): skip-and-continue on inaccessible files; pin rename/recycle guards"`.

### Task C4: Empty library + corrupt/locked DB survive

**Files:**
- Read: `VideoShelfDb` open path, the data-dir resolution, the empty-library CTA (`IsLibraryEmpty`).
- Test: `tests/VideoShelf.Core.Tests/FailPaths/EmptyAndCorruptDbTests.cs`

- [ ] **Step 1: Failing tests:** (a) opening a brand-new empty DB then querying every read repo returns empty collections, never throws; (b) `Migrate()` is idempotent across two consecutive opens of the same file (already a property — pin it); (c) a WAL-busy/locked second connection retries or surfaces gracefully (assert no unhandled throw escapes the read repo — wrap with the existing connection settings; if `busy_timeout` is unset, set a small one).
- [ ] **Step 2: Run — fail/pin.**
- [ ] **Step 3: Add `PRAGMA busy_timeout` / graceful empty handling** only if a gap exists.
- [ ] **Step 4: PASS.**
- [ ] **Step 5: Commit.** `git commit -am "fix(storage): empty-library + busy-DB read paths fail safe"`.

### Task C5: Group C gate + PR

- [ ] Full gate green; push; PR; foreground CI watch; merge; sync main.

---

## GROUP D — First schema-version migration (orphan cleanup)

> **PR #4 of 5. The one non-additive change in M25.** Introduce a guarded `PRAGMA user_version` runner and DROP only tables verified to have ZERO readers. `smart_views` is the confirmed M24 orphan (Smart Views cut end-to-end). **Verify-before-destroy is mandatory: before dropping any table, grep `src/` for its name and confirm zero non-schema, non-test readers. `playlists`/`playlist_items`/`video_art`/`series_art`/`dismissed_duplicates`/`creator_art`/`section_tags`/`series_tags`/`video_tags`/`grouping_overrides` are ACTIVE → never drop.**

### Task D1: Verify the orphan set

**Files:**
- Read: `src/VideoShelf.Core/Storage/VideoShelfDb.cs` (the `Schema` constant — the exact `CREATE TABLE` list) + grep each candidate.

- [ ] **Step 1: For `smart_views`, grep `src/` (exclude `*.Tests`).** Expected readers: only the `Schema` constant + the dead `SELECT * FROM smart_views` in `LibraryRepository`. **If any LIVE feature still reads it, STOP and report — do not drop.**
- [ ] **Step 2: Scan the full `CREATE TABLE` list for any OTHER table with zero live readers** (e.g. a remnant of a cut feature). For each zero-reader candidate, confirm with a grep. Build the final DROP set (likely just `smart_views`). Record the set + the grep evidence in the PR body.

### Task D2: Delete the dead `smart_views` reader

**Files:**
- Modify: `src/VideoShelf.Core/.../LibraryRepository.cs`

- [ ] **Step 1: Remove the dead `SELECT * FROM smart_views` method/usage.** Confirm nothing calls it (grep its method name). Build.
- [ ] **Step 2: Commit.** `git commit -am "chore(core): remove dead smart_views reader (feature cut in M24)"`.

### Task D3: Add the `user_version` migration runner (TDD)

**Files:**
- Modify: `src/VideoShelf.Core/Storage/VideoShelfDb.cs`
- Test: `tests/VideoShelf.Core.Tests/Storage/UserVersionMigrationTests.cs`

- [ ] **Step 1: Write the failing tests.**

```csharp
public class UserVersionMigrationTests
{
    [Fact]
    public void Migrate_DropsSmartViews_AndSetsUserVersion()
    {
        using var db = VideoShelfDb.OpenTemp(); // use the project's existing temp-open test helper
        db.Migrate();
        Assert.False(TableExists(db, "smart_views"));
        Assert.True(UserVersion(db) >= 1);
        // active tables still present:
        Assert.True(TableExists(db, "playlists"));
        Assert.True(TableExists(db, "video_art"));
        Assert.True(TableExists(db, "dismissed_duplicates"));
    }

    [Fact]
    public void Migrate_IsIdempotent_AcrossReopen()
    {
        var path = NewTempDbPath();
        using (var db = VideoShelfDb.Open(path)) db.Migrate();
        using (var db2 = VideoShelfDb.Open(path)) db2.Migrate(); // must not throw, version stable
        using var db3 = VideoShelfDb.Open(path);
        Assert.False(TableExists(db3, "smart_views"));
        Assert.Equal(1, UserVersion(db3)); // does not climb past the latest defined version
    }

    [Fact]
    public void Migrate_PreExistingDbWithSmartViews_GetsCleaned()
    {
        // open a db, manually CREATE TABLE smart_views(...) + set user_version=0, then Migrate()
        // assert smart_views gone + user_version=1 (simulates an upgrade from a pre-M25 install)
    }
}
```
Use the project's existing test helpers for opening a temp DB and for `TableExists`/raw SQL (search `tests/VideoShelf.Core.Tests` for an existing storage-test base; reuse it — STOP and report if there is none and define small local helpers running `PRAGMA user_version` / `SELECT name FROM sqlite_master`).

- [ ] **Step 2: Run — verify FAIL** (`smart_views` still present, `user_version` 0).

- [ ] **Step 3: Implement the runner.** At the END of `Migrate()` (after the existing `Schema` create + `EnsureColumn` calls + the M19 `DROP TABLE IF EXISTS video_chapters`), call a new `RunVersionedMigrations(conn)`:

```csharp
private const int LatestSchemaVersion = 1;

private static void RunVersionedMigrations(SqliteConnection conn)
{
    long current;
    using (var read = conn.CreateCommand())
    {
        read.CommandText = "PRAGMA user_version";
        current = (long)(read.ExecuteScalar() ?? 0L);
    }
    if (current >= LatestSchemaVersion) return;

    using var tx = conn.BeginTransaction();
    if (current < 1)
    {
        // v1: drop tables for features cut in M24 (verified zero readers in D1).
        // DROP IF EXISTS is safe + idempotent; verify-before-destroy satisfied by the D1 grep.
        Exec(conn, tx, "DROP TABLE IF EXISTS smart_views");
        // add any other verified-dead table from D1 here, one DROP per line.
    }
    using (var setv = conn.CreateCommand())
    {
        setv.Transaction = tx;
        setv.CommandText = $"PRAGMA user_version = {LatestSchemaVersion}";
        setv.ExecuteNonQuery();
    }
    tx.Commit();
}

private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql)
{
    using var cmd = conn.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
}
```
**Note on SQLite:** `PRAGMA user_version` cannot be parameterized — the value MUST be a literal (hence string interpolation of the const, which is safe because it is a compile-time int). `DROP TABLE` inside a transaction is supported by SQLite. Call `RunVersionedMigrations(conn)` as the final line of `Migrate()`, reusing whatever open `SqliteConnection` `Migrate()` already holds (match the existing method's connection handling — if `Migrate()` opens its own connection, call the runner on that same one before it closes).

- [ ] **Step 4: Run — verify PASS** on all three tests.
- [ ] **Step 5: Build + a real-app `--view Home --done-signal` launch** to confirm an existing dev DB upgrades cleanly (no crash, app starts). If you have a pre-M25 DB lying around, point `--data-dir` at a copy and confirm `smart_views` is gone after launch.
- [ ] **Step 6: Commit.** `git commit -am "feat(core): first user_version migration runner — drop M24-orphaned smart_views"`.

### Task D4: Group D gate + PR

- [ ] Full gate green; push; PR body = the D1 grep evidence (which tables dropped + proof of zero readers) + the kept-active list + idempotency note; foreground CI watch; merge; sync main.

---

## GROUP E — Release prep + 1.0 close

> **PR #5 of 5.** Version 1.0.0, verify the signed-MSIX CI path, CHANGELOG + README, final sweep, ROADMAP flip + v5 close. The ROADMAP flip rides THIS PR.

### Task E1: Version → 1.0.0

**Files:**
- Modify: `src/VideoShelf.App/VideoShelf.App.csproj`
- Read: `.github/workflows/ci.yml` (the `package` job + the generated `AppxManifest.xml` `Identity Version`).

- [ ] **Step 1: Add version properties to the App csproj** (inside the main `<PropertyGroup>`):

```xml
<Version>1.0.0</Version>
<AssemblyVersion>1.0.0.0</AssemblyVersion>
<FileVersion>1.0.0.0</FileVersion>
```

- [ ] **Step 2: Align the MSIX manifest version.** Read how the `package` job composes `AppxManifest.xml`. If the `Identity Version="x.x.x.0"` is hardcoded in the workflow/manifest template, set it to `1.0.0.0`. If it is already derived from the csproj, leave it. (MSIX requires a 4-part `Major.Minor.Build.Revision` with revision 0.) **STOP and report if the manifest source is unclear.**
- [ ] **Step 3: Build.** `dotnet build -v minimal` clean.
- [ ] **Step 4: Commit.** `git commit -am "chore(release): version VideoShelf 1.0.0"`.

### Task E2: Verify the signed-MSIX package path

**Files:**
- Read/Run: `tools/package/Assert-NoMediaTools.ps1`, the `package` job steps.

- [ ] **Step 1: Run the publish + no-media-tools assertion locally** (mirror the CI steps):
```
dotnet publish src/VideoShelf.App/VideoShelf.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o packaging/_publish
pwsh tools/package/Assert-NoMediaTools.ps1 -PublishDir packaging/_publish
```
Expected: publish succeeds; the assertion PASSES (no ffmpeg/HandBrake in the output). If `makeappx`/`signtool` are available locally, optionally pack + sign to confirm the MSIX builds; otherwise rely on the CI `package` job (it runs on every PR). **Do not commit the `packaging/` output** (confirm it is git-ignored; if not, add it to `.gitignore`).
- [ ] **Step 2:** If `.gitignore` needed a `packaging/` entry, commit it: `git commit -am "chore: ignore packaging output"`.

### Task E3: CHANGELOG.md

**Files:**
- Create: `CHANGELOG.md` (repo root)

- [ ] **Step 1: Write a 1.0 CHANGELOG** summarizing the journey at a high level (NOT a per-PR dump). Use this skeleton, filling each version from the ROADMAP milestone rows:

```markdown
# Changelog

All notable changes to VideoShelf. This project is a local-only personal Windows
video library + player; "1.0" marks the feature-frozen, hardened release.

## 1.0.0 — 2026-06-16
First stable release. A self-contained (.NET 10 WPF + bundled libVLC) creator-centric
video library: multi-source scan → Creator → series/standalone → episode; lean
immersive player + draggable PiP; play-queue/up-next; favorites/ratings/playlists/
watch-later; full maintenance suite (relink, duplicate keeper via Recycle Bin,
orphan cleanup, health dashboard); Insights dashboard; black-glass Ice-Cyan design.
Strictly read-only for library files. No network for content; no external media tools.

### Hardening in 1.0 (M25)
- Global UI-thread + AppDomain exception net with logged crash reports.
- Destructive-path safety audit + regression tests (Recycle-Bin keeper gate,
  rename crash-mid-apply resume, remove-source DB-only undo, frame-picker write scope,
  a "library never written" audit gate).
- Fail-path hardening (visible player errors, bitmap fallback, skip-and-continue
  scans, empty/busy-DB read safety).
- First schema-version migration — dropped feature-cut orphan tables.

## Earlier milestones (v1–v5, M1–M24)
Summarize each version block from ROADMAP.md (v1 foundation/playback, v2 creator
redesign, v3 polish & personalization, v4 depth & scale, v5 depth & reach). One line
per version is enough — ROADMAP.md holds the per-milestone detail.
```
Fill the "Earlier milestones" section with one line per v1–v5 from the ROADMAP version-header rows.

- [ ] **Step 2: Commit.** `git commit -am "docs: add CHANGELOG for 1.0"`.

### Task E4: README pass

**Files:**
- Modify/Create: `README.md` (repo root)

- [ ] **Step 1: Ensure the README covers, concisely:** what VideoShelf is (one paragraph); the load-bearing invariants (**local-only, no network for content, no external media tools on PATH, library files never written**); how to build/run (`dotnet build`/`dotnet run` the App, .NET 10, the `VideoShelf.slnx` solution); how to run the tests (`dotnet test VideoShelf.slnx -c Release`); and a one-line pointer to `ROADMAP.md` + `CHANGELOG.md`. Keep it factual; do not overstate (no "production-grade"/marketing). If a README already exists, update stale bits rather than rewrite.
- [ ] **Step 2: Commit.** `git commit -am "docs: README pass for 1.0"`.

### Task E5: Final full sweep

- [ ] **Step 1: Run the full visual sweep** (`Run-VisualSweep.ps1`) on a populated library; dispatch a Sonnet subagent to read the PNGs → text verdict. Expected: PASS on every screen (the black-glass theme renders, no regression from Groups A–D). Fix any regression with the A3 TDD pattern.
- [ ] **Step 2: Full gate.** `dotnet test VideoShelf.slnx -c Release --nologo -v q` — all green; record final count.

### Task E6: ROADMAP flip + v5 close (rides this PR)

**Files:**
- Modify: `ROADMAP.md`

- [ ] **Step 1: Flip the M25 row to ✅ Merged** with the PR links + a one-line shipped summary (groups A–E, final test count, the first user_version migration, 1.0 version). Update the Legend usage as needed.
- [ ] **Step 2: Add a decision-log entry** at the top of "Decision log & gotchas": M25 shipped, the durable facts (the global handler location + `ResolveDataDir`; the `user_version` runner now exists — **document that future schema changes use `RunVersionedMigrations` + bump `LatestSchemaVersion`, NOT the old `EnsureColumn`-only pattern, for DROPs**; the safety-audit allowlist file; the `smart_views` drop). Note the **2 owner manual checks (frame-picker, #87 half-star popup) are owner-verified live** per the M25 scope decision.
- [ ] **Step 3: Mark v5 (M22–M25) COMPLETE and VideoShelf 1.0 shipped** in the v5 header row + Definition note.
- [ ] **Step 4: Commit.** `git commit -am "docs(roadmap): M25 shipped — VideoShelf 1.0, v5 complete"`.

### Task E7: Group E gate + PR (final)

- [ ] Full gate green; push; open the final PR (body = version bump + CHANGELOG/README + sweep PASS + ROADMAP flip + "VideoShelf 1.0"); **sleep ~20s then** `gh pr checks <PR#> --watch` (foreground); merge `--merge --delete-branch` from the repo root; sync `main`.
- [ ] **Ping the owner** (PushNotification) that M25 merged + CI-green + VideoShelf is 1.0 / v5 complete.

---

## Self-Review (completed by plan author)

- **Spec coverage:** Group A = real-app crash/invisible sweep ✔ + global handler ✔. Group B = all 4 destructive paths (recycle/rename/remove-source/frame-picker) + library-never-written ✔. Group C = fail paths (player error/thumbnail/missing-locked/empty-busy-DB) ✔. Group D = first user_version migration + orphan drop ✔. Group E = version 1.0.0 + MSIX verify + CHANGELOG + README + sweep + ROADMAP flip ✔. Test-coverage-gap prong is woven through B/C/D (every guard gets a pinning test) ✔. The 2 manual checks = owner-verified live, one-line reminder in E6 ✔.
- **Placeholder scan:** audit tasks intentionally say "fix only if a gap is found" — that is the nature of a hardening pass, not a placeholder; each provides the exact test to write and the fix pattern. No "TBD"/"handle edge cases" left unspecified.
- **Type consistency:** `CrashReporter.FormatReport`/`WriteToDisk`, `RunVersionedMigrations`/`LatestSchemaVersion`/`Exec`, `ComposeCoverPath` are used consistently where referenced.
- **Known unknowns flagged as STOP-and-report:** data-dir resolution helper location (A4), whether a Core storage-test base exists (D3), MSIX manifest version source (E1/E2), any orphan candidate with a live reader (D1). These are the only places the codebase may diverge from the digest this plan was written from.
