# M8 — Home + Search redesign (creator-centric) — implementation plan

> **Written for Sonnet execution.** Every task is bite-sized with exact files, complete
> code, exact commands, and expected output. **If something doesn't match what's described
> here (a signature, a file's structure, a missing method), STOP and report rather than
> guess.** This plan was authored against verbatim excerpts captured 2026-06-12; small
> drift is possible.

## Context & goal

VideoShelf v2 re-presents the library around **creators**. M7 shipped the creator read-model
and two reusable cards (`CreatorCard` = thumbnail + name + "N videos"; `VideoCard` = video
thumbnail). **M8 consumes those cards on Home and Search:**

- **Home = a curated funnel.** Rails, in order:
  1. **Continue watching** — video cards (unchanged).
  2. **Recommended creators** — creator cards (NEW; replaces the old "For you" section-card rail).
  3. **Recommended videos** — video cards (NEW).
  4. **Recently added** — video cards (unchanged).
  5. **Recently watched** — video cards (unchanged).
  6. **Pick a tag** — tag chips → results now rendered as **creator cards** (MIGRATED from the
     old `SectionCardViewModel`).
- **Search = a dedicated view** reached from a **persistent top search box**. Results are two
  grouped sections: **Creators** (creator cards) and **Videos** (video cards).

**Design decisions locked with the user (2026-06-12):** keep all Home rails (don't strip to a
minimal funnel); migrate Pick-a-tag to the new card system; Search lives in its own
`AppView.Search` opened from a top search box; For-you is **two homogeneous sub-rails**
(creators, then videos) — not one interleaved rail.

### Conventions (from ROADMAP.md)

- Build: `dotnet build VideoShelf.slnx -c Release -v minimal`
- Test gate: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
- `gh` is **not on PATH** → `& "C:\Program Files\GitHub CLI\gh.exe"`.
- Work in a worktree under `.worktrees/`; **direct pushes to `main` are blocked** — ship via branch + PR.
- Commit author `yovanmc` + trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. No Codex trailer. Merge `--merge` (no squash) from the **main repo root**.
- Theming rule: **additive only** — never re-base a WPF-UI control template for cosmetics.
- Cross-thread rule: **never `ConfigureAwait(false)`** on a chain that ends by mutating a UI-bound `ObservableCollection`.
- `RangeBase.Value` (ProgressBar/Slider) binds **TwoWay by default** — pin `Mode=OneWay` when bound to a read-only property.

### Baseline

222 tests at M7 (105 Core + 117 App), 0 failures. This milestone should land **all green** with a higher count.

---

## Known shapes (verbatim, captured 2026-06-12)

**Core models** (`src\VideoShelf.Core\` — `Models\BrowseModels.cs`, `Discovery\DiscoveryModels.cs`):

```csharp
public sealed record SectionSummary(
    long SectionId, long SourceId, string DisplayName, int SeriesCount, int UnwatchedCount,
    int VideoCount, string? ThumbnailSeedPath);

public sealed record EpisodeView(
    long VideoId, long SeriesId, string FilePath, int EpisodeNo, string Title,
    bool Watched, bool Missing);

public sealed record RecencyItem(
    long VideoId, long SeriesId, long SectionId, string SeriesTitle, bool IsStandalone,
    int EpisodeNo, bool Watched, string? ThumbnailSeedPath);

public sealed record SectionSuggestion(
    long SectionId, string DisplayName, int SeriesCount, int EpisodeCount, int UnwatchedCount,
    IReadOnlyList<string> Tags, double Score);
```

**Card VMs:** `CreatorCardViewModel` + `CreatorsViewModel` live in namespace `VideoShelf.App.ViewModels`;
`ContinueWatchingCardViewModel`, `RecencyCardViewModel`, `SectionCardViewModel`, `TagChipViewModel`
live in `VideoShelf.App.ViewModels.Discovery`.

```csharp
// VideoShelf.App.ViewModels
public partial class CreatorCardViewModel : ObservableObject
{
    public CreatorCardViewModel(SectionSummary summary, string? overrideArtPath, IThumbnailService thumbnails);
    public long SectionId { get; }
    public string Name { get; }
    public int VideoCount { get; }
    public string VideoCountLabel { get; }
    [ObservableProperty] private string? _imagePath;
    public event Action<long>? OpenRequested;     // raised by the Open relay command
    [RelayCommand] private void Open();
    public async Task LoadImageAsync(CancellationToken ct);
}

// VideoShelf.App.ViewModels.Discovery
public sealed partial class RecencyCardViewModel(RecencyItem item) : ObservableObject
{
    public long VideoId { get; }
    public long SeriesId { get; }
    public string SeriesTitle { get; }
    public string EpisodeLabel { get; }
    public bool Watched { get; }
    public string? ThumbnailSeedPath { get; }
    [ObservableProperty] private string? _thumbnailPath;
    public event EventHandler? PlayInvoked;
    [RelayCommand] private void Play();
}
```

**`VideoCard.xaml`** binds `PlayCommand`, `ThumbnailPath`, `ProgressFraction` (`Mode=OneWay`),
`SeriesTitle`, `EpisodeLabel`. (`RecencyCardViewModel` has no `ProgressFraction` → the 3px bar
renders empty; this is already true for the Recently-added/watched rails and is fine — `duration`
is never populated app-wide, a pre-existing limitation.)
**`CreatorCard.xaml`** binds `OpenCommand`, `ImagePath` (`IsAsync=True`), `Name`, `VideoCountLabel`.

**`DiscoveryRepository`** ctor `(VideoShelfDb db, LibraryRepository library, TagRepository tags)`;
`const double HalfLifeDays` exists; private helpers `ReadWatchedTags()`, `ReadWatchedSectionIds()`,
`ReadSectionStats()` exist (used by `GetForYou`).

**`LibraryRepository`** has `GetSectionSummaries() : IReadOnlyList<SectionSummary>`,
`GetEpisodes(long seriesId) : IReadOnlyList<EpisodeView>`, `Search(string query) : IReadOnlyList<SearchHit>`,
and the LIKE-escaping idiom `query.Trim().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")` with `ESCAPE '\'`.

**`MainViewModel`** ctor (9 params today): `(SourcesViewModel, LibraryViewModel, IScanCoordinator,
PlayerViewModel, SettingsViewModel, DiscoveryViewModel, SectionDetailViewModel, RenameToolViewModel,
CreatorsViewModel)`. `enum AppView { Home, Browse, SectionDetail, RenameTool }`. Play routing:
`PlayEpisode(EpisodeView)`; nav: `OpenSectionAsync(long)`. Event-wiring lives at the end of the ctor.

---

## Task 1 — Core: recommended-videos query + scoring refactor

**File:** `src\VideoShelf.Core\Discovery\DiscoveryRepository.cs`

**1a.** Refactor the body of `GetForYou` into a reusable private `ScoreSections(now)` that returns the
**full ordered list** (no `Take`). Replace the existing `GetForYou` method with:

```csharp
public IReadOnlyList<SectionSuggestion> GetForYou(int limit, DateTimeOffset now) =>
    ScoreSections(now).Take(limit).ToList();

private List<SectionSuggestion> ScoreSections(DateTimeOffset now)
{
    var history = ReadWatchedTags();
    if (history.Count == 0) return [];
    var affinity = DiscoveryScoring.BuildTagAffinity(history, now, HalfLifeDays);
    var watchedSections = ReadWatchedSectionIds();

    var scored = new List<SectionSuggestion>();
    foreach (var sec in ReadSectionStats())
    {
        if (watchedSections.Contains(sec.SectionId)) continue;
        var secTags = tags.GetTags(sec.SectionId);
        var score = DiscoveryScoring.ScoreSection(secTags, affinity, sec.UnwatchedCount, sec.EpisodeCount);
        if (score <= 0) continue;
        scored.Add(sec with { Tags = secTags, Score = score });
    }
    return scored
        .OrderByDescending(s => s.Score)
        .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
```

> **STOP if** the existing `GetForYou` body differs materially from the excerpt in this plan
> (e.g. different helper names than `ReadWatchedTags`/`ReadWatchedSectionIds`/`ReadSectionStats`,
> or no `HalfLifeDays` const). Report what you find.

**1b.** Add a new public method that returns **unwatched videos from the recommended creators**,
ordered by recommendation rank then recency:

```csharp
public IReadOnlyList<RecencyItem> GetRecommendedVideos(int limit, DateTimeOffset now)
{
    var sections = ScoreSections(now);
    if (sections.Count == 0) return [];

    // Map each recommended section to its rank (recommendation order).
    var rank = new Dictionary<long, int>();
    for (var i = 0; i < sections.Count; i++) rank[sections[i].SectionId] = i;

    // section ids are trusted integer keys from our own DB (never user input), so an
    // inlined IN-list is safe here and avoids dynamic LIKE/param plumbing.
    var ids = string.Join(",", rank.Keys);
    using var conn = db.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = $"""
        SELECT v.id, v.series_id, s.section_id, s.base_title, s.is_standalone,
               v.episode_no, v.watched, v.thumbnail_path
        FROM videos v
        JOIN series s ON s.id = v.series_id
        WHERE v.missing = 0 AND v.watched = 0 AND s.section_id IN ({ids})
        ORDER BY v.added_at DESC, v.id DESC;
        """;
    var all = new List<RecencyItem>();
    using var r = cmd.ExecuteReader();
    while (r.Read())
        all.Add(new RecencyItem(
            VideoId: r.GetInt64(0), SeriesId: r.GetInt64(1), SectionId: r.GetInt64(2),
            SeriesTitle: r.GetString(3), IsStandalone: r.GetInt64(4) != 0,
            EpisodeNo: r.GetInt32(5), Watched: r.GetInt64(6) != 0,
            ThumbnailSeedPath: r.IsDBNull(7) ? null : r.GetString(7)));

    return all
        .OrderBy(v => rank[v.SectionId])
        .ThenByDescending(v => v.VideoId)
        .Take(limit)
        .ToList();
}
```

**1c. Tests** — `tests\VideoShelf.Core.Tests\Discovery\DiscoveryRepositoryTests.cs`.
Open this file, find the **existing `GetForYou` test** and reuse its exact arrange idiom for seeding
**tags + watch history** (the precise `TagRepository`/`WatchRepository` calls are whatever that test
already uses — do not invent new ones). Add two tests:

1. `GetRecommendedVideos_returns_unwatched_videos_from_recommended_sections` — arrange so one section
   (A) is watched (builds tag affinity) and another section (B) shares A's tag, is **unwatched**, and
   has ≥2 episodes; assert `GetForYou` includes B's section and `GetRecommendedVideos(10, now)` returns
   B's video ids (all unwatched), and contains **no** watched or missing videos.
2. `GetRecommendedVideos_returns_empty_without_history` — a fresh fixture with no watch events →
   `GetRecommendedVideos(10, now)` is empty.

> **STOP if** there is no existing `GetForYou` test to mirror — report so I can specify the seeding calls.

---

## Task 2 — Core: creator + video search queries

**File:** `src\VideoShelf.Core\Storage\LibraryRepository.cs`. Keep the existing `Search(string query)`
untouched. Add two methods (place near `Search`). They reuse the same LIKE-escaping idiom and
mirror `GetSectionSummaries` for the creator shape.

```csharp
public IReadOnlyList<SectionSummary> SearchCreators(string query, int limit)
{
    if (string.IsNullOrWhiteSpace(query)) return [];
    var pattern = "%" + query.Trim()
        .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

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
        WHERE sc.display_name LIKE $q ESCAPE '\'
        GROUP BY sc.id, sc.source_id, sc.display_name
        ORDER BY sc.display_name
        LIMIT $limit
        """;
    cmd.Parameters.AddWithValue("$q", pattern);
    cmd.Parameters.AddWithValue("$limit", limit);
    var list = new List<SectionSummary>();
    using var r = cmd.ExecuteReader();
    while (r.Read())
        list.Add(new SectionSummary(
            SectionId: r.GetInt64(0), SourceId: r.GetInt64(1), DisplayName: r.GetString(2),
            SeriesCount: r.GetInt32(3), UnwatchedCount: r.GetInt32(4), VideoCount: r.GetInt32(5),
            ThumbnailSeedPath: r.IsDBNull(6) ? null : r.GetString(6)));
    return list;
}

public IReadOnlyList<RecencyItem> SearchVideos(string query, int limit)
{
    if (string.IsNullOrWhiteSpace(query)) return [];
    var pattern = "%" + query.Trim()
        .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    using var conn = db.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT v.id, v.series_id, s.section_id, s.base_title, s.is_standalone,
               v.episode_no, v.watched, v.thumbnail_path
        FROM videos v
        JOIN series s ON s.id = v.series_id
        WHERE v.missing = 0
          AND (v.raw_filename LIKE $q ESCAPE '\' OR s.base_title LIKE $q ESCAPE '\')
        ORDER BY s.base_title, v.episode_no
        LIMIT $limit
        """;
    cmd.Parameters.AddWithValue("$q", pattern);
    cmd.Parameters.AddWithValue("$limit", limit);
    var list = new List<RecencyItem>();
    using var r = cmd.ExecuteReader();
    while (r.Read())
        list.Add(new RecencyItem(
            VideoId: r.GetInt64(0), SeriesId: r.GetInt64(1), SectionId: r.GetInt64(2),
            SeriesTitle: r.GetString(3), IsStandalone: r.GetInt64(4) != 0,
            EpisodeNo: r.GetInt32(5), Watched: r.GetInt64(6) != 0,
            ThumbnailSeedPath: r.IsDBNull(7) ? null : r.GetString(7)));
    return list;
}
```

**Tests** — new file `tests\VideoShelf.Core.Tests\Storage\LibrarySearchTests.cs`, using the project's
`TempDb` + `LibraryRepository` arrange idiom (mirror `DiscoveryRepositoryTests`'s `NewFixture`/`AddVideo`
helpers — `UpsertSource`/`UpsertSection`/`UpsertSeries`/`UpsertVideo`). Cover:

- `SearchCreators` matches a section by `display_name` substring, is case-insensitive (SQLite `LIKE`
  is case-insensitive for ASCII), returns the correct `VideoCount`, respects `limit`, and `""` → empty.
- `SearchVideos` matches by `raw_filename` **and** by series `base_title`, excludes `missing=1` videos,
  respects `limit`, and `""` → empty.

> **STOP if** `videos.raw_filename` or `series.base_title`/`series.is_standalone` columns don't exist
> (they're used by the existing `Search`/`GetContinueWatching` — they should). Report.

---

## Task 3 — App: `CreatorCardFactory` + DI

A small factory centralizes "build a creator card from a `SectionSummary`, applying the art override,
and kick off the async image load" so Home and Search build identical cards.

**New file** `src\VideoShelf.App\ViewModels\CreatorCardFactory.cs`:

```csharp
using VideoShelf.App.Services;          // IThumbnailService (adjust if it lives elsewhere)
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;          // CreatorArtRepository

namespace VideoShelf.App.ViewModels;

/// Builds a CreatorCardViewModel from a SectionSummary, applying the user's art override
/// (if any) and starting the background thumbnail load. Used by Home and Search rails.
public sealed class CreatorCardFactory(CreatorArtRepository art, IThumbnailService thumbnails)
{
    public CreatorCardViewModel Create(SectionSummary summary)
    {
        var overridePath = art.GetArtPath(summary.SectionId);   // see STOP note
        var card = new CreatorCardViewModel(summary, overridePath, thumbnails);
        _ = card.LoadImageAsync(CancellationToken.None);
        return card;
    }
}
```

> **STOP / mirror:** open `src\VideoShelf.App\ViewModels\CreatorsViewModel.cs` and use the **exact**
> `CreatorArtRepository` call it already uses to read a single section's override path (the method
> may be named differently than `GetArtPath`, and it may currently be fetched as a batch dictionary).
> Match that call here. Also confirm the `using` for `IThumbnailService` (it's the same namespace
> `CreatorsViewModel` imports). A per-card lookup is acceptable: Home rails are capped at 24 and
> Search at 48, all on quick SQLite reads. If the override lookup is only exposed as a "get-all"
> batch, add a single-id convenience overload on `CreatorArtRepository` (or call the batch once and
> index it) — STOP and report which you chose if it's non-obvious.

**DI** — `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`, in `AddVideoShelf`, register
alongside the other singletons (`CreatorArtRepository` and the thumbnail service are already registered):

```csharp
services.AddSingleton<CreatorCardFactory>();
```

---

## Task 4 — App: Home rails rework (`DiscoveryViewModel` + `DiscoveryView.xaml`)

**4a. `src\VideoShelf.App\ViewModels\Discovery\DiscoveryViewModel.cs`** — apply these changes:

1. Add `CreatorCardFactory cards` to the primary-constructor parameter list:

```csharp
public sealed partial class DiscoveryViewModel(
    DiscoveryRepository discovery, LibraryRepository library, TagRepository tags,
    CreatorCardFactory cards) : ObservableObject
```

2. Replace the `ForYou` collection (and its `SectionCardViewModel` usage) with two new rails, and
   change `TagResults` to creator cards. The collection block becomes:

```csharp
public ObservableCollection<ContinueWatchingCardViewModel> ContinueWatching { get; } = [];
public ObservableCollection<CreatorCardViewModel> RecommendedCreators { get; } = [];
public ObservableCollection<RecencyCardViewModel> RecommendedVideos { get; } = [];
public ObservableCollection<RecencyCardViewModel> RecentlyAdded { get; } = [];
public ObservableCollection<RecencyCardViewModel> RecentlyWatched { get; } = [];
public ObservableCollection<TagChipViewModel> AvailableTags { get; } = [];
public ObservableCollection<CreatorCardViewModel> TagResults { get; } = [];
```

3. Replace the `Has*` flags + `IsEmpty`:

```csharp
public bool HasContinueWatching => ContinueWatching.Count > 0;
public bool HasRecommendedCreators => RecommendedCreators.Count > 0;
public bool HasRecommendedVideos => RecommendedVideos.Count > 0;
public bool HasRecentlyAdded => RecentlyAdded.Count > 0;
public bool HasRecentlyWatched => RecentlyWatched.Count > 0;
public bool HasTags => AvailableTags.Count > 0;
public bool HasTagResults => TagResults.Count > 0;
public bool IsEmpty =>
    !HasContinueWatching && !HasRecommendedCreators && !HasRecommendedVideos
    && !HasRecentlyAdded && !HasRecentlyWatched && !HasTags;
```

4. Add a `SectionId → SectionSummary` cache field and replace `LoadAsync`:

```csharp
private Dictionary<long, SectionSummary> _summaryById = new();

public async Task LoadAsync()
{
    var now = DateTimeOffset.UtcNow;
    var data = await Task.Run(() => (
        cont: discovery.GetContinueWatching(RailLimit),
        forYou: discovery.GetForYou(RailLimit, now),
        recVideos: discovery.GetRecommendedVideos(RailLimit, now),
        added: discovery.GetRecentlyAdded(RailLimit),
        watched: discovery.GetRecentlyWatched(RailLimit),
        tagCounts: tags.GetTagCounts(),
        summaries: library.GetSectionSummaries()));

    _summaryById = data.summaries.ToDictionary(s => s.SectionId);

    Fill(ContinueWatching, data.cont, MakeContinueCard);
    FillCreators(RecommendedCreators, data.forYou);
    Fill(RecommendedVideos, data.recVideos, MakeRecencyCard);
    Fill(RecentlyAdded, data.added, MakeRecencyCard);
    Fill(RecentlyWatched, data.watched, MakeRecencyCard);

    AvailableTags.Clear();
    foreach (var tc in data.tagCounts) AvailableTags.Add(new TagChipViewModel(tc.Tag, tc.SectionCount));
    TagResults.Clear();

    RaiseAllHasFlags();
}

private void FillCreators(ObservableCollection<CreatorCardViewModel> target, IReadOnlyList<SectionSuggestion> items)
{
    target.Clear();
    foreach (var s in items)
        if (_summaryById.TryGetValue(s.SectionId, out var summary))
            target.Add(MakeCreatorCard(summary));
}

private CreatorCardViewModel MakeCreatorCard(SectionSummary summary)
{
    var card = cards.Create(summary);
    card.OpenRequested += id => SectionOpenRequested?.Invoke(this, id);
    return card;
}
```

5. Replace the `ToggleTag` command so tag results are creator cards:

```csharp
[RelayCommand]
private async Task ToggleTag(TagChipViewModel chip)
{
    chip.IsSelected = !chip.IsSelected;
    var selected = AvailableTags.Where(t => t.IsSelected).Select(t => t.Tag).ToList();
    var results = selected.Count == 0
        ? Array.Empty<SectionSuggestion>()
        : await Task.Run(() => discovery.GetSectionsByTags(selected, RailLimit));
    TagResults.Clear();
    foreach (var s in results)
        if (_summaryById.TryGetValue(s.SectionId, out var summary))
            TagResults.Add(MakeCreatorCard(summary));
    OnPropertyChanged(nameof(HasTagResults));
}
```

6. **Delete** the now-unused `MakeSectionCard` helper. Update `RaiseAllHasFlags` to raise the new flag
   names (`HasRecommendedCreators`, `HasRecommendedVideos`, dropping `HasForYou`). Keep `MakeContinueCard`,
   `MakeRecencyCard`, `Fill<>`, `RaisePlay`, and the `PlayRequested`/`SectionOpenRequested` events as-is.
   Add `using VideoShelf.App.ViewModels;` (for `CreatorCardViewModel`/`CreatorCardFactory`) if not present.

> `SectionCardViewModel` is now unused by Home. **Leave the file in place** (removing it would churn its
> tests) — note it in the PR description as a deferred cleanup candidate.

**4b. `src\VideoShelf.App\Views\DiscoveryView.xaml`** — read the full file first. It currently has a
"Continue watching" rail (horizontal `ItemsControl` of `<views:VideoCard/>`), a "For you" rail bound to
`ForYou`/`HasForYou` (using a section-style template), "Recently added"/"Recently watched" rails of
`<views:VideoCard/>`, a tag-chip `WrapPanel`, and a `TagResults` rail. Make these edits:

1. **Replace** the "For you" rail block with **two** rails placed right after Continue-watching, mirroring
   the existing horizontal-rail markup (same `ScrollViewer`+`ItemsControl`+horizontal `StackPanel` panel):

```xml
<!-- Recommended creators -->
<StackPanel Visibility="{Binding HasRecommendedCreators, Converter={StaticResource BoolToVisibility}}">
    <TextBlock Text="Recommended creators" FontSize="18" FontWeight="SemiBold" Margin="0,16,0,8"/>
    <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled">
        <ItemsControl ItemsSource="{Binding RecommendedCreators}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate><StackPanel Orientation="Horizontal"/></ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate><views:CreatorCard Margin="0,0,12,0"/></DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </ScrollViewer>
</StackPanel>

<!-- Recommended videos -->
<StackPanel Visibility="{Binding HasRecommendedVideos, Converter={StaticResource BoolToVisibility}}">
    <TextBlock Text="Recommended videos" FontSize="18" FontWeight="SemiBold" Margin="0,16,0,8"/>
    <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled">
        <ItemsControl ItemsSource="{Binding RecommendedVideos}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate><StackPanel Orientation="Horizontal"/></ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate><views:VideoCard/></DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </ScrollViewer>
</StackPanel>
```

2. In the **Pick-a-tag results** rail, change the `ItemTemplate` from the old section-card template to
   `<DataTemplate><views:CreatorCard Margin="0,0,12,0"/></DataTemplate>` (keep its `ItemsSource="{Binding TagResults}"`
   and `HasTagResults` visibility binding).

> **STOP if** `DiscoveryView.xaml`'s rail structure differs from the above (e.g. rails are a custom
> `ItemsControl` style rather than `ScrollViewer`+horizontal `StackPanel`). Mirror the file's **own**
> existing Continue-watching rail markup for the two new rails rather than copying this verbatim, and
> report the difference.

**4c. Update `DiscoveryViewModel` tests** — `tests\VideoShelf.App.Tests\...\DiscoveryViewModelTests.cs`
(find it). The ctor now needs a `CreatorCardFactory`. Construct one from the test's repos
(`new CreatorCardFactory(creatorArtRepo, nullThumbnails)` — reuse the test's existing
`CreatorArtRepository` + null/fake `IThumbnailService`; mirror how `CreatorsViewModel` is built in
`MainViewModelTestFactory`). Replace assertions referencing `ForYou`/`HasForYou` with
`RecommendedCreators`/`HasRecommendedCreators` (+ `RecommendedVideos`). Add a test that after seeding
watch history + tags + an unwatched recommended section, `RecommendedCreators` contains a
`CreatorCardViewModel` and `RecommendedVideos` is non-empty; and that toggling a tag fills `TagResults`
with `CreatorCardViewModel` items.

> **STOP if** no `DiscoveryViewModelTests` exists, or its DB seeding can't produce a recommended section
> — report so I can specify the arrange.

---

## Task 5 — App: `SearchViewModel` + tests

**New file** `src\VideoShelf.App\ViewModels\SearchViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SearchViewModel : ObservableObject
{
    private const int ResultLimit = 48;
    private readonly LibraryRepository _library;
    private readonly CreatorCardFactory _cards;

    private CancellationTokenSource? _opCts;
    private Task _pending = Task.CompletedTask;
    private bool _searching;

    public SearchViewModel(LibraryRepository library, CreatorCardFactory cards)
    {
        _library = library;
        _cards = cards;
    }

    public ObservableCollection<CreatorCardViewModel> CreatorResults { get; } = [];
    public ObservableCollection<RecencyCardViewModel> VideoResults { get; } = [];

    [ObservableProperty] private string _query = "";

    public bool HasCreatorResults => CreatorResults.Count > 0;
    public bool HasVideoResults => VideoResults.Count > 0;
    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);
    public bool NoResults => HasQuery && !_searching && !HasCreatorResults && !HasVideoResults;

    public event EventHandler<EpisodeView>? PlayRequested;
    public event Action<long>? OpenCreatorRequested;

    partial void OnQueryChanged(string value)
    {
        _opCts?.Cancel();
        var cts = _opCts = new CancellationTokenSource();
        _pending = RunSearchAsync(value, cts.Token);
    }

    private async Task RunSearchAsync(string query, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                _searching = false;
                CreatorResults.Clear();
                VideoResults.Clear();
                RaiseFlags();
                return;
            }

            _searching = true;
            RaiseFlags();
            await Task.Delay(150, ct);   // debounce keystrokes
            var (creators, videos) = await Task.Run(() =>
                (_library.SearchCreators(query, ResultLimit),
                 _library.SearchVideos(query, ResultLimit)), ct);
            ct.ThrowIfCancellationRequested();

            CreatorResults.Clear();
            foreach (var c in creators) CreatorResults.Add(MakeCreatorCard(c));
            VideoResults.Clear();
            foreach (var v in videos) VideoResults.Add(MakeVideoCard(v));

            _searching = false;
            RaiseFlags();
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer keystroke — swallow (no unobserved fault)
        }
    }

    private CreatorCardViewModel MakeCreatorCard(SectionSummary s)
    {
        var card = _cards.Create(s);
        card.OpenRequested += id => OpenCreatorRequested?.Invoke(id);
        return card;
    }

    private RecencyCardViewModel MakeVideoCard(RecencyItem i)
    {
        var card = new RecencyCardViewModel(i);
        card.PlayInvoked += (_, _) => RaisePlay(i.SeriesId, i.VideoId);
        return card;
    }

    private void RaisePlay(long seriesId, long videoId)
    {
        var ep = _library.GetEpisodes(seriesId).FirstOrDefault(e => e.VideoId == videoId);
        if (ep is not null) PlayRequested?.Invoke(this, ep);
    }

    private void RaiseFlags()
    {
        OnPropertyChanged(nameof(HasCreatorResults));
        OnPropertyChanged(nameof(HasVideoResults));
        OnPropertyChanged(nameof(HasQuery));
        OnPropertyChanged(nameof(NoResults));
    }

    /// Test hook: await the in-flight search.
    public Task WaitForIdleAsync() => _pending;
}
```

**DI** — register in `AddVideoShelf` (singleton, like the other VMs):

```csharp
services.AddSingleton<SearchViewModel>();
```

**Tests** — new file `tests\VideoShelf.App.Tests\...\SearchViewModelTests.cs`. Build the VM over a
`TempDb`-backed `LibraryRepository` + a `CreatorCardFactory(creatorArtRepo, nullThumbnails)` (mirror the
construction used elsewhere). Seed one creator/section named e.g. "NatGeo" with a couple of videos. Cover:

- Setting `Query = "nat"` then `await vm.WaitForIdleAsync()` populates `CreatorResults` (≥1) and
  `HasCreatorResults` is true.
- A query matching a video filename/series populates `VideoResults`.
- Clearing `Query = ""` empties both collections and `HasQuery`/`NoResults` are false.
- A video card's `Play` raises `PlayRequested` with the correct `EpisodeView`; a creator card's `Open`
  raises `OpenCreatorRequested` with the section id.

---

## Task 6 — App: Search view + shell wiring + harness

**6a. New view** `src\VideoShelf.App\Views\SearchView.xaml` (+ `SearchView.xaml.cs` with the standard
`InitializeComponent()`). **Copy `DiscoveryView.xaml`'s `<UserControl.Resources>` header verbatim** (it
already merges `DesignTokens.xaml` and declares the `BoolToVisibility` converter + card-gap tokens this
view needs) and the `xmlns`/`xmlns:views` declarations. Body:

```xml
<ScrollViewer VerticalScrollBarVisibility="Auto">
    <StackPanel Margin="16">

        <TextBlock Text="Creators" FontSize="18" FontWeight="SemiBold" Margin="0,0,0,8"
                   Visibility="{Binding HasCreatorResults, Converter={StaticResource BoolToVisibility}}"/>
        <ItemsControl ItemsSource="{Binding CreatorResults}"
                      Visibility="{Binding HasCreatorResults, Converter={StaticResource BoolToVisibility}}">
            <ItemsControl.ItemsPanel><ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate></ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate><views:CreatorCard Margin="0,0,12,12"/></DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <TextBlock Text="Videos" FontSize="18" FontWeight="SemiBold" Margin="0,16,0,8"
                   Visibility="{Binding HasVideoResults, Converter={StaticResource BoolToVisibility}}"/>
        <ItemsControl ItemsSource="{Binding VideoResults}"
                      Visibility="{Binding HasVideoResults, Converter={StaticResource BoolToVisibility}}">
            <ItemsControl.ItemsPanel><ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate></ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate><views:VideoCard Margin="0,0,12,12"/></DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <TextBlock Text="No matches." Opacity="0.7" Margin="0,8"
                   Visibility="{Binding NoResults, Converter={StaticResource BoolToVisibility}}"/>
    </StackPanel>
</ScrollViewer>
```

> **STOP if** `DiscoveryView.xaml` does **not** declare `BoolToVisibility` in its resources (i.e. the
> converter is sourced differently) — match whatever DiscoveryView does so SearchView resolves the same way.

**6b. `MainViewModel`** — wire Search in:

1. Add `AppView.Search` to the enum: `public enum AppView { Home, Browse, SectionDetail, RenameTool, Search }`.
2. Add `SearchViewModel search` as the **last** constructor parameter (keep existing param order).
3. Expose `public SearchViewModel Search { get; }` and assign it in the ctor next to `Creators = creators;`.
4. In the ctor event-wiring block, add:

```csharp
Search = search;
Search.PlayRequested += (_, e) => PlayEpisode(e);
Search.OpenCreatorRequested += async id => await OpenSectionAsync(id);
Search.PropertyChanged += (_, e) =>
{
    // Typing in the persistent search box drives the user into the Search view.
    if (e.PropertyName == nameof(SearchViewModel.Query) && !string.IsNullOrEmpty(Search.Query))
        CurrentView = AppView.Search;
};
```

**6c. `MainWindow.xaml`** — two edits:

1. In the top nav `StackPanel` (the one with the Home/Browse `ui:Button`s), add a search box after the
   Browse button:

```xml
<ui:TextBox PlaceholderText="Search creators and videos…" Width="280" Margin="24,0,0,0"
            Text="{Binding Search.Query, UpdateSourceTrigger=PropertyChanged}" />
```

> If `ui:TextBox` lacks `PlaceholderText` in this WPF-UI version, use `ui:AutoSuggestBox` or a plain
> `TextBox` — match whatever the codebase already uses for text entry. STOP and report if unsure.

2. In the nav-gated content `Grid` (the one hosting `DiscoveryView`/Browse/`SectionDetailView`/`RenameToolView`),
   add a `SearchView` host gated on `AppView.Search`:

```xml
<views:SearchView DataContext="{Binding Search}"
                  Visibility="{Binding DataContext.CurrentView,
                      RelativeSource={RelativeSource AncestorType=Window},
                      Converter={StaticResource EnumToVis}, ConverterParameter=Search}" />
```

**6d. `MainViewModelTestFactory`** (`tests\VideoShelf.App.Tests\TestSupport\MainViewModelTestFactory.cs`)
— construct a `SearchViewModel` (over the same repos/fakes it already builds) and a `CreatorCardFactory`,
and pass `SearchViewModel` as the new final `MainViewModel` ctor arg. Update any other direct
`new MainViewModel(...)` construction sites in App.Tests for the added param (M5/M7 had the same fan-out —
expect 1–3 sites). Add a `DiscoveryViewModel` `CreatorCardFactory` arg wherever the factory builds it.

**6e. Harness** — so the visual sweep can capture Search. In the harness view switch
(`HarnessRunner` / `HarnessOptions`, see `src\VideoShelf.App\...Harness*`), add a `Search` case: set
`main.Search.Query = <a term matching a seeded creator>` (after `--seed-demo`, reuse the first creator's
name from `LibraryRepository.GetSectionSummaries()`), then `await main.Search.WaitForIdleAsync()` and
settle. Add `"Search"` to the `--view` allowed values. Mirror the existing per-view handling exactly.

> **STOP if** the harness view-dispatch structure differs from M6's description (`HarnessRunner` drives
> `MainViewModel` to the requested view then writes the done-signal) — report so I can adjust.

**6f. MainViewModel nav test** — add to the existing `MainViewModel` tests: setting
`main.Search.Query = "x"` flips `main.CurrentView` to `AppView.Search`; `Search.PlayRequested` routes to
the player; `Search.OpenCreatorRequested` opens the section.

---

## Task 7 — Build, test gate, and verification

Run from the worktree root:

```powershell
dotnet build VideoShelf.slnx -c Release -v minimal
dotnet test  VideoShelf.slnx -c Release --nologo -v q
```

**Expected:** build succeeds; **all tests pass, 0 failures**, with a count higher than the M7 baseline of
222 (Core gains ~4–6 from Tasks 1c/2; App gains ~5–8 from Tasks 4c/5/6). If any test fails, fix the cause —
do not weaken assertions. Apply `systematic-debugging` if a failure is non-obvious.

---

## Task 8 — Visual sweep (Home + Search)

Use the M6 screenshot harness (`Run-VisualSweep.ps1` / the `--folder --autostart --view --done-signal`
hooks, with `--seed-demo` so rails/results render). Capture **Home** and the **new Search** view. Per the
project's standing rule, **a Sonnet subagent views the PNGs and returns a TEXT verdict** (PASS/FAIL +
observations + the absolute PNG paths) — **do not load images into the controller context.**

Acceptance criteria for the subagent to check:

- **Home:** rails render top-to-bottom — Continue-watching (video cards), **Recommended creators**
  (creator cards: thumbnail + name + "N videos"), **Recommended videos** (video cards), Recently-added,
  Recently-watched, and a Pick-a-tag chip row. No stacked/overlapping hosts. The seeded continue-watching
  item appears (the M7 seed targets the richest series). Cards are not clipped; the left sidebar
  ("CREATORS" + sources) is present by design — judge only the main content area.
- **Search:** with a seeded query, two grouped sections render — "Creators" (creator cards) above
  "Videos" (video cards). The top search box is visible and populated. No overlap with other hosts.

If a FAIL surfaces a real defect, fix it (additive-only per the theming rule) and re-sweep. Only surface an
actual PNG to the user if they explicitly ask to see one.

> Harness gotchas (from M6): wait for `IsWindowVisible`; settle ~5s for the Mica backdrop; a
> `TOPMOST→NOTOPMOST` bring-to-front toggle; a composited/unlocked desktop is required (a locked/remote
> session captures black). Run `Generate-Fixtures.ps1 -Force` if fixtures look stale.

---

## Task 9 — Ship

1. Commit on the feature branch (author `yovanmc` + the Opus co-author trailer; no Codex trailer).
2. **Flip the ROADMAP.md M8 row** to `✅ Merged` (PR #) with a one-line summary, and append an
   **M8 shipped** entry to the decision log capturing: the two-sub-rail For-you, Pick-a-tag→creator-card
   migration, `CreatorCardFactory`, the `SearchCreators`/`SearchVideos`/`GetRecommendedVideos` queries,
   `AppView.Search` + the top search box, final test count, and any STOP-and-report deviations. This flip
   rides on the M8 branch (direct pushes to `main` are blocked).
3. Push; open a PR; `& "C:\Program Files\GitHub CLI\gh.exe" pr checks <PR#> --watch` (sleep ~20s first);
   merge `--merge --delete-branch` from the **main repo root**; sync `main`.
4. Run `requesting-code-review` on the whole branch before merge; address findings with
   `receiving-code-review` rigor.
5. Ping me the Phase-B→next-plan handoff (M9 — Creator page).

## Acceptance checklist

- [ ] Core: `GetRecommendedVideos` + `ScoreSections` refactor + tests (Task 1)
- [ ] Core: `SearchCreators` + `SearchVideos` + tests (Task 2)
- [ ] App: `CreatorCardFactory` + DI (Task 3)
- [ ] App: Home rails reworked — 2 sub-rails + Pick-a-tag creator cards; `DiscoveryView.xaml` updated; tests green (Task 4)
- [ ] App: `SearchViewModel` + tests (Task 5)
- [ ] App: `SearchView.xaml` + shell wiring (`AppView.Search`, top search box, MainViewModel, DI, test factory, harness) (Task 6)
- [ ] Build clean + full test gate green, count > 222 (Task 7)
- [ ] Visual sweep PASS for Home + Search via subagent text verdict (Task 8)
- [ ] ROADMAP flipped, PR merged, CI green, handoff pinged (Task 9)
