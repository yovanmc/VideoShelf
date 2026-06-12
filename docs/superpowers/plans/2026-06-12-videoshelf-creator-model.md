# M7 — Creator model + card system + shell foundation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Written for Sonnet execution. If something in the codebase doesn't match what this plan says (a signature, a property name, a XAML structure), STOP and report rather than guessing.** This plan was written from a digest; a handful of integration points are flagged "VERIFY — STOP-and-report if it differs."

**Goal:** Re-present the library around "creators" (= today's Sections, relabeled) with a reusable card system and a cohesive nav design language — the foundation milestones M8–M10 build on.

**Architecture:** The `sections` table and four-level hierarchy are **unchanged**; "creator" is a presentation concept over the existing Section row. Core gains a richer creator read-model (total video count + a thumbnail seed path) and a `creator_art` override table. App gains a reusable `CreatorCard` (consumed by a new Browse-as-creator-grid) and a `VideoCard` (extracted from the existing inline Home-rail card, so the rails are now DRY and reusable), an in-app `IImagePicker` for the override, and a restyled nav chrome. All logic lives in plain repos/VMs unit-tested with fakes; concrete picker + UserControls are integration-only (screenshot-verified).

**Tech Stack:** .NET 10, WPF, WPF-UI (`ui:` controls), LibVLCSharp (already wrapped), Microsoft.Data.Sqlite, CommunityToolkit.Mvvm (`[ObservableProperty]`/`[RelayCommand]`), xUnit + Shouldly.

**Conventions (from ROADMAP.md):**
- Build: `dotnet build VideoShelf.slnx -c Release --nologo -v q`
- Test gate: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
- `gh` is **not on PATH** → `& "C:\Program Files\GitHub CLI\gh.exe"`.
- Work on a branch; **direct pushes to `main` are blocked** — ship via PR, `--merge` (no squash). `gh pr merge` from the **main repo root**, not a worktree.
- Commits: use the repo's configured author (`yovanmc`; **do not override `user.email`**), plain `git commit`, with trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. **No Codex trailer.**
- DB convention: repos open a connection per call via `db.Open()`; parameters are **`$`-prefixed**. Migrations are idempotent (`CREATE TABLE IF NOT EXISTS` in the `Schema` const; column adds via the `EnsureColumn` `pragma_table_info` guard).
- Theming rule: **never** re-base a WPF-UI control's Style/ControlTemplate for cosmetics — additive (new keys, Opacity/RenderTransform, new UserControls) only.
- After all tasks: full screenshot sweep (Task 11) verified by a **Sonnet subagent returning a TEXT verdict**, never load PNGs into the controller.

---

## File Structure

**Create:**
- `src/VideoShelf.App/Services/ImagePicker.cs` — `IImagePicker` + `ImagePicker` (OS image dialog).
- `src/VideoShelf.Core/Storage/CreatorArtRepository.cs` — get/set/clear the per-creator art override path.
- `src/VideoShelf.App/ViewModels/CreatorCardViewModel.cs` — one creator card (name, "N videos", resolved image, open command).
- `src/VideoShelf.App/ViewModels/CreatorsViewModel.cs` — loads the creator grid for Browse.
- `src/VideoShelf.App/Views/CreatorCard.xaml` (+ `.xaml.cs`) — reusable creator card control.
- `src/VideoShelf.App/Views/VideoCard.xaml` (+ `.xaml.cs`) — reusable video card control (extracted from the Home rail).
- `tests/VideoShelf.Core.Tests/Storage/CreatorArtRepositoryTests.cs`
- `tests/VideoShelf.App.Tests/CreatorCardViewModelTests.cs`
- `tests/VideoShelf.App.Tests/CreatorsViewModelTests.cs`

**Modify:**
- `src/VideoShelf.Core/Models/BrowseModels.cs` — extend `SectionSummary`.
- `src/VideoShelf.Core/Storage/LibraryRepository.cs` — update `GetSectionSummaries()` SQL.
- `src/VideoShelf.Core/Storage/VideoShelfDb.cs` — add `creator_art` to the `Schema` const.
- `src/VideoShelf.App/ViewModels/SectionDetailViewModel.cs` — creator-art override commands.
- `src/VideoShelf.App/ViewModels/LibraryViewModel.cs` — fix the `_pending` race.
- `src/VideoShelf.App/Views/MainWindow.xaml` — Browse-as-creator-grid + restyled nav + "Creator(s)" labels.
- `src/VideoShelf.App/Views/SectionDetailView.xaml` — override buttons + "Creator" labels.
- `src/VideoShelf.App/Views/DiscoveryView.xaml` — re-point the continue-watching rail at `VideoCard`.
- `src/VideoShelf.App/Resources/DesignTokens.xaml` — additive card/nav tokens.
- The DI registration file (**VERIFY** — likely `src/VideoShelf.App/Services/ServiceCollectionExtensions.cs` where `AddVideoShelf` lives) — register `IImagePicker`, `CreatorArtRepository`, `CreatorsViewModel`, and the new `SectionDetailViewModel` deps.
- Existing App.Tests construction sites for `SectionDetailViewModel` / `MainViewModel` (expected fan-out when ctors gain a param).

---

## Task 1: Extend the creator read-model (Core)

Add total **video count** and a **thumbnail seed path** to `SectionSummary` so a creator card can show "N videos" and resolve a representative frame.

**Files:**
- Modify: `src/VideoShelf.Core/Models/BrowseModels.cs`
- Modify: `src/VideoShelf.Core/Storage/LibraryRepository.cs` (`GetSectionSummaries`)
- Test: `tests/VideoShelf.Core.Tests/Storage/LibraryRepositoryTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `tests/VideoShelf.Core.Tests/Storage/LibraryRepositoryTests.cs` (namespace `VideoShelf.Core.Tests.Storage`):

```csharp
[Fact]
public void GetSectionSummaries_reports_total_video_count_and_a_seed_path()
{
    using var temp = new TempDb();
    var repo = new LibraryRepository(temp.Db);

    var sourceId = repo.UpsertSource(@"C:\Vids", "Vids");
    var sectionId = repo.UpsertSection(sourceId, "Creator A");
    var seriesId = repo.UpsertSeries(sectionId, "Cool Story", isStandalone: false);
    repo.UpsertVideo(seriesId, @"C:\Vids\Creator A\Cool Story 1.mp4", episodeNo: 1, format: ".mp4");
    repo.UpsertVideo(seriesId, @"C:\Vids\Creator A\Cool Story 2.mp4", episodeNo: 2, format: ".mp4");

    var summary = repo.GetSectionSummaries().Single(s => s.SectionId == sectionId);

    summary.VideoCount.ShouldBe(2);
    summary.ThumbnailSeedPath.ShouldNotBeNull();
    summary.ThumbnailSeedPath!.ShouldEndWith(".mp4");
}
```

> **VERIFY:** confirm the exact signatures of `UpsertSource`/`UpsertSection`/`UpsertSeries`/`UpsertVideo` against `LibraryRepository.cs` (the digest shows `UpsertVideo(seriesId, path, episodeNo, format)`). If they differ, adapt the test setup — STOP-and-report if the shape is materially different.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: FAIL — `SectionSummary` has no `VideoCount`/`ThumbnailSeedPath` (compile error).

- [ ] **Step 3: Extend the record**

In `src/VideoShelf.Core/Models/BrowseModels.cs`, replace the `SectionSummary` definition:

```csharp
public sealed record SectionSummary(
    long SectionId,
    long SourceId,
    string DisplayName,
    int SeriesCount,
    int UnwatchedCount,
    int VideoCount,
    string? ThumbnailSeedPath);
```

- [ ] **Step 4: Update the query**

In `src/VideoShelf.Core/Storage/LibraryRepository.cs`, update `GetSectionSummaries()`. Replace the SQL and the row mapping so it also computes a total video count and a representative seed path (the first non-missing video by series/episode order):

```csharp
public IReadOnlyList<SectionSummary> GetSectionSummaries()
{
    using var conn = db.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT sc.id, sc.source_id, sc.display_name,
               COUNT(DISTINCT se.id) AS series_count,
               COALESCE(SUM(CASE WHEN v.id IS NOT NULL AND v.watched = 0 THEN 1 ELSE 0 END), 0) AS unwatched,
               COUNT(v.id) AS video_count,
               (SELECT v2.file_path
                  FROM videos v2
                  JOIN series se2 ON se2.id = v2.series_id
                 WHERE se2.section_id = sc.id AND v2.missing = 0
                 ORDER BY se2.id, v2.episode_no
                 LIMIT 1) AS seed_path
        FROM sections sc
        LEFT JOIN series se ON se.section_id = sc.id
        LEFT JOIN videos v ON v.series_id = se.id
        GROUP BY sc.id, sc.source_id, sc.display_name
        ORDER BY sc.display_name
        """;

    var list = new List<SectionSummary>();
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        list.Add(new SectionSummary(
            SectionId: reader.GetInt64(0),
            SourceId: reader.GetInt64(1),
            DisplayName: reader.GetString(2),
            SeriesCount: reader.GetInt32(3),
            UnwatchedCount: reader.GetInt32(4),
            VideoCount: reader.GetInt32(5),
            ThumbnailSeedPath: reader.IsDBNull(6) ? null : reader.GetString(6)));
    }
    return list;
}
```

> **VERIFY:** match the existing method's connection/reader idiom (the digest shows `db.Open()` + a `SqliteDataReader`). Keep the existing field order for the first five args so any positional consumers still line up; the two new args go last (matching Step 3). If other call sites construct `SectionSummary` positionally, the compiler will flag them — fix each by passing the existing values plus `videoCount`/`seedPath` (or `0`/`null` in tests that don't care).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: PASS (the new test + all existing). Fix any positional-construction compile errors in existing tests by appending the two new args.

- [ ] **Step 6: Commit**

```bash
git add src/VideoShelf.Core/Models/BrowseModels.cs src/VideoShelf.Core/Storage/LibraryRepository.cs tests/VideoShelf.Core.Tests/Storage/LibraryRepositoryTests.cs
git commit -m "feat(core): add video count + thumbnail seed to creator read-model"
```

---

## Task 2: `creator_art` override table + repository (Core)

A new **DB-only** mutation: store a path to a user-chosen image per creator (section). Never writes into library folders.

**Files:**
- Modify: `src/VideoShelf.Core/Storage/VideoShelfDb.cs` (`Schema` const)
- Create: `src/VideoShelf.Core/Storage/CreatorArtRepository.cs`
- Test: `tests/VideoShelf.Core.Tests/Storage/CreatorArtRepositoryTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.Core.Tests/Storage/CreatorArtRepositoryTests.cs`:

```csharp
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class CreatorArtRepositoryTests
{
    [Fact]
    public void Get_returns_null_when_no_override_set()
    {
        using var temp = new TempDb();
        var art = new CreatorArtRepository(temp.Db);

        art.GetArtPath(42).ShouldBeNull();
    }

    [Fact]
    public void Set_then_Get_round_trips_and_Set_overwrites()
    {
        using var temp = new TempDb();
        var art = new CreatorArtRepository(temp.Db);

        art.SetArtPath(7, @"C:\pics\a.png");
        art.GetArtPath(7).ShouldBe(@"C:\pics\a.png");

        art.SetArtPath(7, @"C:\pics\b.jpg");
        art.GetArtPath(7).ShouldBe(@"C:\pics\b.jpg");
    }

    [Fact]
    public void Clear_removes_the_override()
    {
        using var temp = new TempDb();
        var art = new CreatorArtRepository(temp.Db);

        art.SetArtPath(7, @"C:\pics\a.png");
        art.ClearArtPath(7);

        art.GetArtPath(7).ShouldBeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: FAIL — `CreatorArtRepository` does not exist (compile error).

- [ ] **Step 3: Add the table to the schema**

In `src/VideoShelf.Core/Storage/VideoShelfDb.cs`, inside the `Schema` const string (which is a series of `CREATE TABLE IF NOT EXISTS …` statements run by `Migrate()`), append:

```sql
CREATE TABLE IF NOT EXISTS creator_art (
    section_id INTEGER NOT NULL PRIMARY KEY REFERENCES sections(id) ON DELETE CASCADE,
    image_path TEXT NOT NULL
);
```

> Because `Migrate()` runs the whole `Schema` const (all `IF NOT EXISTS`), this is automatically idempotent for both fresh and existing DBs — no `EnsureColumn`/`EnsureTable` call needed. **VERIFY** the `Schema` const is a multi-statement string executed once; if instead each table is a separate `ExecuteNonQuery`, add this as one more statement in the same place.

- [ ] **Step 4: Write the repository**

Create `src/VideoShelf.Core/Storage/CreatorArtRepository.cs`:

```csharp
namespace VideoShelf.Core.Storage;

/// <summary>
/// Per-creator (section) art override. DB-only: stores a path to a user-chosen
/// image; never copies into or writes to library folders.
/// </summary>
public sealed class CreatorArtRepository(VideoShelfDb db)
{
    public string? GetArtPath(long sectionId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT image_path FROM creator_art WHERE section_id = $id";
        cmd.Parameters.AddWithValue("$id", sectionId);
        var result = cmd.ExecuteScalar();
        return result is string s ? s : null;
    }

    public void SetArtPath(long sectionId, string imagePath)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO creator_art (section_id, image_path) VALUES ($id, $path)
            ON CONFLICT(section_id) DO UPDATE SET image_path = excluded.image_path
            """;
        cmd.Parameters.AddWithValue("$id", sectionId);
        cmd.Parameters.AddWithValue("$path", imagePath);
        cmd.ExecuteNonQuery();
    }

    public void ClearArtPath(long sectionId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM creator_art WHERE section_id = $id";
        cmd.Parameters.AddWithValue("$id", sectionId);
        cmd.ExecuteNonQuery();
    }
}
```

> **VERIFY** the constructor idiom: other repos take `VideoShelfDb db` and call `db.Open()`. If they instead take a connection factory or `string` path, mirror that exact shape.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/VideoShelf.Core/Storage/VideoShelfDb.cs src/VideoShelf.Core/Storage/CreatorArtRepository.cs tests/VideoShelf.Core.Tests/Storage/CreatorArtRepositoryTests.cs
git commit -m "feat(core): add creator_art override table + repository"
```

---

## Task 3: In-app image picker seam (App)

Mirror the existing `IFolderPicker`/`FolderPicker` for choosing an image file. Concrete impl is integration-only (no unit test); a fake is used by VM tests.

**Files:**
- Create: `src/VideoShelf.App/Services/ImagePicker.cs`

- [ ] **Step 1: Write the seam + concrete impl**

Create `src/VideoShelf.App/Services/ImagePicker.cs`:

```csharp
using Microsoft.Win32;

namespace VideoShelf.App.Services;

public interface IImagePicker
{
    /// <summary>Returns the chosen image path, or null if cancelled.</summary>
    string? PickImage(string? initialFolder = null);
}

public sealed class ImagePicker : IImagePicker
{
    public string? PickImage(string? initialFolder = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select creator image",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            Multiselect = false,
            CheckFileExists = true,
        };
        if (!string.IsNullOrWhiteSpace(initialFolder))
            dialog.InitialDirectory = initialFolder;

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
```

> **VERIFY** the existing `FolderPicker.cs` namespace/location (digest shows `VideoShelf.App.Services` and `Microsoft.Win32.OpenFolderDialog`). Match it exactly.

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build VideoShelf.slnx -c Release --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/VideoShelf.App/Services/ImagePicker.cs
git commit -m "feat(app): add IImagePicker seam for creator-art override"
```

---

## Task 4: `CreatorCardViewModel` — one creator card (App)

Wraps a `SectionSummary` + resolves its image (override → representative seed frame) + exposes a "N videos" label and an open command.

**Files:**
- Create: `src/VideoShelf.App/ViewModels/CreatorCardViewModel.cs`
- Test: `tests/VideoShelf.App.Tests/CreatorCardViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/VideoShelf.App.Tests/CreatorCardViewModelTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;

namespace VideoShelf.App.Tests;

public class CreatorCardViewModelTests
{
    private sealed class StubThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(videoPath + ".thumb.png");
    }

    private static SectionSummary Summary(long id = 1, int videos = 3, string? seed = @"C:\v\a.mp4")
        => new(SectionId: id, SourceId: 1, DisplayName: "Creator A",
               SeriesCount: 1, UnwatchedCount: 1, VideoCount: videos, ThumbnailSeedPath: seed);

    [Fact]
    public void Exposes_name_and_video_count_label()
    {
        var vm = new CreatorCardViewModel(Summary(videos: 5), overrideArtPath: null, new StubThumbs());

        vm.Name.ShouldBe("Creator A");
        vm.VideoCountLabel.ShouldBe("5 videos");
    }

    [Fact]
    public void Single_video_label_is_singular()
    {
        var vm = new CreatorCardViewModel(Summary(videos: 1), overrideArtPath: null, new StubThumbs());

        vm.VideoCountLabel.ShouldBe("1 video");
    }

    [Fact]
    public async Task Override_art_wins_over_seed_frame()
    {
        var vm = new CreatorCardViewModel(Summary(), overrideArtPath: @"C:\pics\custom.png", new StubThumbs());

        await vm.LoadImageAsync(CancellationToken.None);

        vm.ImagePath.ShouldBe(@"C:\pics\custom.png");
    }

    [Fact]
    public async Task Falls_back_to_representative_frame_when_no_override()
    {
        var vm = new CreatorCardViewModel(Summary(seed: @"C:\v\a.mp4"), overrideArtPath: null, new StubThumbs());

        await vm.LoadImageAsync(CancellationToken.None);

        vm.ImagePath.ShouldBe(@"C:\v\a.mp4.thumb.png");
    }

    [Fact]
    public async Task No_image_when_no_override_and_no_seed()
    {
        var vm = new CreatorCardViewModel(Summary(seed: null), overrideArtPath: null, new StubThumbs());

        await vm.LoadImageAsync(CancellationToken.None);

        vm.ImagePath.ShouldBeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: FAIL — `CreatorCardViewModel` does not exist.

- [ ] **Step 3: Write the view-model**

Create `src/VideoShelf.App/ViewModels/CreatorCardViewModel.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;

namespace VideoShelf.App.ViewModels;

public partial class CreatorCardViewModel : ObservableObject
{
    private readonly SectionSummary _summary;
    private readonly string? _overrideArtPath;
    private readonly IThumbnailService _thumbnails;

    public CreatorCardViewModel(SectionSummary summary, string? overrideArtPath, IThumbnailService thumbnails)
    {
        _summary = summary;
        _overrideArtPath = overrideArtPath;
        _thumbnails = thumbnails;
    }

    public long SectionId => _summary.SectionId;
    public string Name => _summary.DisplayName;
    public int VideoCount => _summary.VideoCount;
    public string VideoCountLabel => $"{VideoCount} {(VideoCount == 1 ? "video" : "videos")}";

    [ObservableProperty]
    private string? _imagePath;

    /// <summary>Raised when the card is activated; the host opens the creator page.</summary>
    public event Action<long>? OpenRequested;

    [RelayCommand]
    private void Open() => OpenRequested?.Invoke(_summary.SectionId);

    /// <summary>Resolve the card image: user override wins, else representative frame.</summary>
    public async Task LoadImageAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_overrideArtPath))
        {
            ImagePath = _overrideArtPath;
            return;
        }

        if (string.IsNullOrWhiteSpace(_summary.ThumbnailSeedPath))
        {
            ImagePath = null;
            return;
        }

        // Fail-safe: thumbnail service never throws, returns null on failure.
        ImagePath = await _thumbnails.GetThumbnailPathAsync(_summary.ThumbnailSeedPath!, ct);
    }
}
```

> **VERIFY** the exact `IThumbnailService` signature in the App project (digest: `Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)`). If the parameter name/order differs, match it.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/VideoShelf.App/ViewModels/CreatorCardViewModel.cs tests/VideoShelf.App.Tests/CreatorCardViewModelTests.cs
git commit -m "feat(app): CreatorCardViewModel with override-wins image resolution"
```

---

## Task 5: `CreatorsViewModel` — the Browse creator grid (App)

Loads all creators (`GetSectionSummaries`) into `CreatorCardViewModel`s for the Browse grid, wiring each card's `OpenRequested` to a callback the host (MainViewModel) supplies.

**Files:**
- Create: `src/VideoShelf.App/ViewModels/CreatorsViewModel.cs`
- Test: `tests/VideoShelf.App.Tests/CreatorsViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.App.Tests/CreatorsViewModelTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.App.Tests;

public class CreatorsViewModelTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task Loads_one_card_per_creator_with_counts_and_open_callback()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Story 1.mp4");
        dir.Touch("Creator A/Story 2.mp4");
        dir.Touch("Creator B/Clip.mp4");

        var lib = new LibraryRepository(temp.Db);
        var art = new CreatorArtRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");

        var opened = new List<long>();
        var vm = new CreatorsViewModel(lib, art, new NullThumbs());
        vm.OpenCreatorRequested += id => opened.Add(id);

        await vm.LoadAsync(CancellationToken.None);

        vm.Creators.Select(c => c.Name).ShouldBe(new[] { "Creator A", "Creator B" });
        vm.Creators.Single(c => c.Name == "Creator A").VideoCountLabel.ShouldBe("2 videos");

        vm.Creators.First().OpenCommand.Execute(null);
        opened.ShouldContain(vm.Creators.First().SectionId);
    }
}
```

> **VERIFY** `AppTempDb`, `TempDir.Touch(relativePath)`, and `ScanService(db, lib).ScanSource(path, name)` against the App.Tests/Core.Tests TestSupport (digest confirms all three). The scanner is **flat**: each immediate subfolder = a section/creator; series group from filenames by first integer token.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: FAIL — `CreatorsViewModel` does not exist.

- [ ] **Step 3: Write the view-model**

Create `src/VideoShelf.App/ViewModels/CreatorsViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.App.Services;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public partial class CreatorsViewModel : ObservableObject
{
    private readonly LibraryRepository _library;
    private readonly CreatorArtRepository _art;
    private readonly IThumbnailService _thumbnails;

    public CreatorsViewModel(LibraryRepository library, CreatorArtRepository art, IThumbnailService thumbnails)
    {
        _library = library;
        _art = art;
        _thumbnails = thumbnails;
    }

    public ObservableCollection<CreatorCardViewModel> Creators { get; } = new();

    /// <summary>Raised when a creator card is activated (forwarded to the host nav).</summary>
    public event Action<long>? OpenCreatorRequested;

    public async Task LoadAsync(CancellationToken ct)
    {
        // Heavy work off the UI thread; resume on the captured context to mutate the UI-bound collection.
        // NOTE: do NOT use ConfigureAwait(false) on this chain (the Cross-thread ObservableCollection gotcha).
        var summaries = await Task.Run(() => _library.GetSectionSummaries(), ct);

        Creators.Clear();
        foreach (var summary in summaries)
        {
            var overridePath = _art.GetArtPath(summary.SectionId);
            var card = new CreatorCardViewModel(summary, overridePath, _thumbnails);
            card.OpenRequested += id => OpenCreatorRequested?.Invoke(id);
            Creators.Add(card);
            await card.LoadImageAsync(ct);
        }
    }
}
```

> **Gotcha (from ROADMAP):** the `await` continuations here must land back on the UI thread because they mutate `Creators` (a UI-bound `ObservableCollection`). Do **not** add `ConfigureAwait(false)` to this chain.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/VideoShelf.App/ViewModels/CreatorsViewModel.cs tests/VideoShelf.App.Tests/CreatorsViewModelTests.cs
git commit -m "feat(app): CreatorsViewModel feeds the Browse creator grid"
```

---

## Task 6: Creator-art override commands on the creator page (App)

Add `Set image…` / `Use default` actions to `SectionDetailViewModel` so the override can be set and cleared from the (existing) section-detail / creator surface.

**Files:**
- Modify: `src/VideoShelf.App/ViewModels/SectionDetailViewModel.cs`
- Test: `tests/VideoShelf.App.Tests/SectionDetailViewModelTests.cs` (existing file — add cases; **VERIFY** filename)

- [ ] **Step 1: Write the failing tests**

Add to the existing `SectionDetailViewModel` test file (a fake picker + the new commands). If no such test file exists, create `tests/VideoShelf.App.Tests/SectionDetailViewModelTests.cs`:

```csharp
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Tests;

public class SectionDetailCreatorArtTests
{
    private sealed class FakePicker(string? result) : IImagePicker
    {
        public string? PickImage(string? initialFolder = null) => result;
    }

    [Fact]
    public void SetCreatorArt_picks_and_persists_then_exposes_path()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var art = new CreatorArtRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");

        var vm = SectionDetailTestFactory.Create(temp, picker: new FakePicker(@"C:\pics\a.png"), art: art);
        await vm.OpenAsync(sectionId);

        vm.SetCreatorArtCommand.Execute(null);

        art.GetArtPath(sectionId).ShouldBe(@"C:\pics\a.png");
        vm.CreatorArtPath.ShouldBe(@"C:\pics\a.png");
    }

    [Fact]
    public void SetCreatorArt_noop_when_picker_cancelled()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var art = new CreatorArtRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");

        var vm = SectionDetailTestFactory.Create(temp, picker: new FakePicker(null), art: art);
        await vm.OpenAsync(sectionId);

        vm.SetCreatorArtCommand.Execute(null);

        art.GetArtPath(sectionId).ShouldBeNull();
    }

    [Fact]
    public void ClearCreatorArt_removes_override()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var art = new CreatorArtRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");
        art.SetArtPath(sectionId, @"C:\pics\a.png");

        var vm = SectionDetailTestFactory.Create(temp, picker: new FakePicker(null), art: art);
        await vm.OpenAsync(sectionId);
        vm.ClearCreatorArtCommand.Execute(null);

        art.GetArtPath(sectionId).ShouldBeNull();
        vm.CreatorArtPath.ShouldBeNull();
    }
}
```

> **VERIFY — STOP-and-report:** the real `SectionDetailViewModel` ctor (digest: 4 deps `LibraryRepository, TagRepository, WatchRepository, IThumbnailService`) and its load method name (`OpenAsync`/`LoadAsync(sectionId)`). Adapt the test factory + the `await` (make the test methods `async Task` if the load is async). Provide a small `SectionDetailTestFactory.Create(...)` helper near these tests that constructs the VM with the real deps + the new `IImagePicker`/`CreatorArtRepository` params; mirror however other App.Tests build this VM. If a factory already exists, extend it.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: FAIL — new ctor params / commands / `CreatorArtPath` don't exist.

- [ ] **Step 3: Extend the view-model**

In `src/VideoShelf.App/ViewModels/SectionDetailViewModel.cs`:
1. Add two ctor params **after** the existing ones: `CreatorArtRepository art, IImagePicker imagePicker`. Store them.
2. Track the current section id when the view loads (it likely already does — reuse it; otherwise capture it in the load method).
3. Add an observable `CreatorArtPath` and the two commands:

```csharp
[ObservableProperty]
private string? _creatorArtPath;

// Call this at the end of the existing load method, after _sectionId is set:
private void RefreshCreatorArt() => CreatorArtPath = _art.GetArtPath(_sectionId);

[RelayCommand]
private void SetCreatorArt()
{
    var picked = _imagePicker.PickImage();
    if (string.IsNullOrWhiteSpace(picked))
        return;
    _art.SetArtPath(_sectionId, picked);
    CreatorArtPath = picked;
}

[RelayCommand]
private void ClearCreatorArt()
{
    _art.ClearArtPath(_sectionId);
    CreatorArtPath = null;
}
```

> **VERIFY** the field that holds the loaded section id (digest implies the VM loads a section via an id). If it's named differently than `_sectionId`, use the real name. Call `RefreshCreatorArt()` at the end of the existing load path so `CreatorArtPath` is populated on open.

- [ ] **Step 4: Update existing construction sites**

Adding ctor params breaks existing `SectionDetailViewModel` construction (DI + any test factories). Update:
- The DI registration (Task 9 covers wiring, but add the params now so it compiles): pass the registered `CreatorArtRepository` + `IImagePicker`.
- Any App.Tests factory that builds `SectionDetailViewModel` (expected fan-out — mirror the M5 pattern where adding a ctor param touched ~3 sites). Build the project to find them:

Run: `dotnet build VideoShelf.slnx -c Release --nologo -v q`
Fix each compile error by supplying the two new args.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(app): creator-art override set/clear commands on creator page"
```

---

## Task 7: Fix the `LibraryViewModel` sort/search `_pending` race (App)

Replace the single `_pending` task with a `CancellationTokenSource` so a newer operation cancels the older one.

**Files:**
- Modify: `src/VideoShelf.App/ViewModels/LibraryViewModel.cs`
- Test: `tests/VideoShelf.App.Tests/LibraryViewModelTests.cs` (existing — add a case)

- [ ] **Step 1: Write the failing test**

Add to the existing `LibraryViewModelTests`:

```csharp
[Fact]
public async Task Changing_search_cancels_a_prior_pending_sort_load()
{
    using var temp = new AppTempDb();
    using var dir = new TempDir();
    dir.Touch("Creator A/Story 1.mp4");
    var vm = Build(temp, dir); // existing helper in this test class
    await vm.LoadSectionsAsync();

    // Kick a sort change then immediately a search change; the VM must settle without throwing
    // and end in the search state (no overlapping mutation of the UI-bound collection).
    vm.SortMode = BrowseSort.DateAdded;
    vm.SearchText = "Story";

    await vm.WaitForIdleAsync();

    vm.SearchText.ShouldBe("Story");
}
```

> **VERIFY** the existing `Build(...)` helper, `WaitForIdleAsync()`, `BrowseSort` enum, and the `SortMode`/`SearchText` property names in `LibraryViewModelTests` (digest confirms `OnSortModeChanged`/`OnSearchTextChanged` partials and a `_pending` field). If `WaitForIdleAsync` doesn't exist, await the VM's existing idle hook or `_pending` accessor; STOP-and-report if there's no way to await idle.

- [ ] **Step 2: Run test to verify behavior (it should pass functionally but may be racy)**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: the test compiles and passes; this task hardens the implementation so the prior load is deterministically cancelled.

- [ ] **Step 3: Replace the race-prone `_pending` with a CTS**

In `src/VideoShelf.App/ViewModels/LibraryViewModel.cs`, replace the `_pending` field and the two `partial void On…Changed` handlers:

```csharp
private CancellationTokenSource? _opCts;
private Task _pending = Task.CompletedTask;

private CancellationToken NextOperation()
{
    _opCts?.Cancel();
    _opCts?.Dispose();
    _opCts = new CancellationTokenSource();
    return _opCts.Token;
}

partial void OnSortModeChanged(BrowseSort value)
{
    var ct = NextOperation();
    if (SelectedSection is { } section)
        _pending = section.LoadSeriesAsync(value, ct);
}

partial void OnSearchTextChanged(string value)
{
    var ct = NextOperation();
    _pending = RunSearchAsync(value, ct);
}
```

> **VERIFY** the real signatures of `LoadSeriesAsync` and `RunSearchAsync` (digest shows `LoadSeriesAsync(value, CancellationToken.None)` and `RunSearchAsync(value)`). Thread the new `ct` through `RunSearchAsync` (add a `CancellationToken ct` param; pass it to any DB/Task.Run inside, and guard collection mutation with `ct.ThrowIfCancellationRequested()` or check `ct.IsCancellationRequested` before mutating). Keep `_pending` so `WaitForIdleAsync` still works. **STOP-and-report** if `RunSearchAsync` mutates a UI-bound collection in a way that can't be made cancellation-safe without broader changes — the harmless behavior is acceptable to leave if the fix balloons.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/VideoShelf.App/ViewModels/LibraryViewModel.cs tests/VideoShelf.App.Tests/LibraryViewModelTests.cs
git commit -m "fix(app): cancel prior sort/search load to close the _pending race"
```

---

## Task 8: Reusable `CreatorCard` + `VideoCard` controls + design tokens (App, XAML)

UserControls verified by build + the Task 11 screenshot sweep (no unit tests). `VideoCard` is **extracted** from the existing inline continue-watching template so the Home rails become DRY.

**Files:**
- Modify: `src/VideoShelf.App/Resources/DesignTokens.xaml` (additive)
- Create: `src/VideoShelf.App/Views/CreatorCard.xaml` + `.xaml.cs`
- Create: `src/VideoShelf.App/Views/VideoCard.xaml` + `.xaml.cs`
- Modify: `src/VideoShelf.App/Views/DiscoveryView.xaml` (re-point the rail)

- [ ] **Step 1: Add design tokens (additive only)**

Append to `src/VideoShelf.App/Resources/DesignTokens.xaml` (before `</ResourceDictionary>`):

```xml
<!-- v2 card system -->
<sys:Double x:Key="CreatorCardWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">200</sys:Double>
<sys:Double x:Key="CreatorCardImageHeight" xmlns:sys="clr-namespace:System;assembly=System.Runtime">120</sys:Double>
<CornerRadius x:Key="CardImageRadius">10</CornerRadius>
<Thickness x:Key="CardGap">0,0,16,16</Thickness>
<SolidColorBrush x:Key="CardCaptionBrush" Color="#B0FFFFFF" />
```

> **VERIFY** whether a `sys:` namespace is already declared at the dictionary root; if so, drop the inline `xmlns:sys` and reuse it. If `System` double resources are awkward, hard-code `Width="200"`/`Height="120"` directly in the card XAML instead and skip these two doubles. Keep additions purely additive (no edits to existing keys/styles).

- [ ] **Step 2: Create `CreatorCard`**

`src/VideoShelf.App/Views/CreatorCard.xaml`:

```xml
<UserControl x:Class="VideoShelf.App.Views.CreatorCard"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Button Command="{Binding OpenCommand}" Width="200" Padding="0"
            Background="Transparent" BorderThickness="0" Cursor="Hand"
            HorizontalContentAlignment="Stretch">
        <StackPanel>
            <Border CornerRadius="{StaticResource CardImageRadius}"
                    Background="{StaticResource ThumbPlaceholderBrush}"
                    Height="120" ClipToBounds="True">
                <Image Source="{Binding ImagePath, IsAsync=True}" Stretch="UniformToFill" />
            </Border>
            <TextBlock Text="{Binding Name}" FontWeight="SemiBold" Margin="2,8,2,0"
                       TextTrimming="CharacterEllipsis" />
            <TextBlock Text="{Binding VideoCountLabel}" Opacity="0.7" FontSize="12" Margin="2,1,2,0" />
        </StackPanel>
    </Button>
</UserControl>
```

`src/VideoShelf.App/Views/CreatorCard.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace VideoShelf.App.Views;

public partial class CreatorCard : UserControl
{
    public CreatorCard() => InitializeComponent();
}
```

- [ ] **Step 3: Create `VideoCard` (extract the existing rail template verbatim)**

`src/VideoShelf.App/Views/VideoCard.xaml` — copy the existing continue-watching `DataTemplate` body (digest shows it binds `ThumbnailPath`, `ProgressFraction`, `SeriesTitle`, `EpisodeLabel`, `PlayCommand`):

```xml
<UserControl x:Class="VideoShelf.App.Views.VideoCard"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Button Command="{Binding PlayCommand}" Margin="0,0,12,0" Padding="0"
            Background="Transparent" BorderThickness="0" Width="200" Cursor="Hand"
            HorizontalContentAlignment="Stretch">
        <StackPanel>
            <Border CornerRadius="{StaticResource CardImageRadius}"
                    Background="{StaticResource ThumbPlaceholderBrush}"
                    Height="112" ClipToBounds="True">
                <Image Source="{Binding ThumbnailPath}" Stretch="UniformToFill" />
            </Border>
            <ProgressBar Minimum="0" Maximum="1" Value="{Binding ProgressFraction}"
                         Height="3" Margin="0,2,0,4" />
            <TextBlock Text="{Binding SeriesTitle}" FontWeight="SemiBold"
                       TextTrimming="CharacterEllipsis" />
            <TextBlock Text="{Binding EpisodeLabel}" Opacity="0.7" FontSize="12" />
        </StackPanel>
    </Button>
</UserControl>
```

`src/VideoShelf.App/Views/VideoCard.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace VideoShelf.App.Views;

public partial class VideoCard : UserControl
{
    public VideoCard() => InitializeComponent();
}
```

> **VERIFY** the exact bound property names on the continue-watching item VM and **preserve them verbatim** — `VideoCard` must bind the same names so the rail renders identically. STOP-and-report if they differ from `ThumbnailPath/ProgressFraction/SeriesTitle/EpisodeLabel/PlayCommand`.

- [ ] **Step 4: Re-point the Home continue-watching rail at `VideoCard`**

In `src/VideoShelf.App/Views/DiscoveryView.xaml`, replace the inline continue-watching `DataTemplate` body with the control (add `xmlns:views="clr-namespace:VideoShelf.App.Views"` to the root if not present):

```xml
<ItemsControl ItemsSource="{Binding ContinueWatching}">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate><StackPanel Orientation="Horizontal" /></ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <views:VideoCard />
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

> **VERIFY** the existing rail markup before replacing; keep its surrounding header/layout. Only the per-item template changes. If other rails (Recently-added etc.) use the same inline card, leave them for M8 — this task only DRYs the continue-watching rail to prove `VideoCard` renders.

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build VideoShelf.slnx -c Release --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(app): reusable CreatorCard + VideoCard controls + card tokens"
```

---

## Task 9: Browse-as-creator-grid + nav restyle + DI wiring + "Creator" labels (App)

Render the new creator grid in the Browse host, restyle the nav chrome cohesively (additive), relabel Section→Creator in UI strings, and register the new services.

**Files:**
- Modify: DI registration (**VERIFY** file — `AddVideoShelf` extension)
- Modify: `src/VideoShelf.App/ViewModels/MainViewModel.cs` (host the `CreatorsViewModel`, wire open callback)
- Modify: `src/VideoShelf.App/Views/MainWindow.xaml` (Browse host = creator grid; nav restyle; labels)
- Modify: `src/VideoShelf.App/Views/SectionDetailView.xaml` (override buttons; "Creator" labels)

- [ ] **Step 1: Register the new services**

In the DI registration (where `AddVideoShelf` builds the container — **VERIFY** path, digest suggests `Services/ServiceCollectionExtensions.cs`), register:

```csharp
services.AddSingleton<IImagePicker, ImagePicker>();
services.AddSingleton<CreatorArtRepository>();
services.AddSingleton<CreatorsViewModel>();
```

> **VERIFY** the lifetime style used for existing repos/VMs (singleton vs transient) and match it. `CreatorArtRepository` needs `VideoShelfDb` (already registered). Ensure `SectionDetailViewModel`'s new ctor params (`CreatorArtRepository`, `IImagePicker`) resolve.

- [ ] **Step 2: Host the creator grid + open callback in `MainViewModel`**

In `src/VideoShelf.App/ViewModels/MainViewModel.cs`:
1. Add a `CreatorsViewModel Creators { get; }` property (injected via ctor).
2. In the ctor, wire its open callback to the existing section-open nav:

```csharp
Creators.OpenCreatorRequested += async id => await OpenSectionAsync(id);
```

3. Ensure Browse load triggers `Creators.LoadAsync(...)` — call it wherever Browse data is (re)loaded (e.g. after a scan/reload, and on first switch to Browse). **VERIFY** the existing `ScanAndReload`/load path (digest: `ScanAndReload` also calls `Discovery.LoadAsync()`) and add `await Creators.LoadAsync(CancellationToken.None)` alongside it.

> **VERIFY** `OpenSectionAsync(long id)` exists on `MainViewModel` (digest confirms `OpenSectionAsync(id)`); it both sets `CurrentView = AppView.SectionDetail` and loads the section. Reuse it — do not duplicate nav logic. Update existing `MainViewModel` test construction sites for the new ctor param (expected fan-out; mirror M5).

- [ ] **Step 3: Browse host = creator grid (MainWindow.xaml)**

In `src/VideoShelf.App/Views/MainWindow.xaml`, find the **Browse** nav host (the element gated by `EnumToVis` `ConverterParameter=Browse`). Replace its content with a scrollable wrap-grid of creator cards (keep the host's visibility binding intact — the **correct** pattern from the M6 fix):

```xml
<!-- Browse host -->
<ScrollViewer VerticalScrollBarVisibility="Auto"
              Visibility="{Binding DataContext.CurrentView,
                           RelativeSource={RelativeSource AncestorType=views:MainWindow},
                           Converter={StaticResource EnumToVis}, ConverterParameter=Browse}">
    <ItemsControl ItemsSource="{Binding Creators.Creators}" Margin="16">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate><WrapPanel /></ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <views:CreatorCard Margin="{StaticResource CardGap}" />
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</ScrollViewer>
```

> **VERIFY — STOP-and-report:** the exact current Browse host markup and its visibility binding before replacing. The replacement MUST keep the same `EnumToVis ConverterParameter=Browse` host-visibility pattern (binding to `DataContext.CurrentView` via the `MainWindow` ancestor — never bare `CurrentView`, which is the latent M6 bug). Ensure `xmlns:views="clr-namespace:VideoShelf.App.Views"` is declared on the root. Preserve the existing sidebar (sources/sections list) untouched.

- [ ] **Step 4: Nav restyle (additive) + Section→Creator labels**

In `MainWindow.xaml`, restyle the top nav cohesively **additively** — e.g. give the nav bar a subtle background/divider and consistent spacing using existing/new tokens; do **not** re-template `ui:Button`. Relabel any user-visible "Section"/"Sections" strings to "Creator"/"Creators" (e.g. the sidebar header `SECTIONS` → `CREATORS`, a "Browse" button may stay "Browse"). Example nav bar:

```xml
<Border Grid.Row="0" Background="{StaticResource SubtleFillBrush}"
        BorderBrush="{StaticResource DividerBrush}" BorderThickness="0,0,0,1">
    <StackPanel Orientation="Horizontal" Margin="16,8">
        <ui:Button Content="Home" Command="{Binding ShowHomeCommand}" Appearance="Transparent" Margin="0,0,8,0" />
        <ui:Button Content="Browse" Command="{Binding ShowBrowseCommand}" Appearance="Transparent" />
    </StackPanel>
</Border>
```

> **VERIFY** the existing nav row markup (digest shows two `ui:Button`s Home/Browse). Keep the commands/bindings; only restyle the container + button `Appearance`. Search the App `.xaml` files for user-visible "Section" labels and relabel to "Creator"; **do not** rename code symbols, the `sections` table, `AppView.SectionDetail`, or `SectionSummary` — UI strings only.

- [ ] **Step 5: Override buttons + "Creator" labels on the creator page**

In `src/VideoShelf.App/Views/SectionDetailView.xaml`, add a small action row near the header for the art override, and relabel visible "Section" → "Creator":

```xml
<StackPanel Orientation="Horizontal" Margin="0,0,0,8">
    <ui:Button Content="Set image…" Command="{Binding SetCreatorArtCommand}" Margin="0,0,8,0" />
    <ui:Button Content="Use default" Command="{Binding ClearCreatorArtCommand}"
               Visibility="{Binding CreatorArtPath, Converter={StaticResource NullToCollapsed}}" />
</StackPanel>
```

> **VERIFY** a null→collapsed converter exists. The digest lists `BoolToVisibility`, `EnumToVisibility`, `MissingToOpacity`, `SortModeToIndex` — there is **no** null→visibility converter. Either (a) add a tiny `NullToCollapsedConverter` to `Converters.cs` + register it (key `NullToCollapsed`), or (b) bind a new `bool HasCreatorArt => CreatorArtPath is not null` on the VM through the existing `BoolToVisibility`. Prefer (b) to avoid a new converter: add `public bool HasCreatorArt => !string.IsNullOrEmpty(CreatorArtPath);` to `SectionDetailViewModel` (raise its change notification in the `CreatorArtPath` partial-changed hook) and bind `Visibility="{Binding HasCreatorArt, Converter={StaticResource BoolToVisibility}}"`.

- [ ] **Step 6: Build + full test run**

Run: `dotnet build VideoShelf.slnx -c Release --nologo -v q`
Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: Build succeeded; all tests PASS (206 prior + the new tests). Fix any ctor-fan-out compile errors in tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(app): Browse creator grid + nav restyle + creator labels + DI wiring"
```

---

## Task 10: Whole-suite green + self-review

- [ ] **Step 1: Run the full gate**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: 0 failures. Record the new test count (was 206) for the PR + ROADMAP note.

- [ ] **Step 2: Confirm no library-file writes**

Grep the new code for any `File.Copy`/`File.Move`/`File.Write`/`Directory.Create` touching library paths. The creator-art override must store **only the user-picked path** in `creator_art` — no copying into AppData or library folders. (Reading the image for display via WPF `Image.Source` is fine.)

- [ ] **Step 3: Commit any fixes**

```bash
git add -A && git commit -m "test: M7 suite green; confirm DB-only creator-art override"
```

---

## Task 11: Screenshot verification sweep

Use the M6 harness to capture the affected views and verify with a **Sonnet subagent returning a TEXT verdict** (do not load PNGs into the controller).

- [ ] **Step 1: Capture the affected views**

Use `tools/harness/Run-VisualSweep.ps1` (or the documented launch hooks: `--folder <fixtures> --seed-demo --view <Home|Browse|SectionDetail> --autostart --done-signal <path>` with `--data-dir` for an isolated throwaway DB). Capture: **Browse** (new creator grid — cards show image + name + "N videos"), **Home** (continue-watching rail still renders via `VideoCard`), **SectionDetail** (creator page shows "Set image…/Use default" + "Creator" labels). Honor the GDI gotchas baked into the script (settle ~5s for Mica; TOPMOST→NOTOPMOST toggle; composited/unlocked desktop required).

> **VERIFY** the exact `Run-VisualSweep.ps1` invocation + `--view` token names against the M6 plan/script. Fixtures must be flat (each subfolder = a creator; filenames group by first integer token).

- [ ] **Step 2: Dispatch a Sonnet subagent for the verdict**

Dispatch a subagent: "Read these PNGs at <paths>. For each, verify against M7 acceptance criteria and return PASS/FAIL + specific observations + the absolute paths viewed. Criteria — Browse: a wrap-grid of creator cards, each with a thumbnail (or placeholder), the creator name, and an 'N videos' caption; no stacked/overlapping hosts. Home: the continue-watching rail renders horizontal video cards identically to before (thumbnail + progress bar + title + episode label). SectionDetail: header shows 'Set image…' and (if art set) 'Use default'; visible labels say 'Creator' not 'Section'." Act on the text verdict only.

- [ ] **Step 3: Fix any FAIL additively and re-capture**

Apply additive fixes only (theming rule). Re-run Steps 1–2 until PASS. Commit fixes:

```bash
git add -A && git commit -m "fix(app): M7 visual sweep adjustments"
```

---

## Task 12: PR → CI → merge → ROADMAP flip

- [ ] **Step 1: Push the branch**

```bash
git push -u origin <branch-name>
```

- [ ] **Step 2: Open the PR**

```bash
& "C:\Program Files\GitHub CLI\gh.exe" pr create --base main --title "M7: Creator model + card system + shell foundation" --body "<summary: creator read-model + creator_art override + reusable CreatorCard/VideoCard + Browse creator grid + nav restyle + _pending race fix; test count; screenshot verdict>"
```

- [ ] **Step 3: Watch CI in the foreground**

Wait ~20s (to dodge "no checks reported"), then:

```bash
& "C:\Program Files\GitHub CLI\gh.exe" pr checks <PR#> --watch
```

Expected: `build-and-test` (and `package`) green.

- [ ] **Step 4: Merge from the main repo root**

```bash
& "C:\Program Files\GitHub CLI\gh.exe" pr merge <PR#> --merge --delete-branch
```

- [ ] **Step 5: Flip the ROADMAP row** (on a follow-up branch or the next phase's branch, per convention — direct pushes to main are blocked)

Update `ROADMAP.md` M7 row → `✅ Merged`, add the PR link + a one-line shipped summary + new test count, and append an "M7 shipped" decision-log entry capturing any durable facts/gotchas discovered. Ship via PR.

---

## Self-Review (already applied)

- **Spec coverage:** creator read-model (Task 1), creator-art override DB+repo (Task 2) + picker (Task 3) + commands (Task 6) → spec §2; `CreatorCard`/`VideoCard` reusable cards (Tasks 4/8) + Browse creator grid (Task 9) → spec §1/§3; nav design language (Task 9) → spec §3; sort/search race fold-in (Task 7) → spec §5. Home/Search redesign (§4/§6 M8), creator page Netflix layout (§4 M9), immersive player (§3 M10) are **out of M7 scope** by design.
- **Type consistency:** `SectionSummary` gains `VideoCount`/`ThumbnailSeedPath` (Task 1) and is consumed by `CreatorCardViewModel` (Task 4); `CreatorArtRepository.GetArtPath/SetArtPath/ClearArtPath` (Task 2) consumed by Tasks 5/6; `IImagePicker.PickImage` (Task 3) consumed by Task 6; `CreatorsViewModel.OpenCreatorRequested` (Task 5) wired in Task 9.
- **Flagged for the executor:** every "VERIFY — STOP-and-report" marks a place where the digest (not direct reading) informed the plan — confirm the real signature/markup before coding, and stop rather than guess if it materially differs.
