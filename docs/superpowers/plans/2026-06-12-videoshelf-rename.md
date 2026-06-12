# Opt-in Rename Tool (Milestone 5) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Written for Sonnet execution. If something doesn't match what you find in the repo (a signature, a file, a pattern), STOP and report rather than guess.** This plan was written against the merged M4 codebase (177 tests). Build/test gate: `dotnet test VideoShelf.slnx -c Release --nologo -v q` (run from `C:\Agent Projects\VideoShelf`).

**Goal:** Add a per-series, opt-in rename tool that previews canonical (editable) file names, renames files on disk after explicit confirmation, repaths the library DB off stable integer video ids (so watched/resume/tags survive), writes a crash-safe undo manifest, and offers one-click Undo.

**Architecture:** All logic is pure and unit-testable in `VideoShelf.Core.Renaming` behind an `IFileSystem` seam (mirrors the existing `IThumbnailService`/`IPlaybackEngine` testability pattern). `RenamePlanner` resolves targets and flags conflicts; `RenameExecutor` writes the undo manifest **before** any move, renames each file, then repaths the DB via `LibraryRepository.UpdateVideoPath`. The App layer adds a `RenameToolViewModel` + `RenameToolView`, a new `AppView.RenameTool` nav host, and a "Rename files…" entry point on each series in the section-detail view. The concrete `RealFileSystem` and the View are integration-only (screenshot-verified in Phase 6, per the ROADMAP).

**Tech Stack:** .NET 10, WPF + WPF-UI, CommunityToolkit.Mvvm (`[ObservableProperty]`/`[RelayCommand]`), Microsoft.Data.Sqlite, System.Text.Json (manifest), xUnit + Shouldly.

---

## Design decisions (locked during planning, 2026-06-12)

- **Naming scheme:** canonical **and editable**. Default proposal is `"<Base Title> <NN>.ext"` derived so it re-parses to the same `(title, episode)` via `TitleParser` (a rescan re-groups identically). Standalone series (a single file) get `"<Base Title>.ext"` (no number). Every proposed name is editable inline before Apply.
- **Scope:** **per-series**. The tool loads one series' videos via `LibraryRepository.GetVideosForSeries(seriesId)`.
- **Undo:** **in-app one-click**. The manifest is written to disk before any move; an "Undo last rename" button reverses files and repaths the DB back. The last manifest path is persisted in `settings` (key `last_rename_manifest`) so Undo survives an app restart.
- **Defensive / crash-safe (per the user's standing destructive-op discipline):** never overwrite — `File.Move(src,dst)` (2-arg) throws if the target exists; the planner pre-flags occupied targets and duplicate targets; the executor re-verifies at apply time; the manifest is written first so a crash mid-batch is recoverable; Undo is tolerant of partially-applied batches.
- **Entry point:** section-detail series cards only (the browse view's series cards are left unchanged this milestone — documented, revisit in Phase 6).

## Stable-identity facts this relies on (verified against the schema)

- `videos.id INTEGER PRIMARY KEY` is the stable identity. `videos.file_path TEXT UNIQUE` is what we repath; `videos.raw_filename` mirrors the file name (used by Search) and **must** be updated too.
- `watch_events.video_id`, `videos.resume_position`/`resume_updated_at`, and `section_tags.section_id` all key off stable ids — untouched by a repath, so they survive automatically.
- `grouping_overrides.file_path` is **path-keyed** (reserved/unused today but present in the schema) — the repath updates it in the same transaction to avoid a future orphan.
- The scanner matches on `file_path`. After a successful move **and** DB repath, a rescan finds the file and `ClearMissing(newPath)` matches it. If a crash happens **between** move and DB repath, the row points at the old (now-missing) path; the undo manifest is the recovery path.

---

## File structure

**Create (Core — `src/VideoShelf.Core/Renaming/`):**
- `IFileSystem.cs` — filesystem seam.
- `RealFileSystem.cs` — production `IFileSystem` over `System.IO` (the only place rename touches disk).
- `CanonicalNamer.cs` — pure name builder + sanitizer + pad-width.
- `RenameModels.cs` — `RenameItemStatus`, `RenameItem`, `RenamePlan`.
- `RenamePlanner.cs` — resolves targets, flags conflicts.
- `RenameManifest.cs` — `RenameManifestEntry`, `RenameManifest`.
- `RenameResult.cs` — `RenameResult`.
- `RenameExecutor.cs` — Apply + Undo (crash-safe).

**Create (App):**
- `src/VideoShelf.App/ViewModels/RenameRowViewModel.cs`
- `src/VideoShelf.App/ViewModels/RenameToolViewModel.cs`
- `src/VideoShelf.App/Views/RenameToolView.xaml` (+ `.xaml.cs`)

**Create (tests):**
- `tests/VideoShelf.Core.Tests/InMemoryFileSystem.cs` (test helper)
- `tests/VideoShelf.Core.Tests/CanonicalNamerTests.cs`
- `tests/VideoShelf.Core.Tests/RenamePlannerTests.cs`
- `tests/VideoShelf.Core.Tests/RenameExecutorTests.cs`
- `tests/VideoShelf.Core.Tests/UpdateVideoPathTests.cs`
- `tests/VideoShelf.App.Tests/RenameToolViewModelTests.cs`
- `tests/VideoShelf.App.Tests/RenameNavigationTests.cs`

**Modify:**
- `src/VideoShelf.Core/Storage/LibraryRepository.cs` — add `UpdateVideoPath`.
- `src/VideoShelf.App/Services/AppPaths.cs` — add `RenameManifestDirectory`.
- `src/VideoShelf.App/Services/ServiceCollectionExtensions.cs` — register new services.
- `src/VideoShelf.App/ViewModels/SeriesViewModel.cs` — add `RenameRequested` event + `RequestRename` command.
- `src/VideoShelf.App/ViewModels/SectionDetailViewModel.cs` — bubble `RenameRequested`.
- `src/VideoShelf.App/ViewModels/MainViewModel.cs` — `AppView.RenameTool`, host property, nav.
- `src/VideoShelf.App/Views/MainWindow.xaml` — `RenameToolView` host.
- `src/VideoShelf.App/Views/SectionDetailView.xaml` — "Rename files…" button per series.

---

### Task 1: Filesystem seam (`IFileSystem`, `RealFileSystem`, in-memory fake)

**Files:**
- Create: `src/VideoShelf.Core/Renaming/IFileSystem.cs`
- Create: `src/VideoShelf.Core/Renaming/RealFileSystem.cs`
- Create: `tests/VideoShelf.Core.Tests/InMemoryFileSystem.cs`
- Test: `tests/VideoShelf.Core.Tests/InMemoryFileSystemTests.cs`

- [ ] **Step 1: Write `IFileSystem`**

```csharp
// src/VideoShelf.Core/Renaming/IFileSystem.cs
namespace VideoShelf.Core.Renaming;

/// <summary>Filesystem seam so rename planning/execution is unit-testable with an in-memory fake.</summary>
public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    /// <summary>Renames/moves a file. MUST throw if the destination already exists (never overwrite).</summary>
    void Move(string sourcePath, string destinationPath);
    string ReadAllText(string path);
    void WriteAllText(string path, string contents);
}
```

- [ ] **Step 2: Write `RealFileSystem`**

```csharp
// src/VideoShelf.Core/Renaming/RealFileSystem.cs
using System.IO;

namespace VideoShelf.Core.Renaming;

/// <summary>Production <see cref="IFileSystem"/> over System.IO. The only place the rename tool touches disk.
/// Move uses the 2-arg File.Move, which throws if the destination exists — defensive by default.</summary>
public sealed class RealFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void Move(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
}
```

- [ ] **Step 3: Write the in-memory fake (test helper, used by later tasks)**

```csharp
// tests/VideoShelf.Core.Tests/InMemoryFileSystem.cs
using System;
using System.Collections.Generic;
using System.IO;
using VideoShelf.Core.Renaming;

namespace VideoShelf.Core.Tests;

/// <summary>In-memory <see cref="IFileSystem"/> for rename tests. Move throws if the target exists
/// (mirrors the 2-arg File.Move contract). Paths are normalized with Path.GetFullPath.</summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryFileSystem(params string[] initialFiles)
    {
        foreach (var f in initialFiles) _files[Norm(f)] = "";
    }

    private static string Norm(string p) => Path.GetFullPath(p);

    public void AddFile(string path, string contents = "") => _files[Norm(path)] = contents;

    public bool FileExists(string path) => _files.ContainsKey(Norm(path));
    public bool DirectoryExists(string path) => _dirs.Contains(Norm(path));
    public void CreateDirectory(string path) => _dirs.Add(Norm(path));

    public void Move(string sourcePath, string destinationPath)
    {
        var src = Norm(sourcePath);
        var dst = Norm(destinationPath);
        if (!_files.ContainsKey(src)) throw new FileNotFoundException("source not found", src);
        if (_files.ContainsKey(dst)) throw new IOException($"target exists: {dst}");
        _files[dst] = _files[src];
        _files.Remove(src);
    }

    public string ReadAllText(string path) => _files[Norm(path)];
    public void WriteAllText(string path, string contents) => _files[Norm(path)] = contents;
}
```

- [ ] **Step 4: Write a sanity test for the fake**

```csharp
// tests/VideoShelf.Core.Tests/InMemoryFileSystemTests.cs
using System.IO;
using Shouldly;
using VideoShelf.Core.Tests;
using Xunit;

namespace VideoShelf.Core.Tests;

public class InMemoryFileSystemTests
{
    [Fact]
    public void Move_RelocatesFile_AndThrowsOnExistingTarget()
    {
        var fs = new InMemoryFileSystem(@"C:\lib\a.mkv");
        fs.Move(@"C:\lib\a.mkv", @"C:\lib\b.mkv");
        fs.FileExists(@"C:\lib\a.mkv").ShouldBeFalse();
        fs.FileExists(@"C:\lib\b.mkv").ShouldBeTrue();

        fs.AddFile(@"C:\lib\c.mkv");
        Should.Throw<IOException>(() => fs.Move(@"C:\lib\b.mkv", @"C:\lib\c.mkv"));
    }
}
```

- [ ] **Step 5: Run tests, expect PASS**

Run: `dotnet test tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj -c Release --nologo -v q`
Expected: build succeeds, all tests pass (baseline 83 Core + 1 new = 84).

- [ ] **Step 6: Commit**

```bash
git add src/VideoShelf.Core/Renaming/IFileSystem.cs src/VideoShelf.Core/Renaming/RealFileSystem.cs tests/VideoShelf.Core.Tests/InMemoryFileSystem.cs tests/VideoShelf.Core.Tests/InMemoryFileSystemTests.cs
git commit -m "feat(core): add IFileSystem seam + in-memory fake for rename tool"
```

---

### Task 2: `CanonicalNamer`

**Files:**
- Create: `src/VideoShelf.Core/Renaming/CanonicalNamer.cs`
- Test: `tests/VideoShelf.Core.Tests/CanonicalNamerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/VideoShelf.Core.Tests/CanonicalNamerTests.cs
using Shouldly;
using VideoShelf.Core.Renaming;
using Xunit;

namespace VideoShelf.Core.Tests;

public class CanonicalNamerTests
{
    [Fact]
    public void Build_NumbersEpisodes_WithZeroPadding()
        => CanonicalNamer.Build("My Show", 3, ".mkv", 2).ShouldBe("My Show 03.mkv");

    [Fact]
    public void Build_Standalone_HasNoNumber()
        => CanonicalNamer.Build("My Movie", null, ".mp4", 2).ShouldBe("My Movie.mp4");

    [Fact]
    public void Build_AddsLeadingDot_WhenExtensionMissingIt()
        => CanonicalNamer.Build("X", 1, "mkv", 2).ShouldBe("X 01.mkv");

    [Fact]
    public void Build_SanitizesIllegalCharacters_AndCollapsesWhitespace()
        => CanonicalNamer.Build("A: B / C", 1, ".mkv", 2).ShouldBe("A B C 01.mkv");

    [Fact]
    public void Build_FallsBackToUntitled_WhenTitleSanitizesEmpty()
        => CanonicalNamer.Build("///", null, ".mkv", 2).ShouldBe("untitled.mkv");

    [Fact]
    public void PadWidth_IsAtLeastTwo_AndGrowsWithMax()
    {
        CanonicalNamer.PadWidth(new[] { 1, 2, 9 }).ShouldBe(2);
        CanonicalNamer.PadWidth(new[] { 1, 120 }).ShouldBe(3);
        CanonicalNamer.PadWidth(new int[0]).ShouldBe(2);
    }

    [Fact]
    public void Build_ReparsesToSameTitleAndEpisode_ViaTitleParser()
    {
        var name = CanonicalNamer.Build("My Show", 4, ".mkv", 2); // "My Show 04.mkv"
        var parsed = TitleParser.Parse(System.IO.Path.GetFileNameWithoutExtension(name));
        parsed.BaseTitle.ShouldBe("My Show");
        parsed.EpisodeNumber.ShouldBe(4);
    }
}
```

> Note: `TitleParser` is `VideoShelf.Core.Naming.TitleParser`; `ParsedTitle` exposes `BaseTitle` and `EpisodeNumber`. If the test project lacks a `using VideoShelf.Core.Naming;` global, add it to the file.

- [ ] **Step 2: Run, expect FAIL** (`CanonicalNamer` not defined).

Run: `dotnet test tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj -c Release --nologo -v q`
Expected: compile error / FAIL.

- [ ] **Step 3: Implement `CanonicalNamer`**

```csharp
// src/VideoShelf.Core/Renaming/CanonicalNamer.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace VideoShelf.Core.Renaming;

/// <summary>Builds canonical "&lt;Base Title&gt; &lt;NN&gt;.ext" file names that re-parse to the same
/// (title, episode) via TitleParser — so a rescan re-groups them identically.</summary>
public static class CanonicalNamer
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    /// <summary>Minimum zero-pad width for a set of episode numbers (>= 2 so natural sort holds to 99).</summary>
    public static int PadWidth(IEnumerable<int> episodeNumbers)
    {
        var max = 0;
        foreach (var n in episodeNumbers)
            if (n > max) max = n;
        var digits = max <= 0 ? 1 : (int)Math.Floor(Math.Log10(max)) + 1;
        return Math.Max(2, digits);
    }

    /// <summary>Replaces characters illegal in a file name with spaces, collapses whitespace, trims.</summary>
    public static string SanitizeTitle(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title)
            sb.Append(Array.IndexOf(InvalidChars, ch) >= 0 ? ' ' : ch);
        return string.Join(' ',
            sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>Builds the canonical file name (with extension). A null <paramref name="episodeNo"/> means a
    /// standalone — no number is appended. <paramref name="extension"/> may or may not include the leading dot.</summary>
    public static string Build(string baseTitle, int? episodeNo, string extension, int padWidth)
    {
        var title = SanitizeTitle(baseTitle);
        if (title.Length == 0) title = "untitled";
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        if (episodeNo is null)
            return title + ext;
        var num = episodeNo.Value.ToString(CultureInfo.InvariantCulture).PadLeft(padWidth, '0');
        return $"{title} {num}{ext}";
    }
}
```

- [ ] **Step 4: Run, expect PASS.**

- [ ] **Step 5: Commit**

```bash
git add src/VideoShelf.Core/Renaming/CanonicalNamer.cs tests/VideoShelf.Core.Tests/CanonicalNamerTests.cs
git commit -m "feat(core): add CanonicalNamer for rename proposals"
```

---

### Task 3: Rename models + `RenamePlanner`

**Files:**
- Create: `src/VideoShelf.Core/Renaming/RenameModels.cs`
- Create: `src/VideoShelf.Core/Renaming/RenamePlanner.cs`
- Test: `tests/VideoShelf.Core.Tests/RenamePlannerTests.cs`

- [ ] **Step 1: Write the models**

```csharp
// src/VideoShelf.Core/Renaming/RenameModels.cs
using System.Collections.Generic;
using System.IO;

namespace VideoShelf.Core.Renaming;

/// <summary>Why a single row will or won't be renamed.</summary>
public enum RenameItemStatus
{
    Unchanged,       // old == new, nothing to do
    Ready,           // will be renamed
    SourceMissing,   // source file is gone from disk
    TargetExists,    // a different existing file already occupies the target path
    DuplicateTarget, // two rows in this batch resolve to the same target name
    InvalidName,     // proposed name is empty / contains illegal characters
}

/// <summary>One planned rename: stable video id + old/new absolute paths and a status.</summary>
public sealed record RenameItem(long VideoId, int EpisodeNo, string OldPath, string NewPath, RenameItemStatus Status)
{
    public string OldName => Path.GetFileName(OldPath);
    public string NewName => Path.GetFileName(NewPath);
    public bool WillRename => Status == RenameItemStatus.Ready;
}

/// <summary>The planned renames for one series, with conflicts already flagged.</summary>
public sealed record RenamePlan(IReadOnlyList<RenameItem> Items)
{
    public int ReadyCount
    {
        get { var c = 0; foreach (var i in Items) if (i.WillRename) c++; return c; }
    }
    public bool HasReady => ReadyCount > 0;
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/VideoShelf.Core.Tests/RenamePlannerTests.cs
using System.Collections.Generic;
using Shouldly;
using VideoShelf.Core.Models;
using VideoShelf.Core.Renaming;
using Xunit;

namespace VideoShelf.Core.Tests;

public class RenamePlannerTests
{
    private static Video V(long id, string path, int ep) =>
        new(id, 1, path, ep, System.IO.Path.GetFileName(path), "mkv", null, null, false, "", false);

    [Fact]
    public void Ready_WhenTargetIsFreeAndSourceExists()
    {
        var fs = new InMemoryFileSystem(@"C:\lib\old1.mkv");
        var planner = new RenamePlanner(fs);
        var videos = new[] { V(1, @"C:\lib\old1.mkv", 1) };
        var proposed = new Dictionary<long, string> { [1] = "Show 01.mkv" };

        var plan = planner.BuildPlan(videos, proposed);

        plan.Items[0].Status.ShouldBe(RenameItemStatus.Ready);
        plan.Items[0].NewName.ShouldBe("Show 01.mkv");
        plan.ReadyCount.ShouldBe(1);
    }

    [Fact]
    public void Unchanged_WhenProposedEqualsCurrent()
    {
        var fs = new InMemoryFileSystem(@"C:\lib\Show 01.mkv");
        var plan = new RenamePlanner(fs).BuildPlan(
            new[] { V(1, @"C:\lib\Show 01.mkv", 1) },
            new Dictionary<long, string> { [1] = "Show 01.mkv" });
        plan.Items[0].Status.ShouldBe(RenameItemStatus.Unchanged);
    }

    [Fact]
    public void TargetExists_WhenADifferentFileOccupiesTheName()
    {
        var fs = new InMemoryFileSystem(@"C:\lib\old1.mkv", @"C:\lib\Show 01.mkv");
        var plan = new RenamePlanner(fs).BuildPlan(
            new[] { V(1, @"C:\lib\old1.mkv", 1) },
            new Dictionary<long, string> { [1] = "Show 01.mkv" });
        plan.Items[0].Status.ShouldBe(RenameItemStatus.TargetExists);
    }

    [Fact]
    public void DuplicateTarget_WhenTwoRowsMapToTheSameName()
    {
        var fs = new InMemoryFileSystem(@"C:\lib\a.mkv", @"C:\lib\b.mkv");
        var plan = new RenamePlanner(fs).BuildPlan(
            new[] { V(1, @"C:\lib\a.mkv", 1), V(2, @"C:\lib\b.mkv", 1) },
            new Dictionary<long, string> { [1] = "Show 01.mkv", [2] = "Show 01.mkv" });
        plan.Items[0].Status.ShouldBe(RenameItemStatus.DuplicateTarget);
        plan.Items[1].Status.ShouldBe(RenameItemStatus.DuplicateTarget);
    }

    [Fact]
    public void SourceMissing_WhenFileNotOnDisk()
    {
        var fs = new InMemoryFileSystem(); // empty
        var plan = new RenamePlanner(fs).BuildPlan(
            new[] { V(1, @"C:\lib\gone.mkv", 1) },
            new Dictionary<long, string> { [1] = "Show 01.mkv" });
        plan.Items[0].Status.ShouldBe(RenameItemStatus.SourceMissing);
    }

    [Fact]
    public void InvalidName_WhenProposedHasIllegalCharacters()
    {
        var fs = new InMemoryFileSystem(@"C:\lib\a.mkv");
        var plan = new RenamePlanner(fs).BuildPlan(
            new[] { V(1, @"C:\lib\a.mkv", 1) },
            new Dictionary<long, string> { [1] = "bad/name.mkv" });
        plan.Items[0].Status.ShouldBe(RenameItemStatus.InvalidName);
    }
}
```

- [ ] **Step 3: Run, expect FAIL** (`RenamePlanner` not defined).

- [ ] **Step 4: Implement `RenamePlanner`**

```csharp
// src/VideoShelf.Core/Renaming/RenamePlanner.cs
using System;
using System.Collections.Generic;
using System.IO;
using VideoShelf.Core.Models;

namespace VideoShelf.Core.Renaming;

/// <summary>Pure planner: given a series' videos and a proposed file name per video id, resolves absolute
/// target paths and flags conflicts (missing source, occupied target, duplicate targets, invalid name).</summary>
public sealed class RenamePlanner(IFileSystem fs)
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    /// <summary>Builds a plan. A video id absent from <paramref name="proposedNames"/> keeps its current name.</summary>
    public RenamePlan BuildPlan(IReadOnlyList<Video> videos, IReadOnlyDictionary<long, string> proposedNames)
    {
        var rows = new List<(RenameItem Item, bool Invalid)>(videos.Count);
        var targetCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var v in videos)
        {
            var dir = Path.GetDirectoryName(v.FilePath) ?? "";
            var proposed = (proposedNames.TryGetValue(v.Id, out var name) ? name : Path.GetFileName(v.FilePath))?.Trim() ?? "";
            var invalid = proposed.Length > 0 && proposed.IndexOfAny(InvalidChars) >= 0;
            var newPath = (proposed.Length == 0 || invalid) ? v.FilePath : Path.Combine(dir, proposed);

            rows.Add((new RenameItem(v.Id, v.EpisodeNo, v.FilePath, newPath, RenameItemStatus.Ready), invalid));
            if (!invalid && !PathsEqual(newPath, v.FilePath))
                targetCounts[newPath] = targetCounts.GetValueOrDefault(newPath) + 1;
        }

        var result = new List<RenameItem>(rows.Count);
        foreach (var (item, invalid) in rows)
            result.Add(item with { Status = Classify(item, invalid, targetCounts) });
        return new RenamePlan(result);
    }

    private RenameItemStatus Classify(RenameItem row, bool invalid, Dictionary<string, int> targetCounts)
    {
        if (invalid) return RenameItemStatus.InvalidName;
        if (PathsEqual(row.OldPath, row.NewPath)) return RenameItemStatus.Unchanged;
        if (!fs.FileExists(row.OldPath)) return RenameItemStatus.SourceMissing;
        if (targetCounts.GetValueOrDefault(row.NewPath) > 1) return RenameItemStatus.DuplicateTarget;
        if (fs.FileExists(row.NewPath)) return RenameItemStatus.TargetExists;
        return RenameItemStatus.Ready;
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 5: Run, expect PASS.**

- [ ] **Step 6: Commit**

```bash
git add src/VideoShelf.Core/Renaming/RenameModels.cs src/VideoShelf.Core/Renaming/RenamePlanner.cs tests/VideoShelf.Core.Tests/RenamePlannerTests.cs
git commit -m "feat(core): add rename plan models + conflict-aware RenamePlanner"
```

---

### Task 4: `LibraryRepository.UpdateVideoPath` (DB repath off stable id)

**Files:**
- Modify: `src/VideoShelf.Core/Storage/LibraryRepository.cs` (add method at end of class)
- Test: `tests/VideoShelf.Core.Tests/UpdateVideoPathTests.cs`

- [ ] **Step 1: Write the failing tests**

> These use a real temp SQLite DB through the public repo API. If the Core.Tests project already has a DB fixture helper, you may reuse it; otherwise this self-contained setup is fine.

```csharp
// tests/VideoShelf.Core.Tests/UpdateVideoPathTests.cs
using System;
using System.IO;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.Core.Tests;

public class UpdateVideoPathTests : IDisposable
{
    private readonly string _dir;
    private readonly VideoShelfDb _db;
    private readonly LibraryRepository _library;
    private readonly WatchRepository _watch;

    public UpdateVideoPathTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vs-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new VideoShelfDb(Path.Combine(_dir, "library.db"));
        _db.Migrate();
        _library = new LibraryRepository(_db);
        _watch = new WatchRepository(_db);
    }

    [Fact]
    public void RepathsFilePathAndRawFilename_AndStateSurvives()
    {
        var src = _library.UpsertSource(@"C:\root", "Root");
        var sec = _library.UpsertSection(src, "Section");
        var ser = _library.UpsertSeries(sec, "Show", isStandalone: false);
        var vid = _library.UpsertVideo(ser, @"C:\root\Section\old1.mkv", 1, "mkv");

        _watch.SetWatched(vid, true);
        _library.SetResumePosition(vid, 123.0);

        _library.UpdateVideoPath(vid, @"C:\root\Section\old1.mkv", @"C:\root\Section\Show 01.mkv");

        var v = _library.GetVideosForSeries(ser).Single();
        v.FilePath.ShouldBe(@"C:\root\Section\Show 01.mkv");
        v.RawFilename.ShouldBe("Show 01.mkv");
        _watch.IsWatched(vid).ShouldBeTrue();
        _library.GetResumePosition(vid).ShouldBe(123.0);
    }
}
```

> `WatchRepository.SetWatched(long, bool)` and `IsWatched(long)` exist; `LibraryRepository.GetResumePosition(long)` / `SetResumePosition(long,double)` exist. If a signature differs, STOP and report.

- [ ] **Step 2: Run, expect FAIL** (`UpdateVideoPath` not defined).

- [ ] **Step 3: Implement `UpdateVideoPath`** (append inside the `LibraryRepository` class, e.g. after `GetNextEpisode`)

```csharp
    /// <summary>Repaths a video after an on-disk rename. Updates the stable row's file_path + raw_filename and
    /// any path-keyed grouping_overrides, in one transaction. Watched/resume/tags key off ids and are untouched.</summary>
    public void UpdateVideoPath(long videoId, string oldPath, string newPath)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE videos SET file_path = $new, raw_filename = $raw WHERE id = $id";
            cmd.Parameters.AddWithValue("$new", newPath);
            cmd.Parameters.AddWithValue("$raw", Path.GetFileName(newPath));
            cmd.Parameters.AddWithValue("$id", videoId);
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE grouping_overrides SET file_path = $new WHERE file_path = $old";
            cmd.Parameters.AddWithValue("$new", newPath);
            cmd.Parameters.AddWithValue("$old", oldPath);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
```

> `LibraryRepository.cs` already has `using System.IO;` (line 2), so `Path.GetFileName` resolves.

- [ ] **Step 4: Run, expect PASS.**

- [ ] **Step 5: Commit**

```bash
git add src/VideoShelf.Core/Storage/LibraryRepository.cs tests/VideoShelf.Core.Tests/UpdateVideoPathTests.cs
git commit -m "feat(core): repath a video off its stable id (UpdateVideoPath)"
```

---

### Task 5: Manifest, result, and `RenameExecutor` (Apply + Undo, crash-safe)

**Files:**
- Create: `src/VideoShelf.Core/Renaming/RenameManifest.cs`
- Create: `src/VideoShelf.Core/Renaming/RenameResult.cs`
- Create: `src/VideoShelf.Core/Renaming/RenameExecutor.cs`
- Test: `tests/VideoShelf.Core.Tests/RenameExecutorTests.cs`

- [ ] **Step 1: Write manifest + result types**

```csharp
// src/VideoShelf.Core/Renaming/RenameManifest.cs
using System.Collections.Generic;

namespace VideoShelf.Core.Renaming;

public sealed record RenameManifestEntry(long VideoId, string OldPath, string NewPath);

/// <summary>Crash-safe undo record for one Apply: written to disk BEFORE any file moves.</summary>
public sealed record RenameManifest(
    string BatchId,
    long SeriesId,
    string CreatedAtUtc,
    IReadOnlyList<RenameManifestEntry> Entries);
```

```csharp
// src/VideoShelf.Core/Renaming/RenameResult.cs
using System.Collections.Generic;

namespace VideoShelf.Core.Renaming;

public sealed record RenameResult(int Renamed, int Skipped, string? ManifestPath, IReadOnlyList<string> Errors);
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/VideoShelf.Core.Tests/RenameExecutorTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Models;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.Core.Tests;

public class RenameExecutorTests : IDisposable
{
    private readonly string _dir;
    private readonly VideoShelfDb _db;
    private readonly LibraryRepository _library;
    private readonly long _seriesId;
    private readonly long _v1;
    private readonly long _v2;

    public RenameExecutorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vs-exec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new VideoShelfDb(Path.Combine(_dir, "library.db"));
        _db.Migrate();
        _library = new LibraryRepository(_db);
        var src = _library.UpsertSource(@"C:\root", "Root");
        var sec = _library.UpsertSection(src, "S");
        _seriesId = _library.UpsertSeries(sec, "Show", false);
        _v1 = _library.UpsertVideo(_seriesId, @"C:\m\old1.mkv", 1, "mkv");
        _v2 = _library.UpsertVideo(_seriesId, @"C:\m\old2.mkv", 2, "mkv");
    }

    private (RenameExecutor exec, InMemoryFileSystem fs, RenamePlan plan) Setup()
    {
        var fs = new InMemoryFileSystem(@"C:\m\old1.mkv", @"C:\m\old2.mkv");
        var planner = new RenamePlanner(fs);
        var videos = _library.GetVideosForSeries(_seriesId);
        var proposed = new Dictionary<long, string> { [_v1] = "Show 01.mkv", [_v2] = "Show 02.mkv" };
        var plan = planner.BuildPlan(videos, proposed);
        return (new RenameExecutor(fs, _library), fs, plan);
    }

    [Fact]
    public void Apply_RenamesFiles_RepathsDb_AndWritesManifest()
    {
        var (exec, fs, plan) = Setup();
        var result = exec.Apply(plan, _seriesId, Path.Combine(_dir, "manifests"));

        result.Renamed.ShouldBe(2);
        result.ManifestPath.ShouldNotBeNull();
        fs.FileExists(@"C:\m\Show 01.mkv").ShouldBeTrue();
        fs.FileExists(@"C:\m\old1.mkv").ShouldBeFalse();

        var paths = _library.GetVideosForSeries(_seriesId).Select(v => v.FilePath).OrderBy(p => p).ToArray();
        paths.ShouldBe(new[] { @"C:\m\Show 01.mkv", @"C:\m\Show 02.mkv" });
    }

    [Fact]
    public void Undo_ReversesFiles_AndRepathsDbBack()
    {
        var (exec, fs, plan) = Setup();
        var result = exec.Apply(plan, _seriesId, Path.Combine(_dir, "manifests"));

        var undo = exec.Undo(result.ManifestPath!);

        undo.Renamed.ShouldBe(2);
        fs.FileExists(@"C:\m\old1.mkv").ShouldBeTrue();
        fs.FileExists(@"C:\m\Show 01.mkv").ShouldBeFalse();
        var paths = _library.GetVideosForSeries(_seriesId).Select(v => v.FilePath).OrderBy(p => p).ToArray();
        paths.ShouldBe(new[] { @"C:\m\old1.mkv", @"C:\m\old2.mkv" });
    }

    [Fact]
    public void Undo_IsTolerant_OfEntriesWhoseMoveNeverHappened()
    {
        var (exec, fs, plan) = Setup();
        var result = exec.Apply(plan, _seriesId, Path.Combine(_dir, "manifests"));
        // Simulate a partial batch: delete one renamed file before undo.
        fs.Move(@"C:\m\Show 02.mkv", @"C:\m\somewhere-else.mkv");

        var undo = exec.Undo(result.ManifestPath!);

        // Only the still-present rename is reversed; the other is skipped, no throw.
        undo.Renamed.ShouldBe(1);
        fs.FileExists(@"C:\m\old1.mkv").ShouldBeTrue();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }
}
```

- [ ] **Step 3: Run, expect FAIL** (`RenameExecutor` not defined).

- [ ] **Step 4: Implement `RenameExecutor`**

```csharp
// src/VideoShelf.Core/Renaming/RenameExecutor.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VideoShelf.Core.Storage;

namespace VideoShelf.Core.Renaming;

/// <summary>Executes a confirmed <see cref="RenamePlan"/>: writes an undo manifest first, then renames files on
/// disk and repaths the DB off stable video ids. Crash-safe and reversible by design.</summary>
public sealed class RenameExecutor(IFileSystem fs, LibraryRepository library)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public RenameResult Apply(RenamePlan plan, long seriesId, string manifestDirectory)
    {
        var ready = new List<RenameItem>();
        foreach (var i in plan.Items) if (i.WillRename) ready.Add(i);
        if (ready.Count == 0)
            return new RenameResult(0, plan.Items.Count, null, Array.Empty<string>());

        // Re-verify against "now" — fail safe if the disk changed since planning.
        var actionable = new List<RenameItem>();
        var errors = new List<string>();
        foreach (var i in ready)
        {
            if (!fs.FileExists(i.OldPath)) { errors.Add($"{i.OldName}: source missing at apply time"); continue; }
            if (fs.FileExists(i.NewPath)) { errors.Add($"{i.NewName}: target already exists at apply time"); continue; }
            actionable.Add(i);
        }
        if (actionable.Count == 0)
            return new RenameResult(0, plan.Items.Count, null, errors);

        // 1) Write the undo manifest BEFORE any move, so a crash mid-batch is recoverable.
        var batchId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
        var entries = new List<RenameManifestEntry>(actionable.Count);
        foreach (var i in actionable) entries.Add(new RenameManifestEntry(i.VideoId, i.OldPath, i.NewPath));
        var manifest = new RenameManifest(batchId, seriesId, DateTimeOffset.UtcNow.ToString("O"), entries);

        if (!fs.DirectoryExists(manifestDirectory)) fs.CreateDirectory(manifestDirectory);
        var manifestPath = Path.Combine(manifestDirectory, $"rename-{batchId}.json");
        WriteAtomic(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

        // 2) Move each file, then repath the DB off the stable video id.
        var renamed = 0;
        foreach (var i in actionable)
        {
            try { fs.Move(i.OldPath, i.NewPath); }      // 2-arg move never overwrites
            catch (Exception ex) { errors.Add($"{i.OldName} -> {i.NewName}: {ex.Message}"); continue; }
            library.UpdateVideoPath(i.VideoId, i.OldPath, i.NewPath);
            renamed++;
        }

        return new RenameResult(renamed, plan.Items.Count - renamed, manifestPath, errors);
    }

    /// <summary>Reverses the renames in a manifest: moves new->old where the new file still exists and old is free,
    /// repaths the DB back. Tolerant of partially-applied batches.</summary>
    public RenameResult Undo(string manifestPath)
    {
        if (!fs.FileExists(manifestPath))
            return new RenameResult(0, 0, manifestPath, new[] { "manifest not found" });

        var manifest = JsonSerializer.Deserialize<RenameManifest>(fs.ReadAllText(manifestPath), JsonOptions);
        if (manifest is null)
            return new RenameResult(0, 0, manifestPath, new[] { "manifest unreadable" });

        var reverted = 0;
        var skipped = 0;
        var errors = new List<string>();
        foreach (var e in manifest.Entries)
        {
            if (!fs.FileExists(e.NewPath)) { skipped++; continue; }                 // move never happened
            if (fs.FileExists(e.OldPath)) { skipped++; errors.Add($"{Path.GetFileName(e.OldPath)}: original path occupied"); continue; }
            try { fs.Move(e.NewPath, e.OldPath); }
            catch (Exception ex) { errors.Add($"undo {Path.GetFileName(e.NewPath)}: {ex.Message}"); continue; }
            library.UpdateVideoPath(e.VideoId, e.NewPath, e.OldPath);
            reverted++;
        }
        return new RenameResult(reverted, skipped, manifestPath, errors);
    }

    private void WriteAtomic(string path, string contents)
    {
        var tmp = path + ".tmp";
        fs.WriteAllText(tmp, contents);
        fs.Move(tmp, path); // batchId is unique, so path does not pre-exist
    }
}
```

- [ ] **Step 5: Run, expect PASS.**

- [ ] **Step 6: Commit**

```bash
git add src/VideoShelf.Core/Renaming/RenameManifest.cs src/VideoShelf.Core/Renaming/RenameResult.cs src/VideoShelf.Core/Renaming/RenameExecutor.cs tests/VideoShelf.Core.Tests/RenameExecutorTests.cs
git commit -m "feat(core): crash-safe RenameExecutor with undo manifest"
```

---

### Task 6: App view-models (`RenameRowViewModel`, `RenameToolViewModel`) + `AppPaths`

**Files:**
- Modify: `src/VideoShelf.App/Services/AppPaths.cs`
- Create: `src/VideoShelf.App/ViewModels/RenameRowViewModel.cs`
- Create: `src/VideoShelf.App/ViewModels/RenameToolViewModel.cs`
- Test: `tests/VideoShelf.App.Tests/RenameToolViewModelTests.cs`

- [ ] **Step 1: Add the manifest directory to `AppPaths`** (after `SeekPreviewDirectory`, line 23)

```csharp
    public string RenameManifestDirectory => Path.Combine(Root, "rename-manifests");
```

- [ ] **Step 2: Write `RenameRowViewModel`**

```csharp
// src/VideoShelf.App/ViewModels/RenameRowViewModel.cs
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.Core.Renaming;

namespace VideoShelf.App.ViewModels;

/// <summary>One editable row in the rename preview: current name, proposed name, resolved status.</summary>
public sealed partial class RenameRowViewModel : ObservableObject
{
    public long VideoId { get; }
    public int EpisodeNo { get; }
    public string OldName { get; }

    [ObservableProperty] private string _newName;
    [ObservableProperty] private RenameItemStatus _status;

    public event EventHandler? NewNameEdited;

    public RenameRowViewModel(long videoId, int episodeNo, string oldName, string proposedName, RenameItemStatus status)
    {
        VideoId = videoId;
        EpisodeNo = episodeNo;
        OldName = oldName;
        _newName = proposedName;
        _status = status;
    }

    public bool WillRename => Status == RenameItemStatus.Ready;

    public string StatusText => Status switch
    {
        RenameItemStatus.Ready => "Will rename",
        RenameItemStatus.Unchanged => "Unchanged",
        RenameItemStatus.SourceMissing => "Source missing",
        RenameItemStatus.TargetExists => "Target exists",
        RenameItemStatus.DuplicateTarget => "Duplicate target",
        RenameItemStatus.InvalidName => "Invalid name",
        _ => "",
    };

    partial void OnStatusChanged(RenameItemStatus value)
    {
        OnPropertyChanged(nameof(WillRename));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnNewNameChanged(string value) => NewNameEdited?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 3: Write the failing tests**

```csharp
// tests/VideoShelf.App.Tests/RenameToolViewModelTests.cs
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests;   // InMemoryFileSystem (see note below)
using Xunit;

namespace VideoShelf.App.Tests;

public class RenameToolViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly VideoShelfDb _db;
    private readonly LibraryRepository _library;
    private readonly SettingsRepository _settings;
    private readonly InMemoryFileSystem _fs;
    private readonly long _seriesId;

    public RenameToolViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vs-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new VideoShelfDb(Path.Combine(_dir, "library.db"));
        _db.Migrate();
        _library = new LibraryRepository(_db);
        _settings = new SettingsRepository(_db);
        var src = _library.UpsertSource(@"C:\root", "Root");
        var sec = _library.UpsertSection(src, "S");
        _seriesId = _library.UpsertSeries(sec, "My Show", false);
        _library.UpsertVideo(_seriesId, @"C:\m\junk_ep1.mkv", 1, "mkv");
        _library.UpsertVideo(_seriesId, @"C:\m\junk_ep2.mkv", 2, "mkv");
        _fs = new InMemoryFileSystem(@"C:\m\junk_ep1.mkv", @"C:\m\junk_ep2.mkv");
    }

    private RenameToolViewModel Build()
    {
        var planner = new RenamePlanner(_fs);
        var executor = new RenameExecutor(_fs, _library);
        var paths = new AppPaths(_dir);
        return new RenameToolViewModel(_library, planner, executor, _settings, paths);
    }

    [Fact]
    public async Task Load_BuildsCanonicalEditableProposals()
    {
        var vm = Build();
        await vm.LoadAsync(_seriesId, "My Show", isStandalone: false);

        vm.Rows.Count.ShouldBe(2);
        vm.Rows.Select(r => r.NewName).ShouldBe(new[] { "My Show 01.mkv", "My Show 02.mkv" });
        vm.Rows.All(r => r.WillRename).ShouldBeTrue();
    }

    [Fact]
    public async Task EditingName_ReplansAndFlagsDuplicate()
    {
        var vm = Build();
        await vm.LoadAsync(_seriesId, "My Show", false);
        vm.Rows[1].NewName = "My Show 01.mkv"; // collide with row 0

        vm.Rows[0].Status.ShouldBe(RenameItemStatus.DuplicateTarget);
        vm.Rows[1].Status.ShouldBe(RenameItemStatus.DuplicateTarget);
    }

    [Fact]
    public async Task Apply_RenamesOnDisk_RepathsDb_AndEnablesUndo()
    {
        var vm = Build();
        await vm.LoadAsync(_seriesId, "My Show", false);
        await vm.ApplyCommand.ExecuteAsync(null);

        _fs.FileExists(@"C:\m\My Show 01.mkv").ShouldBeTrue();
        _library.GetVideosForSeries(_seriesId).Select(v => Path.GetFileName(v.FilePath))
            .OrderBy(n => n).ShouldBe(new[] { "My Show 01.mkv", "My Show 02.mkv" });
        vm.CanUndo.ShouldBeTrue();
    }

    [Fact]
    public async Task Undo_RevertsDiskAndDb()
    {
        var vm = Build();
        await vm.LoadAsync(_seriesId, "My Show", false);
        await vm.ApplyCommand.ExecuteAsync(null);
        await vm.UndoCommand.ExecuteAsync(null);

        _fs.FileExists(@"C:\m\junk_ep1.mkv").ShouldBeTrue();
        vm.CanUndo.ShouldBeFalse();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
```

> **Cross-project test helper:** `InMemoryFileSystem` lives in `VideoShelf.Core.Tests`. To reference it from `VideoShelf.App.Tests`, add a project reference: in `tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj` add
> `<ProjectReference Include="..\VideoShelf.Core.Tests\VideoShelf.Core.Tests.csproj" />`.
> If that causes a duplicate-test or build issue, instead copy `InMemoryFileSystem.cs` into `tests/VideoShelf.App.Tests/` under namespace `VideoShelf.App.Tests` and adjust the `using`. STOP and report if neither builds cleanly.

- [ ] **Step 4: Run, expect FAIL** (`RenameToolViewModel` not defined).

- [ ] **Step 5: Implement `RenameToolViewModel`**

```csharp
// src/VideoShelf.App/ViewModels/RenameToolViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>Per-series opt-in rename tool: preview canonical (editable) names, confirm, defensive crash-safe
/// rename with an undo manifest, and one-click undo. The only feature that mutates video files.</summary>
public sealed partial class RenameToolViewModel : ObservableObject
{
    private const string LastManifestKey = "last_rename_manifest";

    private readonly LibraryRepository _library;
    private readonly RenamePlanner _planner;
    private readonly RenameExecutor _executor;
    private readonly SettingsRepository _settings;
    private readonly string _manifestDirectory;

    private long _seriesId;
    private bool _isStandalone;
    private string _baseTitle = "";
    private IReadOnlyList<Video> _videos = Array.Empty<Video>();
    private bool _suppressReplan;

    public RenameToolViewModel(
        LibraryRepository library,
        RenamePlanner planner,
        RenameExecutor executor,
        SettingsRepository settings,
        AppPaths paths)
    {
        _library = library;
        _planner = planner;
        _executor = executor;
        _settings = settings;
        _manifestDirectory = paths.RenameManifestDirectory;
    }

    public ObservableCollection<RenameRowViewModel> Rows { get; } = new();

    [ObservableProperty] private string _seriesTitle = "";
    [ObservableProperty] private string _statusSummary = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _canUndo;

    public event EventHandler? CloseRequested;

    public async Task LoadAsync(long seriesId, string baseTitle, bool isStandalone)
    {
        _seriesId = seriesId;
        _baseTitle = baseTitle;
        _isStandalone = isStandalone;
        SeriesTitle = baseTitle;

        _videos = await Task.Run(() => _library.GetVideosForSeries(seriesId));

        var standalone = _isStandalone || _videos.Count <= 1;
        var padWidth = CanonicalNamer.PadWidth(_videos.Select(v => v.EpisodeNo));

        _suppressReplan = true;
        Rows.Clear();
        foreach (var v in _videos)
        {
            var ext = Path.GetExtension(v.FilePath);
            var proposed = CanonicalNamer.Build(_baseTitle, standalone ? (int?)null : v.EpisodeNo, ext, padWidth);
            var row = new RenameRowViewModel(v.Id, v.EpisodeNo, Path.GetFileName(v.FilePath), proposed, RenameItemStatus.Ready);
            row.NewNameEdited += (_, _) => Replan();
            Rows.Add(row);
        }
        _suppressReplan = false;

        Replan();
        CanUndo = _settings.GetString(LastManifestKey, "").Length > 0;
    }

    private void Replan()
    {
        if (_suppressReplan) return;
        var proposed = Rows.ToDictionary(r => r.VideoId, r => r.NewName);
        var plan = _planner.BuildPlan(_videos, proposed);
        var byId = plan.Items.ToDictionary(i => i.VideoId, i => i.Status);
        foreach (var row in Rows)
            if (byId.TryGetValue(row.VideoId, out var status))
                row.Status = status;

        var ready = plan.ReadyCount;
        var blocked = Rows.Count(r => r.Status is RenameItemStatus.TargetExists
            or RenameItemStatus.DuplicateTarget or RenameItemStatus.SourceMissing or RenameItemStatus.InvalidName);
        StatusSummary = blocked > 0 ? $"{ready} to rename, {blocked} blocked" : $"{ready} to rename";
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private bool CanApply() => !IsBusy && Rows.Any(r => r.WillRename);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task Apply()
    {
        IsBusy = true;
        try
        {
            var proposed = Rows.ToDictionary(r => r.VideoId, r => r.NewName);
            var plan = _planner.BuildPlan(_videos, proposed);
            var result = await Task.Run(() => _executor.Apply(plan, _seriesId, _manifestDirectory));

            if (result.ManifestPath is not null)
                _settings.SetString(LastManifestKey, result.ManifestPath);

            StatusSummary = result.Errors.Count > 0
                ? $"Renamed {result.Renamed}; {result.Errors.Count} error(s)"
                : $"Renamed {result.Renamed} file(s)";

            await LoadAsync(_seriesId, _baseTitle, _isStandalone); // reflect disk truth
        }
        finally { IsBusy = false; }
    }

    private bool CanRunUndo() => !IsBusy && CanUndo;

    [RelayCommand(CanExecute = nameof(CanRunUndo))]
    private async Task Undo()
    {
        var manifestPath = _settings.GetString(LastManifestKey, "");
        if (manifestPath.Length == 0) return;

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _executor.Undo(manifestPath));
            _settings.SetString(LastManifestKey, ""); // consumed
            StatusSummary = $"Reverted {result.Renamed} file(s)";
            await LoadAsync(_seriesId, _baseTitle, _isStandalone);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    partial void OnCanUndoChanged(bool value) => UndoCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        ApplyCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }
}
```

- [ ] **Step 6: Run, expect PASS.**

Run: `dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/VideoShelf.App/Services/AppPaths.cs src/VideoShelf.App/ViewModels/RenameRowViewModel.cs src/VideoShelf.App/ViewModels/RenameToolViewModel.cs tests/VideoShelf.App.Tests/RenameToolViewModelTests.cs tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj
git commit -m "feat(app): RenameToolViewModel + editable rename rows"
```

---

### Task 7: Wire the entry point + navigation + DI

**Files:**
- Modify: `src/VideoShelf.App/ViewModels/SeriesViewModel.cs`
- Modify: `src/VideoShelf.App/ViewModels/SectionDetailViewModel.cs`
- Modify: `src/VideoShelf.App/ViewModels/MainViewModel.cs`
- Modify: `src/VideoShelf.App/Services/ServiceCollectionExtensions.cs`
- Test: `tests/VideoShelf.App.Tests/RenameNavigationTests.cs`

- [ ] **Step 1: Add the rename command to `SeriesViewModel`** (after the `PlayRequested` event, line 33)

```csharp
    public event System.EventHandler<SeriesViewModel>? RenameRequested;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void RequestRename() => RenameRequested?.Invoke(this, this);
```

> `SeriesViewModel.cs` does not currently import `CommunityToolkit.Mvvm.Input`; either add `using CommunityToolkit.Mvvm.Input;` at the top and use `[RelayCommand]`, or use the fully-qualified attribute as shown. The generated command is `RequestRenameCommand`.

- [ ] **Step 2: Bubble `RenameRequested` from `SectionDetailViewModel`**

Add the event (near `PlayRequested`, line 31):

```csharp
    public event EventHandler<SeriesViewModel>? RenameRequested;
```

In `LoadAsync`, where each `SeriesViewModel` is created and wired (inside the `foreach`, right after the existing `svm.PlayRequested += ...` on line 50), add:

```csharp
            svm.RenameRequested += (_, s) => RenameRequested?.Invoke(this, s);
```

- [ ] **Step 3: Add the nav host to `MainViewModel`**

Change the enum (line 11):

```csharp
public enum AppView { Home, Browse, SectionDetail, RenameTool }
```

Add the constructor parameter and wiring. Update the constructor signature to add `RenameToolViewModel renameTool` (place it last), store it, and wire its events. The full updated constructor:

```csharp
    public MainViewModel(
        SourcesViewModel sources,
        LibraryViewModel library,
        IScanCoordinator scanCoordinator,
        PlayerViewModel player,
        SettingsViewModel settings,
        DiscoveryViewModel discovery,
        SectionDetailViewModel sectionDetail,
        RenameToolViewModel renameTool)
    {
        _sources = sources;
        _library = library;
        _scanCoordinator = scanCoordinator;
        _player = player;
        _settings = settings;

        _library.PlayRequested += (_, ep) => PlayEpisode(ep);
        _player.NextEpisodeRequested += (_, ep) => PlayEpisode(ep);

        Discovery = discovery;
        SectionDetail = sectionDetail;
        RenameTool = renameTool;
        Discovery.PlayRequested += (_, e) => PlayEpisode(e);
        Discovery.SectionOpenRequested += async (_, id) => await OpenSectionAsync(id);
        SectionDetail.PlayRequested += (_, e) => PlayEpisode(e);
        SectionDetail.RenameRequested += async (_, s) => await OpenRenameToolAsync(s);
        RenameTool.CloseRequested += (_, _) => CurrentView = AppView.SectionDetail;
    }
```

Add the property (next to `SectionDetail`, line 53):

```csharp
    public RenameToolViewModel RenameTool { get; }
```

Add the open method (e.g. after `OpenSectionAsync`, line 90):

```csharp
    public async Task OpenRenameToolAsync(SeriesViewModel series)
    {
        await RenameTool.LoadAsync(series.SeriesId, series.BaseTitle, series.IsStandalone);
        CurrentView = AppView.RenameTool;
    }
```

- [ ] **Step 4: Register services in DI** (`ServiceCollectionExtensions.cs`, after the `TagRepository`/`DiscoveryRepository` block, before `MainViewModel`)

```csharp
        services.AddSingleton<IFileSystem, RealFileSystem>();
        services.AddSingleton<RenamePlanner>();
        services.AddSingleton<RenameExecutor>();
        services.AddSingleton<RenameToolViewModel>();
```

Add the using at the top of the file:

```csharp
using VideoShelf.Core.Renaming;
```

- [ ] **Step 5: Write the navigation test**

```csharp
// tests/VideoShelf.App.Tests/RenameNavigationTests.cs
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using Xunit;

namespace VideoShelf.App.Tests;

public class RenameNavigationTests
{
    [Fact]
    public void DiContainer_ResolvesMainViewModel_WithRenameToolWired()
    {
        // Mirrors how the app composes services; if AddVideoShelf needs a real DB path it should
        // already be covered by existing App.Tests DI tests — follow their pattern if this differs.
        var services = new ServiceCollection().AddVideoShelf().BuildServiceProvider();
        var main = services.GetRequiredService<MainViewModel>();
        main.RenameTool.ShouldNotBeNull();
        main.CurrentView.ShouldBe(AppView.Home);
    }
}
```

> If `AddVideoShelf()` cannot be built in a test without a writable DB/library path (it constructs `LibraryBootstrap`/`VideoShelfDb`), check how existing App.Tests exercise DI. If there is no precedent for resolving the whole graph in tests, **replace this test** with a direct `MainViewModel` construction using fakes/in-memory repos that the other App.Tests already use, asserting `OpenRenameToolAsync` sets `CurrentView == AppView.RenameTool`. STOP and report if the existing DI test pattern is unclear.

- [ ] **Step 6: Run the full suite, expect PASS**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/VideoShelf.App/ViewModels/SeriesViewModel.cs src/VideoShelf.App/ViewModels/SectionDetailViewModel.cs src/VideoShelf.App/ViewModels/MainViewModel.cs src/VideoShelf.App/Services/ServiceCollectionExtensions.cs tests/VideoShelf.App.Tests/RenameNavigationTests.cs
git commit -m "feat(app): wire rename tool entry point + RenameTool nav host + DI"
```

---

### Task 8: Views (`RenameToolView` + host + entry button) — XAML only

> Views are integration UI: no unit tests. They are verified in the Phase 6 retroactive screenshot sweep (per the ROADMAP). After this task, run the app once manually if convenient, but CI only needs a clean build.

**Files:**
- Create: `src/VideoShelf.App/Views/RenameToolView.xaml`
- Create: `src/VideoShelf.App/Views/RenameToolView.xaml.cs`
- Modify: `src/VideoShelf.App/Views/MainWindow.xaml`
- Modify: `src/VideoShelf.App/Views/SectionDetailView.xaml`

- [ ] **Step 1: Create `RenameToolView.xaml`**

```xml
<UserControl x:Class="VideoShelf.App.Views.RenameToolView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
             xmlns:conv="clr-namespace:VideoShelf.App.Converters">
    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/VideoShelf.App;component/Resources/DesignTokens.xaml" />
            </ResourceDictionary.MergedDictionaries>
            <conv:BoolToVisibility x:Key="BoolToVisibility" />
        </ResourceDictionary>
    </UserControl.Resources>

    <DockPanel Margin="24">
        <!-- Header -->
        <StackPanel DockPanel.Dock="Top" Margin="0,0,0,12">
            <TextBlock FontSize="22" FontWeight="SemiBold">
                <Run Text="Rename: " /><Run Text="{Binding SeriesTitle}" />
            </TextBlock>
            <TextBlock Opacity="0.75" TextWrapping="Wrap" Margin="0,4,0,0"
                       Text="Preview the new file names, edit any you like, then Apply. This renames files on disk and is reversible with Undo." />
        </StackPanel>

        <!-- Action bar -->
        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" Margin="0,12,0,0">
            <ui:Button Content="Apply rename" Appearance="Primary" Command="{Binding ApplyCommand}" />
            <ui:Button Content="Undo last rename" Margin="8,0,0,0" Command="{Binding UndoCommand}" />
            <ui:Button Content="Back" Margin="8,0,0,0" Command="{Binding CloseCommand}" />
            <TextBlock Text="{Binding StatusSummary}" VerticalAlignment="Center" Margin="16,0,0,0" Opacity="0.85" />
            <ui:ProgressRing IsIndeterminate="True" Width="20" Height="20" Margin="12,0,0,0"
                             Visibility="{Binding IsBusy, Converter={StaticResource BoolToVisibility}}" />
        </StackPanel>

        <!-- Rows -->
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <ItemsControl ItemsSource="{Binding Rows}"
                          VirtualizingStackPanel.IsVirtualizing="True"
                          VirtualizingStackPanel.VirtualizationMode="Recycling">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Margin="0,0,0,6" Padding="10"
                                Background="{StaticResource SubtleFillBrush}"
                                CornerRadius="{StaticResource CardRadius}">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="140" />
                                </Grid.ColumnDefinitions>
                                <TextBlock Grid.Column="0" Text="{Binding OldName}" VerticalAlignment="Center"
                                           Opacity="0.8" TextTrimming="CharacterEllipsis" />
                                <TextBlock Grid.Column="1" Text="→" Margin="10,0" VerticalAlignment="Center" Opacity="0.6" />
                                <ui:TextBox Grid.Column="2" Text="{Binding NewName, UpdateSourceTrigger=PropertyChanged}" />
                                <TextBlock Grid.Column="3" Text="{Binding StatusText}" VerticalAlignment="Center"
                                           Margin="10,0,0,0" Opacity="0.85" />
                            </Grid>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

- [ ] **Step 2: Create `RenameToolView.xaml.cs`**

```csharp
using System.Windows.Controls;

namespace VideoShelf.App.Views;

public partial class RenameToolView : UserControl
{
    public RenameToolView() => InitializeComponent();
}
```

- [ ] **Step 3: Add the host to `MainWindow.xaml`** — insert immediately after the `SectionDetailView` host (after line 232, before the closing `</Grid>` of the nav-gated hosts on line 234):

```xml
                    <!-- Rename tool view -->
                    <views:RenameToolView DataContext="{Binding RenameTool}"
                                          Visibility="{Binding CurrentView,
                                              RelativeSource={RelativeSource AncestorType=Window},
                                              Converter={StaticResource EnumToVis},
                                              ConverterParameter=RenameTool}" />
```

- [ ] **Step 4: Add the "Rename files…" entry button to `SectionDetailView.xaml`** — in the series-card header `DockPanel` (the one starting line 88), add as the **first** child (so it docks right of the card, before the unwatched badge):

```xml
                                        <ui:Button DockPanel.Dock="Right" Content="Rename files…"
                                                   Padding="8,2" Margin="8,0,0,0"
                                                   Command="{Binding RequestRenameCommand}" />
```

So the header `DockPanel` becomes:

```xml
                                    <DockPanel>
                                        <ui:Button DockPanel.Dock="Right" Content="Rename files…"
                                                   Padding="8,2" Margin="8,0,0,0"
                                                   Command="{Binding RequestRenameCommand}" />
                                        <Border DockPanel.Dock="Right" Background="{StaticResource AccentBrush}"
                                                CornerRadius="{StaticResource ControlRadius}"
                                                Padding="6,1"
                                                Visibility="{Binding HasUnwatched, Converter={StaticResource BoolToVisibility}}">
                                            <TextBlock FontSize="11" Foreground="#101010">
                                                <Run Text="{Binding UnwatchedCount, Mode=OneWay}" />
                                                <Run Text=" unwatched" />
                                            </TextBlock>
                                        </Border>
                                        <TextBlock Text="{Binding BaseTitle}" FontWeight="SemiBold" />
                                    </DockPanel>
```

> The `DataContext` of this `DataTemplate` is a `SeriesViewModel`, so `RequestRenameCommand` binds directly. `BoolToVisibility` is already merged in `MainWindow.xaml`'s resources and applies to the hosted `SectionDetailView` content; it is already used elsewhere in this file (line 92), so no new resource is needed.

- [ ] **Step 5: Build, expect success**

Run: `dotnet build VideoShelf.slnx -c Release --nologo -v q`
Expected: build succeeds (XAML compiles, `RequestRenameCommand`/`RenameTool` bindings resolve at compile-time for x:Class members; runtime bindings are checked in Phase 6).

- [ ] **Step 6: Run the full suite once more**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: PASS (all prior tests green; no new test failures from the XAML changes).

- [ ] **Step 7: Commit**

```bash
git add src/VideoShelf.App/Views/RenameToolView.xaml src/VideoShelf.App/Views/RenameToolView.xaml.cs src/VideoShelf.App/Views/MainWindow.xaml src/VideoShelf.App/Views/SectionDetailView.xaml
git commit -m "feat(app): RenameToolView + nav host + section-detail entry button"
```

---

## Verification (run before opening the PR)

- [ ] `dotnet build VideoShelf.slnx -c Release --nologo -v q` — clean build.
- [ ] `dotnet test VideoShelf.slnx -c Release --nologo -v q` — all green. Expected count ≈ **177 baseline + ~20 new** (Core: in-memory fs, CanonicalNamer ×7, planner ×6, UpdateVideoPath ×1, executor ×3; App: RenameToolViewModel ×4, nav ×1). Exact totals may vary; the gate is **zero failures**, not a specific number.
- [ ] Manually confirm (optional, not required for CI): launch the app, open a section → a series, click "Rename files…", verify the preview shows canonical names, Apply renames on disk, Undo reverts. **Full visual verification is deferred to Phase 6** (no launch hooks/fixture exist yet — documented ROADMAP decision).

## Self-review checklist (run after writing code, before PR)

1. **Spec coverage:** preview diff ✓ (Task 6/8), explicit confirm ✓ (Apply button), defensive crash-safe rename ✓ (Task 5, manifest-first + 2-arg move + re-verify), undo manifest ✓ (Task 5), in-app undo ✓ (Task 6/8), DB repath off stable id with watched/resume/tags surviving ✓ (Task 4), per-series scope ✓, editable canonical names ✓ (Task 2/6).
2. **Type consistency:** `RenameItemStatus`, `RenameItem`, `RenamePlan`, `RenameResult`, `RenameManifest(Entry)` are referenced identically across Core + App. `UpdateVideoPath(long,string,string)`, `Apply(plan, seriesId, dir)`, `Undo(manifestPath)`, `LoadAsync(seriesId, baseTitle, isStandalone)`, settings key `last_rename_manifest` — all consistent.
3. **No placeholders:** every step has concrete code/commands.

## Notes for the executor

- Work on branch `feat/rename-tool` in a worktree under `.worktrees/` (see runbook). Open the PR with `& "C:\Program Files\GitHub CLI\gh.exe" pr create ...`, watch CI (`build-and-test`) in the foreground, then `gh pr merge <PR#> --merge --delete-branch` **from the main repo root**. Commit author `yovanmc` + `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- This milestone's ROADMAP row flip to ✅ Merged rides on this branch (per convention), and so does flipping M5 from `📝 Plan ready` → `✅ Merged` with the PR number and a one-line summary, plus a decision-log entry capturing any gotchas (e.g. the cross-project test-helper reference resolution).
