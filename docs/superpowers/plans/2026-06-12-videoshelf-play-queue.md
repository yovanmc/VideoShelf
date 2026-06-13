# M14 — Play-queue & up-next

> **Written for Sonnet execution.** Every task lists exact files, complete code (or precise insertion points with the surrounding anchor), exact commands, and expected output. **If something doesn't match what's described here — a signature, a column name, a line range — STOP and report rather than guess.** The digests below were captured 2026-06-12; verify each anchor by reading the cited method before editing.
>
> Conventions (from ROADMAP): solution is `VideoShelf.slnx`. Test gate: `dotnet test VideoShelf.slnx -c Release --nologo -v q`. Build quietly: `dotnet build VideoShelf.slnx -v minimal`. `gh` is NOT on PATH → `& "C:\Program Files\GitHub CLI\gh.exe"`. Work in a worktree under `.worktrees/`; **merge `gh pr merge` from the main repo root**, `--merge` (no squash). Direct pushes to `main` are blocked. Commits: author `yovanmc` + trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` (no Codex trailer). Branch: `feat/play-queue`.

## Goal

A **scoped, explicit play-queue / up-next** that goes beyond today's single-series auto-next:

- **"Play all"** across a creator (whole section), a single series, plus **"Play next" / "Add to queue"** from series tiles and Home cards.
- A **visible up-next list** in **two** surfaces: an **in-player drawer** (slides over the video) **and** a **dedicated Queue page** (`AppView.Queue`).
- **Manual controls:** skip-to-next/previous, jump-to-item, remove item, clear all, **and reorder (move up/down)**.
- **Queue is ephemeral / in-memory** — it lives only in `PlayQueueViewModel` for the session; closing the app clears it. **No DB schema change** (preserves the M8→M14 no-migration streak).
- **NOT** infinite whole-library autoplay (stays OUT per v1 §13). The queue is only ever what the user explicitly built.

### The orchestration contract (read this before coding)

Today (verify): `PlayerViewModel.OnEnded` (`src/VideoShelf.App/ViewModels/PlayerViewModel.cs:333-348`) marks the finished video watched, then **itself** checks `settings.GetAutoAdvanceEpisodes()`, calls `library.GetNextEpisode(...)`, and raises `NextEpisodeRequested`, which `MainViewModel` (`:43`) routes to `PlayEpisode`.

**New design — `MainViewModel` is the single orchestrator of "what plays next":**

1. `PlayerViewModel.OnEnded` only **marks watched** and raises a new `PlaybackEnded(finishedEpisode)` event. It no longer computes the next episode. (`NextEpisodeRequested` and the `GetNextEpisode`/setting logic are removed from `PlayerViewModel`.)
2. `MainViewModel` subscribes `PlaybackEnded` → asks `PlayQueueViewModel.GetNextAfterEnd(finished)` → if non-null, plays it.
3. `PlayQueueViewModel.GetNextAfterEnd` is **queue-first**: if an explicit queue is active and has a next item, advance and return it; once the explicit queue is exhausted it clears and returns null (stop). If there is **no** explicit queue (a plain single play), it falls back to the legacy behaviour — `settings.GetAutoAdvanceEpisodes()` + `library.GetNextEpisode(...)`.
4. **All playback funnels through `MainViewModel.OpenPlayer(ep)`.** Direct plays (a card/episode click) go through `PlayEpisode(ep)` which calls `_playQueue.StartSingle(ep)` (a non-explicit queue-of-one) then `OpenPlayer`. Queue-initiated plays (Play-all / jump / skip / advance) raise `PlayQueueViewModel.PlayRequested` → `MainViewModel.OpenPlayer` **without** re-touching queue state (the queue already set its own cursor).

This keeps the queue's `CurrentIndex` authoritative and means "Add to queue while watching a single video" works (the queue takes over when the current video ends), while legacy single-series auto-advance is preserved verbatim for non-queue playback.

---

## Digest — verified anchors (read each before editing)

**Core models** (`src/VideoShelf.Core/Models/BrowseModels.cs`, namespace `VideoShelf.Core.Models`):
- `EpisodeView(long VideoId, long SeriesId, string FilePath, int EpisodeNo, string Title, bool Watched, bool Missing)` — `:14-16`
- `SeriesSummary(...)` `:9-11`, `SectionSummary(...)` `:4-6`

**`LibraryRepository`** (`src/VideoShelf.Core/Storage/LibraryRepository.cs`):
- `GetVideosForSeries(long seriesId)` `:88-107` — `ORDER BY episode_no`
- `GetEpisodes(long seriesId)` `:244-268` → `IReadOnlyList<EpisodeView>`; title computed in C# as `episodeNo <= 1 ? baseTitle : $"{baseTitle} {episodeNo}"`. **This is the projection to mirror.**
- `GetSeriesForSection(long sectionId)` `:132-142` — `ORDER BY sort_key` (sort_key = `base_title.ToLowerInvariant()`, set at upsert `:51`)
- `GetNextEpisode(long seriesId, int currentEpisodeNo)` `:419-443` → `EpisodeView?`; returns null for standalone (`is_standalone = 0` filter), else first `episode_no > current`.
- `GetSection(long id)` `:121-130` → lean `Section` record; `GetSectionSummaries()` `:170-203`.
- DB pattern: `db.Open()` per call, `$`-prefixed params.

**`PlayerViewModel`** (`src/VideoShelf.App/ViewModels/PlayerViewModel.cs`): ctor 6 params `(IPlaybackEngine engine, LibraryRepository library, WatchRepository watch, SettingsRepository settings, ResumePolicy resumePolicy, ISubtitleFilePicker subtitlePicker)` `:19-25`. `OnEnded` `:333-348`. Engine `Ended` hooked in `Open()` `:273-280`. `NextEpisodeRequested` event `:239`. Overlay props: `AreControlsVisible` `:91`, `AutoHideSuppressed` `:94-96`, `IsPlaying` `:52`, `IsScrubbing` `:82`, `IsFullscreen` `:101`.

**`MainViewModel`** (`src/VideoShelf.App/ViewModels/MainViewModel.cs`): ctor 11 params `:22-68`. Event wiring `:42-43` (`_library.PlayRequested`, `_player.NextEpisodeRequested`). `PlayEpisode(EpisodeView)` `:131-135`. `IsPlayerVisible` `:89`, `IsPictureInPicture` `:92`, `IsInlinePlayerVisible` `:95`. `enum AppView { Home, Browse, SectionDetail, RenameTool, Search, Settings }` `:11`. `CurrentView` `:83`. Back-stack `_backStack` `:100`, `PushNav` `:106-111`, `ClearBack` `:113-117`, `GoBack` `:120-125`.

**`SettingsRepository`** (`src/VideoShelf.Core/Storage/SettingsRepository.cs`): `GetAutoAdvanceEpisodes()` `:31-36` (default true), const `AutoAdvanceKey` `:6`.

**`SectionDetailViewModel`** (`src/VideoShelf.App/ViewModels/SectionDetailViewModel.cs`): ctor 6 params `(LibraryRepository, TagRepository, WatchRepository, IThumbnailService, CreatorArtRepository, IImagePicker)`. Builds each `SeriesViewModel(s, library, watch, thumbnails)`. Series→section bubbling precedent: `SeriesViewModel.RequestRename`/`RenameRequested` → `MainViewModel.OpenRenameToolAsync`. Public `SectionId`; load is `LoadAsync(long sectionId)`.

**`SeriesViewModel`** (`src/VideoShelf.App/ViewModels/SeriesViewModel.cs`): `Activate` command (standalone→play / multi→expand), `IsExpanded`, `EpisodeCountLabel`, `RequestRename`. Has `library`, `watch`, `thumbnails`.

**`DiscoveryViewModel`** (`src/VideoShelf.App/ViewModels/DiscoveryViewModel.cs`): ctor `(DiscoveryRepository disc, LibraryRepository lib, TagRepository tags, CreatorCardFactory cardFactory, StatsRepository statsRepo)` — **has `LibraryRepository lib`**. Continue-watching items are `ContinueWatchingItem(long VideoId, long SeriesId, long SectionId, string SeriesTitle, bool IsStandalone, int EpisodeNo, double ResumePosition, double? Duration, string? ThumbnailSeedPath)`; recency items `RecencyItem(long VideoId, ...)`. **Neither carries `FilePath`** — to enqueue, resolve a full `EpisodeView` via a new `LibraryRepository.GetEpisode(videoId)` (Task 1).

**DI** (`src/VideoShelf.App/Services/ServiceCollectionExtensions.cs`): `AddVideoShelf` `:20-93`. `PlayerViewModel` registered via factory `:58-73` (sets `CaptureDirectory`/`SeekPreviewDirectory`). `DiscoveryViewModel` `:77`, `SectionDetailViewModel` `:78`, `MainViewModel` `:90` (plain `AddSingleton` → auto-resolves ctor params), `MainWindow` `:91`.

**Views:**
- `PlayerView.xaml`: `RootGrid` `:18` → `PlayerShell` `:19` → `VideoSurface` (VideoView) `:20` → `OverlayRoot` (Grid, holds all overlays) `:21`. `ControlsLayer` (bound to `Player.AreControlsVisible`) `:43`. Bottom transport Border `:71-132`; Play/Pause `:94`; **secondary-controls StackPanel `:96-129`** with `DataTrigger IsPictureInPicture=True → Collapsed` (chapter/audio/subtitle/+Sub/volume/screenshot/fullscreen/mini-player). `PlayerView.xaml.cs`: auto-hide `DispatcherTimer` 3s, `OnAutoHideTick`, `ShowControls`, seek drag handlers, `ApplyPipState()` host-margin logic.
- `MainWindow.xaml`: `PlayerHost` ContentControl Grid.Row=0 RowSpan=2 `:156`, PiP size DataTrigger (`Width=360 Height=203`) `:161-171`. `MainWindow.xaml.cs`: `UpdatePlayerHost(bool)` `:62-73` (sets/clears `PlayerHost.Content = new PlayerView { DataContext = _viewModel }`). **PlayerView's DataContext is the `MainViewModel`** — so in `PlayerView.xaml` bind to `{Binding PlayQueue...}`.
- `SectionDetailView.xaml`: hero Grid `:16` (Height 240) → bg Image `:17`, gradient `:19-26`, Edit toggle `:28-40`, bottom StackPanel `:41-71` (DisplayName `:42`, "N videos" `:43-45`, tags+art `:46-70`). Series `ItemsControl` `:94`, tile Border `:98` (Width 240), header button `:107-128` (`ActivateCommand`), tile `ContextMenu` exists (rename) `~:102`, expanded panel `:130-150`.
- App-level converters in `App.xaml`: `BoolToVisibility` `:15`, `MissingToOpacity` `:16`, `EnumToVisibility` (key `EnumToVis`) `:17`, `FractionToWidth` `:18`. View-local `EnumSetToVisibility` (key `EnumSetToVis`) used in `MainWindow.xaml` for nav gating.
- Design tokens (`src/VideoShelf.App/Resources/DesignTokens.xaml`): `CardRadius`(8), `ControlRadius`(4), `CardImageRadius`(10), `AccentBrush`(#5CC8FF), `SubtleFillBrush`, `DividerBrush`, `ThumbPlaceholderBrush`, `SectionHeader` style, `Caption` style.

**Tests:**
- Core test DB helper: `tests/VideoShelf.Core.Tests/TestSupport/TempDb.cs` (`new TempDb()` → `temp.Db` migrated; auto-deleted). Example pattern in `tests/VideoShelf.Core.Tests/Storage/LibraryRepositoryTests.cs:24-38`.
- App test factory: `tests/VideoShelf.App.Tests/TestSupport/MainViewModelTestFactory.cs:29-68` (`Create(out MainVmContext ctx)`), uses `AppTempDb`, builds `discoveryVm`, `sectionDetailVm`, `player`, then `MainViewModel(...)`.
- Fakes (`tests/VideoShelf.App.Tests/TestSupport/`): `FakePlaybackEngine` (drive via `Raise*`), `FakeMediaProbe`, `FakeFolderPicker`, `FakeImagePicker`, `FakeSubtitleFilePicker`.
- Direct construction sites: `PlayerViewModelTests.cs` (`NewVm()` helper), `PlayerCaptureTests.cs`, `PlayerSubtitleTests.cs`, `PlayerTracksAndChaptersTests.cs`, `PlayerMissingFileTests.cs`, `PlayerEndOfMediaTests.cs`, `SectionDetailViewModelTests.cs:33`, `DiscoveryViewModelTests.cs`, `MainViewModelTests.cs`/`MainViewModelPlaybackTests.cs`/`MainViewModelNavigationTests.cs` (via factory).

---

## Task 1 — Core: queue-source queries (`GetEpisodesForSection`, `GetEpisode`) + tests

**File:** `src/VideoShelf.Core/Storage/LibraryRepository.cs`

First **read `GetEpisodes` (`:244-268`)** to copy its exact column names, join shape, and title computation. Then add two methods nearby (after `GetEpisodes`). The code below mirrors that projection; **if `GetEpisodes` uses different column/table names, match it and STOP-and-report any divergence.**

```csharp
/// <summary>
/// All playable (non-missing) episodes across every series in a section,
/// in deterministic play order: series by sort_key, then episode_no.
/// Used to build a "Play all" queue for a creator.
/// </summary>
public IReadOnlyList<EpisodeView> GetEpisodesForSection(long sectionId)
{
    using var conn = db.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT v.id, v.series_id, v.file_path, v.episode_no, se.base_title, v.watched, v.missing
        FROM videos v
        JOIN series se ON se.id = v.series_id
        WHERE se.section_id = $sid AND v.missing = 0
        ORDER BY se.sort_key, v.episode_no;
        """;
    cmd.Parameters.AddWithValue("$sid", sectionId);
    var list = new List<EpisodeView>();
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        var episodeNo = r.GetInt32(3);
        var baseTitle = r.GetString(4);
        var title = episodeNo <= 1 ? baseTitle : $"{baseTitle} {episodeNo}";
        list.Add(new EpisodeView(
            r.GetInt64(0), r.GetInt64(1), r.GetString(2), episodeNo, title,
            r.GetInt32(5) != 0, r.GetInt32(6) != 0));
    }
    return list;
}

/// <summary>Single episode by video id (for enqueue from Home cards that only carry a VideoId).</summary>
public EpisodeView? GetEpisode(long videoId)
{
    using var conn = db.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT v.id, v.series_id, v.file_path, v.episode_no, se.base_title, v.watched, v.missing
        FROM videos v
        JOIN series se ON se.id = v.series_id
        WHERE v.id = $vid;
        """;
    cmd.Parameters.AddWithValue("$vid", videoId);
    using var r = cmd.ExecuteReader();
    if (!r.Read()) return null;
    var episodeNo = r.GetInt32(3);
    var baseTitle = r.GetString(4);
    var title = episodeNo <= 1 ? baseTitle : $"{baseTitle} {episodeNo}";
    return new EpisodeView(
        r.GetInt64(0), r.GetInt64(1), r.GetString(2), episodeNo, title,
        r.GetInt32(5) != 0, r.GetInt32(6) != 0);
}
```

**Tests** — new file `tests/VideoShelf.Core.Tests/Storage/PlayQueueQueriesTests.cs` (mirror `LibraryRepositoryTests` setup):

```csharp
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using Xunit;

namespace VideoShelf.Core.Tests.Storage;

public sealed class PlayQueueQueriesTests
{
    [Fact]
    public void GetEpisodesForSection_flattens_series_in_order_and_excludes_missing()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);
        var src = repo.UpsertSource(@"C:\V", "V");
        var sec = repo.UpsertSection(src, "Creator A");

        // "Alpha" sorts before "Beta" by sort_key; episodes by episode_no
        var alpha = repo.UpsertSeries(sec, "Alpha", isStandalone: false);
        repo.UpsertVideo(alpha, @"C:\V\Creator A\Alpha 1.mp4", 1, ".mp4");
        repo.UpsertVideo(alpha, @"C:\V\Creator A\Alpha 2.mp4", 2, ".mp4");
        var beta = repo.UpsertSeries(sec, "Beta", isStandalone: true);
        repo.UpsertVideo(beta, @"C:\V\Creator A\Beta.mp4", 1, ".mp4");

        var eps = repo.GetEpisodesForSection(sec);
        eps.Select(e => e.Title).ShouldBe(new[] { "Alpha", "Alpha 2", "Beta" });
        eps.Select(e => e.FilePath).ShouldContain(@"C:\V\Creator A\Beta.mp4");
    }

    [Fact]
    public void GetEpisode_round_trips_by_video_id()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);
        var src = repo.UpsertSource(@"C:\V", "V");
        var sec = repo.UpsertSection(src, "Creator A");
        var s = repo.UpsertSeries(sec, "Solo", isStandalone: true);
        repo.UpsertVideo(s, @"C:\V\Creator A\Solo.mp4", 1, ".mp4");

        var one = repo.GetEpisodesForSection(sec).Single();
        var byId = repo.GetEpisode(one.VideoId);
        byId.ShouldNotBeNull();
        byId!.FilePath.ShouldBe(@"C:\V\Creator A\Solo.mp4");
        repo.GetEpisode(999_999).ShouldBeNull();
    }
}
```

> If `UpsertVideo`/`UpsertSeries`/`UpsertSection`/`UpsertSource` signatures differ from `LibraryRepositoryTests.cs:24-38`, match the real ones (read that file). The "missing-excluded" assertion is covered implicitly (no missing rows seeded here); a missing row can't be created without a setter — leave it (the SQL `v.missing = 0` is the guard).

**Verify Task 1:**
```
dotnet test VideoShelf.slnx -c Release --nologo -v q
```
Expected: build succeeds, all tests pass, **+2 Core tests** over the M13 baseline of 130 Core (→ 132 Core).

---

## Task 2 — App: `PlayQueueViewModel` + `QueueItemViewModel` + unit tests

**New file:** `src/VideoShelf.App/ViewModels/QueueItemViewModel.cs`

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.Core.Models;

namespace VideoShelf.App.ViewModels;

public sealed partial class QueueItemViewModel : ObservableObject
{
    public EpisodeView Episode { get; }
    public QueueItemViewModel(EpisodeView episode) => Episode = episode;

    public string Title => Episode.Title;

    [ObservableProperty] private bool _isNowPlaying;
}
```

**New file:** `src/VideoShelf.App/ViewModels/PlayQueueViewModel.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>
/// Ephemeral, in-memory up-next queue. The currently-playing item, when it
/// originates from the queue, is Items[CurrentIndex]. IsExplicitQueue is true
/// when the user built a real queue (Play all / Add to queue / Play next) and
/// gates the queue UI + the queue-first end-of-media behaviour. A plain single
/// play (StartSingle) is a non-explicit queue-of-one that preserves legacy
/// single-series auto-advance. The queue is never persisted.
/// </summary>
public sealed partial class PlayQueueViewModel : ObservableObject
{
    private readonly LibraryRepository _library;
    private readonly SettingsRepository _settings;

    public PlayQueueViewModel(LibraryRepository library, SettingsRepository settings)
    {
        _library = library;
        _settings = settings;
    }

    public ObservableCollection<QueueItemViewModel> Items { get; } = new();

    [ObservableProperty] private int _currentIndex = -1;
    [ObservableProperty] private bool _isExplicitQueue;
    [ObservableProperty] private bool _isQueueOpen; // in-player drawer open state

    /// <summary>Host plays this episode without the queue re-touching its own cursor.</summary>
    public event EventHandler<EpisodeView>? PlayRequested;

    /// <summary>True when there is a real, user-built queue to display.</summary>
    public bool HasQueue => IsExplicitQueue && Items.Count > 0;

    /// <summary>"N in queue" label for nav/page headers.</summary>
    public string CountLabel => Items.Count == 1 ? "1 in queue" : $"{Items.Count} in queue";

    partial void OnIsExplicitQueueChanged(bool value) => OnPropertyChanged(nameof(HasQueue));
    partial void OnCurrentIndexChanged(int value) => UpdateNowPlayingFlags();

    private void NotifyCollectionDerived()
    {
        OnPropertyChanged(nameof(HasQueue));
        OnPropertyChanged(nameof(CountLabel));
        UpdateNowPlayingFlags();
    }

    private void UpdateNowPlayingFlags()
    {
        for (int i = 0; i < Items.Count; i++)
            Items[i].IsNowPlaying = i == CurrentIndex;
    }

    // ---- entry: build + start a real queue (Play all / per-series play all) ----
    public void PlayAll(IReadOnlyList<EpisodeView> episodes)
    {
        if (episodes is null || episodes.Count == 0) return;
        Items.Clear();
        foreach (var e in episodes) Items.Add(new QueueItemViewModel(e));
        IsExplicitQueue = true;
        CurrentIndex = 0;
        NotifyCollectionDerived();
        PlayRequested?.Invoke(this, Items[0].Episode);
    }

    // ---- entry: direct single play (a card/episode click, non-queue) ----
    public void StartSingle(EpisodeView episode)
    {
        Items.Clear();
        Items.Add(new QueueItemViewModel(episode));
        IsExplicitQueue = false;
        CurrentIndex = 0;
        NotifyCollectionDerived();
    }

    // ---- enqueue (no immediate playback change) ----
    public void Enqueue(EpisodeView episode)
    {
        Items.Add(new QueueItemViewModel(episode));
        IsExplicitQueue = true;
        NotifyCollectionDerived();
    }

    public void EnqueueRange(IReadOnlyList<EpisodeView> episodes)
    {
        if (episodes is null || episodes.Count == 0) return;
        foreach (var e in episodes) Items.Add(new QueueItemViewModel(e));
        IsExplicitQueue = true;
        NotifyCollectionDerived();
    }

    public void PlayNext(EpisodeView episode)
    {
        var at = CurrentIndex >= 0 ? CurrentIndex + 1 : Items.Count;
        Items.Insert(at, new QueueItemViewModel(episode));
        IsExplicitQueue = true;
        NotifyCollectionDerived();
    }

    public void PlayNextRange(IReadOnlyList<EpisodeView> episodes)
    {
        if (episodes is null || episodes.Count == 0) return;
        var at = CurrentIndex >= 0 ? CurrentIndex + 1 : Items.Count;
        for (int i = 0; i < episodes.Count; i++)
            Items.Insert(at + i, new QueueItemViewModel(episodes[i]));
        IsExplicitQueue = true;
        NotifyCollectionDerived();
    }

    // ---- end-of-media: queue-first, falling back to legacy single-series auto-advance ----
    public EpisodeView? GetNextAfterEnd(EpisodeView finished)
    {
        if (IsExplicitQueue)
        {
            if (CurrentIndex >= 0 && CurrentIndex + 1 < Items.Count)
            {
                CurrentIndex++; // raises UpdateNowPlayingFlags
                return Items[CurrentIndex].Episode;
            }
            // explicit queue exhausted → clear and stop
            Clear();
            return null;
        }
        // non-explicit single play → legacy auto-advance
        if (_settings.GetAutoAdvanceEpisodes())
        {
            var next = _library.GetNextEpisode(finished.SeriesId, finished.EpisodeNo);
            if (next is not null)
            {
                StartSingle(next);
                return next;
            }
        }
        return null;
    }

    // ---- manual controls (bound from drawer + page) ----
    [RelayCommand]
    private void JumpTo(QueueItemViewModel? item)
    {
        if (item is null) return;
        var idx = Items.IndexOf(item);
        if (idx < 0) return;
        IsExplicitQueue = true;
        CurrentIndex = idx;
        PlayRequested?.Invoke(this, item.Episode);
    }

    [RelayCommand]
    private void SkipNext()
    {
        if (CurrentIndex >= 0 && CurrentIndex + 1 < Items.Count)
        {
            CurrentIndex++;
            PlayRequested?.Invoke(this, Items[CurrentIndex].Episode);
        }
    }

    [RelayCommand]
    private void SkipPrevious()
    {
        if (CurrentIndex > 0)
        {
            CurrentIndex--;
            PlayRequested?.Invoke(this, Items[CurrentIndex].Episode);
        }
    }

    [RelayCommand]
    private void RemoveItem(QueueItemViewModel? item)
    {
        if (item is null) return;
        var idx = Items.IndexOf(item);
        if (idx < 0) return;
        Items.RemoveAt(idx);
        if (idx < CurrentIndex) CurrentIndex--;
        else if (idx == CurrentIndex) CurrentIndex = Math.Min(CurrentIndex, Items.Count - 1);
        if (Items.Count == 0) { IsExplicitQueue = false; CurrentIndex = -1; }
        NotifyCollectionDerived();
    }

    [RelayCommand]
    private void MoveUp(QueueItemViewModel? item)
    {
        if (item is null) return;
        var idx = Items.IndexOf(item);
        if (idx <= 0) return;
        Items.Move(idx, idx - 1);
        if (CurrentIndex == idx) CurrentIndex--;
        else if (CurrentIndex == idx - 1) CurrentIndex++;
        NotifyCollectionDerived();
    }

    [RelayCommand]
    private void MoveDown(QueueItemViewModel? item)
    {
        if (item is null) return;
        var idx = Items.IndexOf(item);
        if (idx < 0 || idx >= Items.Count - 1) return;
        Items.Move(idx, idx + 1);
        if (CurrentIndex == idx) CurrentIndex++;
        else if (CurrentIndex == idx + 1) CurrentIndex--;
        NotifyCollectionDerived();
    }

    [RelayCommand]
    private void Clear()
    {
        Items.Clear();
        CurrentIndex = -1;
        IsExplicitQueue = false;
        IsQueueOpen = false;
        NotifyCollectionDerived();
    }

    [RelayCommand]
    private void ToggleDrawer() => IsQueueOpen = !IsQueueOpen;
}
```

> **CommunityToolkit.Mvvm note:** `[RelayCommand] private void JumpTo(QueueItemViewModel? item)` generates `JumpToCommand` (an `IRelayCommand<QueueItemViewModel>`). Bind XAML `Command="{Binding JumpToCommand}" CommandParameter="{Binding}"`. Confirm the toolkit version matches existing `[RelayCommand]` usage in the repo (e.g. `SectionDetailViewModel`) — it does (M13 used it).

**Unit tests** — new file `tests/VideoShelf.App.Tests/PlayQueueViewModelTests.cs`. Use a real `LibraryRepository`+`SettingsRepository` on an `AppTempDb` (mirror the factory's DB setup) so `GetNextAfterEnd`'s fallback exercises real `GetNextEpisode`.

```csharp
using Shouldly;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests;

public sealed class PlayQueueViewModelTests
{
    private static EpisodeView Ep(long id, long series, int no, string title) =>
        new(id, series, $@"C:\V\{title}.mp4", no, title, false, false);

    private static (PlayQueueViewModel q, AppTempDb db, LibraryRepository lib) New()
    {
        var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var settings = new SettingsRepository(db.Db);
        return (new PlayQueueViewModel(lib, settings), db, lib);
    }

    [Fact]
    public void PlayAll_sets_first_as_current_and_requests_play()
    {
        var (q, db, _) = New();
        using var _d = db;
        EpisodeView? played = null;
        q.PlayRequested += (_, e) => played = e;
        q.PlayAll(new[] { Ep(1,1,1,"A"), Ep(2,1,2,"B") });
        q.HasQueue.ShouldBeTrue();
        q.CurrentIndex.ShouldBe(0);
        played!.Title.ShouldBe("A");
        q.Items[0].IsNowPlaying.ShouldBeTrue();
    }

    [Fact]
    public void GetNextAfterEnd_advances_then_clears_on_exhaustion()
    {
        var (q, db, _) = New();
        using var _d = db;
        q.PlayAll(new[] { Ep(1,1,1,"A"), Ep(2,1,2,"B") });
        var next = q.GetNextAfterEnd(q.Items[0].Episode);
        next!.Title.ShouldBe("B");
        q.CurrentIndex.ShouldBe(1);
        var after = q.GetNextAfterEnd(q.Items[0].Episode); // last item finished
        after.ShouldBeNull();
        q.HasQueue.ShouldBeFalse();
        q.Items.Count.ShouldBe(0);
    }

    [Fact]
    public void StartSingle_falls_back_to_series_auto_advance_when_enabled()
    {
        var (q, db, lib) = New();
        using var _d = db;
        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "Creator");
        var ser = lib.UpsertSeries(sec, "Show", isStandalone: false);
        lib.UpsertVideo(ser, @"C:\V\Creator\Show 1.mp4", 1, ".mp4");
        lib.UpsertVideo(ser, @"C:\V\Creator\Show 2.mp4", 2, ".mp4");
        var first = lib.GetEpisodes(ser)[0];

        q.StartSingle(first);
        q.HasQueue.ShouldBeFalse();           // single play => no queue UI
        var next = q.GetNextAfterEnd(first);
        next.ShouldNotBeNull();
        next!.EpisodeNo.ShouldBe(2);
    }

    [Fact]
    public void Enqueue_then_end_of_single_plays_queue()
    {
        var (q, db, _) = New();
        using var _d = db;
        q.StartSingle(Ep(1,1,1,"X"));
        q.Enqueue(Ep(2,2,1,"Y"));
        q.HasQueue.ShouldBeTrue();            // enqueue promotes to explicit
        var next = q.GetNextAfterEnd(q.Items[0].Episode);
        next!.Title.ShouldBe("Y");
    }

    [Fact]
    public void PlayNext_inserts_after_current()
    {
        var (q, db, _) = New();
        using var _d = db;
        q.PlayAll(new[] { Ep(1,1,1,"A"), Ep(2,1,2,"B") });
        q.PlayNext(Ep(3,3,1,"C"));
        q.Items[1].Title.ShouldBe("C");
        q.Items[2].Title.ShouldBe("B");
    }

    [Fact]
    public void Remove_before_current_keeps_now_playing()
    {
        var (q, db, _) = New();
        using var _d = db;
        q.PlayAll(new[] { Ep(1,1,1,"A"), Ep(2,1,2,"B"), Ep(3,1,3,"C") });
        q.GetNextAfterEnd(q.Items[0].Episode); // now playing index 1 ("B")
        q.RemoveItemCommand.Execute(q.Items[0]); // remove "A"
        q.Items[q.CurrentIndex].Title.ShouldBe("B");
    }

    [Fact]
    public void MoveDown_keeps_now_playing_pointer()
    {
        var (q, db, _) = New();
        using var _d = db;
        q.PlayAll(new[] { Ep(1,1,1,"A"), Ep(2,1,2,"B"), Ep(3,1,3,"C") });
        var a = q.Items[0]; // current
        q.MoveDownCommand.Execute(a);
        q.Items[1].Title.ShouldBe("A");
        q.Items[q.CurrentIndex].Title.ShouldBe("A");
    }

    [Fact]
    public void JumpTo_requests_play_and_sets_current()
    {
        var (q, db, _) = New();
        using var _d = db;
        EpisodeView? played = null;
        q.PlayRequested += (_, e) => played = e;
        q.PlayAll(new[] { Ep(1,1,1,"A"), Ep(2,1,2,"B"), Ep(3,1,3,"C") });
        q.JumpToCommand.Execute(q.Items[2]);
        q.CurrentIndex.ShouldBe(2);
        played!.Title.ShouldBe("C");
    }
}
```

> If `AppTempDb` lives in a different namespace, match `MainViewModelTestFactory.cs`'s `using`s. If `SettingsRepository`/`LibraryRepository` ctors differ, match the factory.

**Verify Task 2:**
```
dotnet test VideoShelf.slnx -c Release --nologo -v q
```
Expected: pass, **+8 App tests**.

---

## Task 3 — App: refactor end-of-media + wire `MainViewModel` orchestration + DI

**3a. `PlayerViewModel`** (`src/VideoShelf.App/ViewModels/PlayerViewModel.cs`):

- Add near the other events (by `:239`):
  ```csharp
  /// <summary>Raised after the finished video is marked watched. The host decides what (if anything) plays next.</summary>
  public event EventHandler<EpisodeView>? PlaybackEnded;
  ```
- **Remove** the `NextEpisodeRequested` event declaration (`:239`).
- Rewrite `OnEnded` (`:333-348`) to:
  ```csharp
  private void OnEnded(object? sender, EventArgs e)
  {
      IsPlaying = false;
      if (_current is not { } cur) return;
      watch.SetWatched(cur.VideoId, true);
      PlaybackEnded?.Invoke(this, cur);
  }
  ```
  This drops the `settings.GetAutoAdvanceEpisodes()` / `library.GetNextEpisode` calls (that logic now lives in `PlayQueueViewModel.GetNextAfterEnd`). **Leave the `library` and `settings` ctor params in place** (they may be used elsewhere; do not change the 6-param ctor — avoid fan-out). If after removal the compiler flags `library`/`settings` as unused *and* they truly are unused elsewhere, leave them anyway (a benign reserved param, matching the repo's documented precedent) — do **not** alter the ctor.

**3b. `MainViewModel`** (`src/VideoShelf.App/ViewModels/MainViewModel.cs`):

- Add `PlayQueueViewModel playQueue` as a ctor param (→ **12 params**) and store `private readonly PlayQueueViewModel _playQueue = playQueue;` (match the field/assignment style used for the other VMs in this ctor).
- Expose it for the views:
  ```csharp
  public PlayQueueViewModel PlayQueue => _playQueue;
  ```
- In the ctor body where events are wired (`:42-43`), **replace** the `_player.NextEpisodeRequested` line and route everything through one play path:
  ```csharp
  _library.PlayRequested += (_, ep) => PlayEpisode(ep);
  _playQueue.PlayRequested += (_, ep) => OpenPlayer(ep);
  _player.PlaybackEnded += (_, finished) =>
  {
      var next = _playQueue.GetNextAfterEnd(finished);
      if (next is not null) OpenPlayer(next);
  };
  ```
  > **Also check the Home/Discovery play path.** If `DiscoveryViewModel` (continue-watching / recommended cards) currently starts playback via its own event that `MainViewModel` subscribes here, keep that subscription but ensure it calls `PlayEpisode(ep)` (so it funnels through `StartSingle`). If continue-watching cards play through `_library.PlayRequested` already, no change. **Read the existing subscriptions in this ctor before editing and preserve every play entry point — just make each land on `PlayEpisode`.**
- Replace `PlayEpisode` (`:131-135`) and add `OpenPlayer`:
  ```csharp
  public void PlayEpisode(EpisodeView episode)
  {
      _playQueue.StartSingle(episode);
      OpenPlayer(episode);
  }

  private void OpenPlayer(EpisodeView episode)
  {
      IsPlayerVisible = true;
      _player.Open(episode);
  }
  ```

**3c. DI** (`src/VideoShelf.App/Services/ServiceCollectionExtensions.cs`): register the queue as a singleton **before** the `MainViewModel`/`DiscoveryViewModel`/`SectionDetailViewModel` registrations (so auto-resolution finds it). Add near the other VM registrations (`:77-90`):
```csharp
services.AddSingleton<PlayQueueViewModel>();
```
`MainViewModel`, `DiscoveryViewModel`, and `SectionDetailViewModel` stay plain `AddSingleton<...>()` (auto-resolve the new param after Tasks 4–5 add it). `PlayQueueViewModel`'s ctor deps (`LibraryRepository`, `SettingsRepository`) are already registered.

**3d. Tests to update:**
- `MainViewModelTestFactory.cs` (`:29-68`): construct the queue and thread it into `discoveryVm` (Task 5), `sectionDetailVm` (Task 4), and `MainViewModel`:
  ```csharp
  var playQueue = new PlayQueueViewModel(lib, settings);
  // ...pass playQueue into DiscoveryViewModel, SectionDetailViewModel, and MainViewModel ctors
  ```
  (Add the arg to `new MainViewModel(...)` as the new last param, matching ctor order.)
- `PlayerEndOfMediaTests.cs`: **rewrite** assertions from `NextEpisodeRequested` to `PlaybackEnded`. The new contract: when the engine raises `Ended`, the VM marks the current video watched and raises `PlaybackEnded(current)`. Auto-advance is no longer the player's job, so any test asserting "auto-advance plays next" moves to `PlayQueueViewModelTests` (already covered in Task 2's `StartSingle_falls_back...`). Example:
  ```csharp
  [Fact]
  public void Ended_marks_watched_and_raises_PlaybackEnded()
  {
      var (vm, engine, watch) = NewVm(); // adapt to the file's helper
      vm.Open(SomeEpisode);
      EpisodeView? ended = null;
      vm.PlaybackEnded += (_, e) => ended = e;
      engine.RaiseEnded();               // adapt to FakePlaybackEngine's API
      ended.ShouldNotBeNull();
      // assert watch state set (read via the repo the helper exposes)
  }
  ```
  > Read the existing `PlayerEndOfMediaTests` helper (`NewVm`/how it raises Ended, how it inspects watched) and adapt. If a test relied on `settings.SetAutoAdvanceEpisodes(false)` suppressing the next-episode signal, delete/replace it — that decision now lives in the queue VM.
- `MainViewModelPlaybackTests.cs`: if it asserts end-of-media auto-advance via `MainViewModel`, update it to the new path: raise the player's `Ended`/`PlaybackEnded`, then assert `MainViewModel` opened the expected next episode (queue-driven or fallback). If it only tests `PlayEpisode`/visibility, just keep it compiling with the new factory.

**Verify Task 3:**
```
dotnet build VideoShelf.slnx -v minimal
dotnet test VideoShelf.slnx -c Release --nologo -v q
```
Expected: build clean, all tests pass. No net test-count change here beyond the rewrites (Task 2 added the queue tests).

---

## Task 4 — App: creator-page entry points (Play all + per-series + tile context menu)

**4a. `SeriesViewModel`** (`src/VideoShelf.App/ViewModels/SeriesViewModel.cs`): add commands that **bubble events** (mirror the existing `RequestRename`/`RenameRequested` pattern — read it first). Add:
```csharp
public event EventHandler? PlayAllRequested;
public event EventHandler? EnqueueRequested;
public event EventHandler? PlayNextRequested;

[RelayCommand] private void PlayAllSeries() => PlayAllRequested?.Invoke(this, EventArgs.Empty);
[RelayCommand] private void AddSeriesToQueue() => EnqueueRequested?.Invoke(this, EventArgs.Empty);
[RelayCommand] private void PlaySeriesNext() => PlayNextRequested?.Invoke(this, EventArgs.Empty);
```
(`SeriesViewModel` exposes `SeriesId` — confirm the property name; the handlers in 4b use it.)

**4b. `SectionDetailViewModel`** (`src/VideoShelf.App/ViewModels/SectionDetailViewModel.cs`):
- Add `PlayQueueViewModel playQueue` as a ctor param (→ **7 params**), store as `_playQueue`.
- Where it constructs each `SeriesViewModel` (read that block), subscribe the three new events:
  ```csharp
  svm.PlayAllRequested += (_, _) => _playQueue.PlayAll(library.GetEpisodes(svm.SeriesId));
  svm.EnqueueRequested += (_, _) => _playQueue.EnqueueRange(library.GetEpisodes(svm.SeriesId));
  svm.PlayNextRequested += (_, _) => _playQueue.PlayNextRange(library.GetEpisodes(svm.SeriesId));
  ```
  (`library` is the existing repo field — match its name. `GetEpisodes(seriesId)` returns episodes ordered by `episode_no`.)
- Add a creator-wide "Play all" command:
  ```csharp
  [RelayCommand]
  private void PlayAll() => _playQueue.PlayAll(library.GetEpisodesForSection(SectionId));
  ```

**4c. `SectionDetailView.xaml`** (`src/VideoShelf.App/Views/SectionDetailView.xaml`):
- Hero: insert a **"▶ Play all"** button in the bottom StackPanel, after the "N videos" label (`~:45`), before the tag pills. Use an additive `ui:Button` (do not re-template):
  ```xml
  <ui:Button Content="▶ Play all"
             Command="{Binding PlayAllCommand}"
             Appearance="Primary"
             Margin="0,8,0,0"
             HorizontalAlignment="Left"/>
  ```
  > It must be visible regardless of Edit mode (Play-all is consumption, not metadata editing — keep it out of the `IsEditing`-gated group).
- Series tile `ContextMenu` (the one already holding "Rename files…", `~:102`): add three items bound to the `SeriesViewModel` (the tile's DataContext) commands:
  ```xml
  <MenuItem Header="Play all" Command="{Binding PlayAllSeriesCommand}"/>
  <MenuItem Header="Play next" Command="{Binding PlaySeriesNextCommand}"/>
  <MenuItem Header="Add to queue" Command="{Binding AddSeriesToQueueCommand}"/>
  <Separator/>
  <!-- existing Rename files… item stays -->
  ```

**4d. Tests:**
- `SectionDetailViewModelTests.cs` (`:33`): add the queue arg to the ctor: `new SectionDetailViewModel(lib, tags, watch, new NullThumbs(), art, new FakeImagePicker(null), new PlayQueueViewModel(lib, settings))` (build a `SettingsRepository settings` from the same temp DB if not already present — read the file).
- Add a test: after `LoadAsync(sectionId)` on a seeded multi-series section, `vm.PlayAllCommand.Execute(null)` makes the injected queue's `HasQueue` true with `Items.Count` == total episodes. (Inject the same `PlayQueueViewModel` instance you assert on.)
- `MainViewModelTestFactory.cs`: pass the shared `playQueue` into `SectionDetailViewModel`.

**Verify Task 4:** `dotnet test VideoShelf.slnx -c Release --nologo -v q` → pass (**+~1 App test**).

---

## Task 5 — App: Home-card entry points (Add to queue / Play next)

**5a. `DiscoveryViewModel`** (`src/VideoShelf.App/ViewModels/DiscoveryViewModel.cs`):
- Add `PlayQueueViewModel playQueue` as a ctor param (store `_playQueue`).
- Add commands keyed by `VideoId` (resolve a full `EpisodeView` via the new `GetEpisode`):
  ```csharp
  [RelayCommand]
  private void EnqueueVideo(long videoId)
  {
      var ep = lib.GetEpisode(videoId);
      if (ep is not null) _playQueue.Enqueue(ep);
  }

  [RelayCommand]
  private void PlayVideoNext(long videoId)
  {
      var ep = lib.GetEpisode(videoId);
      if (ep is not null) _playQueue.PlayNext(ep);
  }
  ```
  (`lib` is the existing `LibraryRepository` field — match its name.)

**5b. `DiscoveryView.xaml`** (`src/VideoShelf.App/Views/DiscoveryView.xaml`):
- On the **continue-watching** card template **and** the **recommended-videos** card template, add a right-click `ContextMenu` whose items bind to the `DiscoveryViewModel` commands via `RelativeSource` (the card's own DataContext is the item VM, so reach the page VM through an ancestor). The card item VM exposes `VideoId` (confirm — `ContinueWatchingCardViewModel`/`RecencyCardViewModel`; if the property is named differently, match it):
  ```xml
  <Border.ContextMenu>
    <ContextMenu>
      <MenuItem Header="Play next"
                Command="{Binding PlacementTarget.Tag.PlayVideoNextCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                CommandParameter="{Binding VideoId}"/>
      <MenuItem Header="Add to queue"
                Command="{Binding PlacementTarget.Tag.EnqueueVideoCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                CommandParameter="{Binding VideoId}"/>
    </ContextMenu>
  </Border.ContextMenu>
  ```
  For `PlacementTarget.Tag` to reach the page VM, set the card root's `Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=UserControl}}"` (the `DiscoveryView` root UserControl's DataContext is the `DiscoveryViewModel`).
  > **If this RelativeSource/Tag pattern isn't already used in the repo**, the simpler robust alternative is to give the card item VMs a back-reference to their owning `DiscoveryViewModel` (set when the cards are built) and bind `Command="{Binding Owner.EnqueueVideoCommand}" CommandParameter="{Binding VideoId}"`. **Pick whichever matches existing patterns; STOP-and-report if neither binds cleanly** rather than guessing through repeated rebuilds.

**5c. Tests:**
- `DiscoveryViewModelTests.cs`: add the queue ctor arg. Add a test: seed a video, `vm.EnqueueVideoCommand.Execute(videoId)` → injected queue `HasQueue` true, `Items[0].Episode.VideoId == videoId`.
- `MainViewModelTestFactory.cs`: pass the shared `playQueue` into `DiscoveryViewModel`.

**Verify Task 5:** `dotnet test VideoShelf.slnx -c Release --nologo -v q` → pass (**+~1 App test**).

---

## Task 6 — App: up-next UI (in-player drawer + Queue page + nav)

> Theming rule: **additive only** — no re-templating WPF-UI controls. Reuse `DesignTokens.xaml` keys (`CardRadius`, `SubtleFillBrush`, `AccentBrush`, `DividerBrush`, `SectionHeader`, `Caption`). To avoid duplicating the queue-item row, define a shared `DataTemplate x:Key="QueueItemTemplate"` and a `Style x:Key="QueueListStyle"` in `DesignTokens.xaml` (or a new merged `Resources/QueueStyles.xaml` added to `App.xaml`'s merged dictionaries), and reference it from both the drawer and the page.

**6a. Shared queue-item template** (add to `DesignTokens.xaml`, or a new dict merged in `App.xaml`):
```xml
<DataTemplate x:Key="QueueItemTemplate">
  <Border Padding="8,6" Margin="0,0,0,4" CornerRadius="{StaticResource ControlRadius}"
          Background="{StaticResource SubtleFillBrush}">
    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="4"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <!-- now-playing accent bar -->
      <Border Grid.Column="0" Width="3" CornerRadius="2"
              Background="{StaticResource AccentBrush}"
              Visibility="{Binding IsNowPlaying, Converter={StaticResource BoolToVisibility}}"/>
      <TextBlock Grid.Column="1" Text="{Binding Title}" VerticalAlignment="Center"
                 Margin="8,0,0,0" TextTrimming="CharacterEllipsis"
                 FontWeight="{Binding IsNowPlaying, Converter={StaticResource ...}}"/>
      <StackPanel Grid.Column="2" Orientation="Horizontal">
        <ui:Button Content="▲" Appearance="Transparent" Padding="4"
                   Command="{Binding DataContext.MoveUpCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                   CommandParameter="{Binding}"/>
        <ui:Button Content="▼" Appearance="Transparent" Padding="4"
                   Command="{Binding DataContext.MoveDownCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                   CommandParameter="{Binding}"/>
        <ui:Button Content="▶" Appearance="Transparent" Padding="4"
                   Command="{Binding DataContext.JumpToCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                   CommandParameter="{Binding}"/>
        <ui:Button Content="✕" Appearance="Transparent" Padding="4"
                   Command="{Binding DataContext.RemoveItemCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                   CommandParameter="{Binding}"/>
      </StackPanel>
    </Grid>
  </Border>
</DataTemplate>
```
> The `ItemsControl.DataContext` for these must be the `PlayQueueViewModel`. Drop the bold `FontWeight` converter if there's no clean existing one — a simple DataTrigger in the template or leaving weight constant is fine (keep it simple; STOP-and-report if a converter is missing rather than inventing one). The `ui:` namespace must be declared in whichever file holds the template; if `DesignTokens.xaml` doesn't already import WPF-UI, put this template in the new `QueueStyles.xaml` (declare `xmlns:ui`).

**6b. In-player drawer** (`src/VideoShelf.App/Views/PlayerView.xaml`):
- Add two transport buttons inside the **secondary-controls StackPanel** (`:96-129`, the group that collapses in PiP), bound through the player's `MainViewModel` DataContext to `PlayQueue`:
  ```xml
  <ui:Button Content="⏭" Appearance="Transparent"
             ToolTip="Skip to next"
             Command="{Binding PlayQueue.SkipNextCommand}"
             Visibility="{Binding PlayQueue.HasQueue, Converter={StaticResource BoolToVisibility}}"/>
  <ui:Button Content="☰ Up next" Appearance="Transparent"
             Command="{Binding PlayQueue.ToggleDrawerCommand}"
             Visibility="{Binding PlayQueue.HasQueue, Converter={StaticResource BoolToVisibility}}"/>
  ```
- Add the **drawer panel** as a child of `OverlayRoot` (`:21`), as a **sibling of `ControlsLayer`** (so it isn't auto-hidden with the transport), right-aligned, opaque, with its own visibility. It is a child of the VideoView content (opaque → renders over video, same as the controls do). Collapse it in PiP:
  ```xml
  <Border x:Name="QueueDrawer"
          HorizontalAlignment="Right" VerticalAlignment="Stretch"
          Width="320" Background="#E6101010"
          BorderBrush="{StaticResource DividerBrush}" BorderThickness="1,0,0,0">
    <Border.Style>
      <Style TargetType="Border">
        <Setter Property="Visibility" Value="Collapsed"/>
        <Style.Triggers>
          <!-- open only when toggled AND not in PiP -->
          <MultiDataTrigger>
            <MultiDataTrigger.Conditions>
              <Condition Binding="{Binding PlayQueue.IsQueueOpen}" Value="True"/>
              <Condition Binding="{Binding IsPictureInPicture}" Value="False"/>
            </MultiDataTrigger.Conditions>
            <Setter Property="Visibility" Value="Visible"/>
          </MultiDataTrigger>
        </Style.Triggers>
      </Style>
    </Border.Style>
    <DockPanel Margin="12">
      <Grid DockPanel.Dock="Top">
        <TextBlock Text="UP NEXT" Style="{StaticResource SectionHeader}"/>
        <ui:Button Content="Clear" Appearance="Transparent" HorizontalAlignment="Right"
                   Command="{Binding PlayQueue.ClearCommand}"/>
      </Grid>
      <ItemsControl DataContext="{Binding PlayQueue}"
                    ItemsSource="{Binding Items}"
                    ItemTemplate="{StaticResource QueueItemTemplate}"
                    Margin="0,8,0,0"/>
    </DockPanel>
  </Border>
  ```
  > Confirm `OverlayRoot` is a `Grid` (overlapping children allowed). Place `QueueDrawer` **after** `ControlsLayer` in markup so it draws on top. The `DataContext="{Binding PlayQueue}"` on the `ItemsControl` makes the template's `RelativeSource AncestorType=ItemsControl` resolve commands on the queue VM. `MultiDataTrigger` conditions read from the PlayerView's DataContext (`MainViewModel`) — `IsPictureInPicture` and `PlayQueue.IsQueueOpen` are both there.

**6c. Queue page** (`AppView.Queue`):
- `MainViewModel.cs`: add `Queue` to `enum AppView` (`:11`). Add nav:
  ```csharp
  [RelayCommand]
  private void ShowQueue()
  {
      PushNav(CurrentView);
      CurrentView = AppView.Queue;
  }
  ```
  (Match the exact `PushNav`/nav idiom used by `ShowSettings`/`OpenSectionAsync` — read those. Top-level nav clears back-stack; a detail-like page pushes. Treat Queue like a detail page: push, so Back returns.)
- New view `src/VideoShelf.App/Views/QueuePageView.xaml` (+ `.xaml.cs` with parameterless `InitializeComponent`). DataContext is inherited (`MainViewModel`); bind to `PlayQueue`:
  ```xml
  <UserControl ... xmlns:ui="...">
    <Grid Margin="24">
      <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="*"/>
      </Grid.RowDefinitions>
      <Grid Grid.Row="0" Margin="0,0,0,12">
        <StackPanel Orientation="Horizontal">
          <TextBlock Text="UP NEXT" Style="{StaticResource SectionHeader}" VerticalAlignment="Center"/>
          <TextBlock Text="{Binding PlayQueue.CountLabel}" Style="{StaticResource Caption}"
                     Margin="12,0,0,0" VerticalAlignment="Center"/>
        </StackPanel>
        <ui:Button Content="Clear all" Appearance="Secondary" HorizontalAlignment="Right"
                   Command="{Binding PlayQueue.ClearCommand}"/>
      </Grid>
      <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
        <ItemsControl DataContext="{Binding PlayQueue}"
                      ItemsSource="{Binding Items}"
                      ItemTemplate="{StaticResource QueueItemTemplate}"/>
      </ScrollViewer>
      <TextBlock Grid.Row="1" Text="The queue is empty." Style="{StaticResource Caption}"
                 HorizontalAlignment="Center" VerticalAlignment="Center"
                 Visibility="{Binding PlayQueue.HasQueue, Converter={StaticResource ...InverseBool...}}"/>
    </Grid>
  </UserControl>
  ```
  > For the empty-state inverse visibility, reuse whatever inverse-bool pattern the repo already has (e.g. the empty-library CTA from M11). If none exists, omit the empty-state label (don't invent a converter).
- `MainWindow.xaml`: host `QueuePageView` gated by the nav converter the other hosts use (`EnumSetToVisibility` / `EnumToVisibility`) — copy an existing host entry (e.g. the Settings host) and swap the view + enum value to `Queue`. **Use `DataContext.CurrentView` binding** (per the M6 critical-bug lesson: never bind `CurrentView` against the Window).
- Nav chrome: add an **"Up next"** button to the top nav (near Home/Browse/⚙), visible when `PlayQueue.HasQueue`, command `ShowQueueCommand`:
  ```xml
  <ui:Button Content="Up next" Appearance="Transparent"
             Command="{Binding ShowQueueCommand}"
             Visibility="{Binding PlayQueue.HasQueue, Converter={StaticResource BoolToVisibility}}"/>
  ```
  (Optionally add the active-underline sibling like the other nav items, matching `EnumSetToVisibility` usage — additive.)

**6d. Tests:** add a `MainViewModelNavigationTests` case: `vm.ShowQueueCommand.Execute(null)` sets `CurrentView == AppView.Queue` and `GoBack()` returns to the prior view. (Use the factory.)

**Verify Task 6:**
```
dotnet build VideoShelf.slnx -v minimal
dotnet test VideoShelf.slnx -c Release --nologo -v q
```
Expected: clean build (XAML compiles), tests pass (**+~1 App test**). Visual correctness is Task 7.

---

## Task 7 — Harness hooks + visual sweep (subagent text verdict)

> The sweep needs **pwsh 7** + an **unlocked, composited desktop**, and **no fullscreen-stealing app** in the foreground (the ROADMAP records League of Legends / Webcam Recorder bleeding into GDI grabs — enumerate top-level windows / close stray media windows first). Screenshots are inspected by a **Sonnet subagent that returns a TEXT verdict + file paths — never load PNGs into the controller.**

**7a. `HarnessRunner`** (find under `src/VideoShelf.App`, the class wired in M6): add coverage for the new surfaces. Read how existing `--view` cases are dispatched and `SeedDemoAsync` seeds data, then:
- **`--view Queue`**: ensure the demo library has a multi-episode creator, build a queue via `MainViewModel.PlayQueue.PlayAll(library.GetEpisodesForSection(sectionId))` (use the richest section, like `FindRichestSeriesAsync`'s section), then `CurrentView = AppView.Queue`, settle, signal.
- **In-player drawer**: extend the existing `--view Player` (or add `--view PlayerQueue`) path to also `PlayQueue.PlayAll(...)` then set `PlayQueue.IsQueueOpen = true` and `Player.AutoHideSuppressed = true`, so the capture shows the drawer over live video. (The richest-series clip is what actually plays per the M13 note — build the queue from that section.)

**7b. Sweep script** (`Run-VisualSweep.ps1` + `Generate-Fixtures.ps1`, pwsh-7): add the new view(s) to the relaunch-per-view loop (one launch → wait on `--done-signal` → GDI grab → PNG). Reuse the M6 capture gotchas already baked in (wait for `IsWindowVisible`, ~5s Mica settle, TOPMOST→NOTOPMOST toggle).

**7c. Verdict** — dispatch a **Sonnet subagent** to Read the new PNGs and return PASS/FAIL + observations + absolute paths. Acceptance criteria:
- **Creator page**: a "▶ Play all" button is visible in the hero (not gated behind Edit).
- **Queue page**: lists the queued episodes in order, one row highlighted as now-playing (accent bar), with up/down/▶/✕ controls and a "Clear all" action; header shows "N in queue".
- **In-player drawer**: renders as an opaque right-side panel **over the live video** (not black surround — it's an opaque overlay child, the M10 transparency trap does not apply), lists items, now-playing row highlighted, "Up next"/skip buttons present in the transport. Confirm **no transport/queue bleed** onto non-player views (the M8→M10 regression class).
- **PiP**: the drawer is **collapsed** in PiP (verify the player still shrinks to the corner cleanly).

If a criterion FAILS, fix additively and re-sweep. Only surface a PNG to the user if they explicitly ask to see one.

---

## Task 8 — Verification, PR, CI, merge, ROADMAP flip

1. **Full gate:**
   ```
   dotnet build VideoShelf.slnx -v minimal
   dotnet test VideoShelf.slnx -c Release --nologo -v q
   ```
   Expected total ≈ **132 Core + ~165 App** (M13 baseline 130 Core + 153 App, plus +2 Core and ~+12 App from this milestone). Report the actual final count.
2. **Whole-branch self-review** (`superpowers:requesting-code-review` mindset): re-check the orchestration contract — exactly one play path (`OpenPlayer`), no double-marking watched, queue cursor stays valid after remove/move, no `ConfigureAwait(false)` on any UI-collection mutation (the `Items` ObservableCollection is UI-bound — all mutations must stay on the UI thread; `GetNextAfterEnd`/commands run on the UI thread already, keep it that way).
3. **PR** from `feat/play-queue` (author `yovanmc` + Claude trailer). Open with `& "C:\Program Files\GitHub CLI\gh.exe" pr create ...`.
4. **Watch CI in the foreground:** sleep ~20s, then `& "C:\Program Files\GitHub CLI\gh.exe" pr checks <PR#> --watch`.
5. **Merge** from the **main repo root** (not the worktree): `gh pr merge <PR#> --merge --delete-branch`; sync `main`.
6. **Flip the ROADMAP row** (rides this branch): M14 → `✅ Merged`, link this plan + the PR, one-line shipped summary; append a decision-log entry with the durable facts (final ctor counts: `MainViewModel` 12, `SectionDetailViewModel` 7, `DiscoveryViewModel` +1; `PlayerViewModel` ctor unchanged at 6 but `OnEnded` now raises `PlaybackEnded`; the queue-first orchestration contract; `GetEpisodesForSection`/`GetEpisode` added; ephemeral/no-migration; whichever Home-card binding pattern was chosen). Set M15 expectations unchanged.

---

## Risk notes / STOP-and-report triggers

- **The end-of-media refactor is the highest-risk change** — it moves auto-advance ownership out of `PlayerViewModel`. If removing `NextEpisodeRequested` breaks a reference you didn't expect (grep `NextEpisodeRequested` across the solution first), STOP and report the call sites.
- **Home-card command binding** (Task 5b) — if neither the `PlacementTarget.Tag` RelativeSource nor an `Owner` back-reference binds cleanly to the page VM, STOP rather than rebuild-guessing.
- **Scope size** — this is a large milestone (8 tasks, two UI surfaces, reorder, Home cards). If any single task balloons well past its described shape, STOP and report so the milestone can be split rather than partially landed.
- **Column names** in Task 1's SQL are inferred from the digest — verify against `GetEpisodes`/`GetSeriesForSection` before trusting them.
- Keep every change **additive** to WPF-UI controls (theming rule). The queue drawer/page use plain Borders/StackPanels + tokens, never re-templated WPF-UI controls.
