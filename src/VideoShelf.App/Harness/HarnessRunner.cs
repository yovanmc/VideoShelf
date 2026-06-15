using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VideoShelf.App.Scale;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Views;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Harness;

/// <summary>
/// Drives the running app into a deterministic, screenshot-ready state for the
/// visual harness, then writes the done-signal file. UI-thread only.
/// Test-only: gated behind HarnessOptions.IsHarness in App.OnStartup.
/// </summary>
/// <remarks>
/// I1 note: MainViewModel's BulkBar/CommandPalette/MultiRename are nullable-trailing-param
/// (null in slim test contexts, real instances in production DI and the harness).
/// The production DI chain in ServiceCollectionExtensions.cs threads all three real
/// instances; no consolidation is needed here — the pattern is correct as-is.
/// </remarks>
public sealed class HarnessRunner
{
    private readonly MainViewModel _main;
    private readonly HarnessOptions _options;
    private readonly LibraryRepository _library;
    private readonly WatchRepository _watch;
    private readonly TagRepository _tags;
    private readonly CurationRepository _curation;
    private readonly SmartViewRepository _smartViews;
    private readonly PlaylistRepository _playlists;
    private readonly MaintenanceRepository _maintenance;

    public HarnessRunner(
        MainViewModel main,
        HarnessOptions options,
        LibraryRepository library,
        WatchRepository watch,
        TagRepository tags,
        CurationRepository curation,
        SmartViewRepository smartViews,
        PlaylistRepository playlists,
        MaintenanceRepository maintenance)
    {
        _main = main;
        _options = options;
        _library = library;
        _watch = watch;
        _tags = tags;
        _curation = curation;
        _smartViews = smartViews;
        _playlists = playlists;
        _maintenance = maintenance;
    }

    /// <summary>
    /// Optional action executed AFTER the main SettleAsync (post-settle).
    /// Set by NavigateAsync for player sub-states that need the PlayerView to be
    /// fully loaded before opening flyouts or showing feedback badges.
    /// </summary>
    private Action? _postSettleAction;

    public async Task RunAsync()
    {
        try
        {
            // Stress-library seeding: DB-only rows (no disk files), seeded before any scan/load
            // so GetSectionSummaries() sees the stress data on the first view load.
            // After seeding, reload the library VM so the Browse/Home grids reflect the stress data.
            if (_options.StressSpec is not null)
            {
                var (creators, biggest, total) = _options.ParseStressSpec();
                var plan = StressLibrarySpec.Generate(creators, biggest, total, seed: 20260614);
                var dataDir = _options.DataDir ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoShelf");
                var stressRoot = Path.Combine(dataDir, "stress");
                new StressLibrarySeeder(_library).Seed(plan, sourceRoot: stressRoot);
                // Trigger the library reload command so the MainViewModel/Creators VM refresh.
                await ScanAndReloadAsync();
            }

            if (_options.Folder is not null)
                await AddSourceAsync(_options.Folder);
            if (_options.AutoStart || _options.Folder is not null)
                await ScanAndReloadAsync();

            if (_options.SeedDemo)
            {
                await SeedDemoAsync();
                await ScanAndReloadAsync();
            }

            // M19 player sub-states always need the full video settle (player in tree).
            var isPlayerState = _options.View is
                "Player" or "PiP" or "PlayerQueue" or
                "PlayerMore" or "PlayerTracks" or "PlayerVolume" or
                "PlayerSpeed" or "PlayerAspect" or "PlayerAbRepeat" or
                "PlayerSkipFeedback" or "PlayerUpNext";

            await NavigateAsync(_options.View);
            await SettleAsync(isVideo: isPlayerState);

            // Run any post-settle action (e.g. open a flyout after the PlayerView is loaded).
            if (_postSettleAction is { } postAction)
            {
                _postSettleAction = null;
                await Application.Current.Dispatcher.InvokeAsync(postAction, DispatcherPriority.Render);
                // Short extra settle so the flyout/badge is visible before capture.
                await Task.Delay(300);
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            }

            // Metrics capture: write after the view is settled and rendered.
            if (_options.MetricsOut is { } metricsPath)
                await WriteMetricsAsync(metricsPath, _options.View ?? "");

            WriteDoneSignal($"OK view={_options.View}");
        }
        catch (Exception ex)
        {
            WriteDoneSignal("ERROR: " + ex.Message);
        }
    }

    private async Task NavigateAsync(string view)
    {
        switch (view)
        {
            case "Home": _main.CurrentView = AppView.Home; break;
            case "Browse": _main.CurrentView = AppView.Browse; break;
            case "Settings": ShowSettings(); break;
            case "SectionDetail":
                await _main.OpenSectionAsync((await FindRichestSeriesAsync()).SectionId);
                var expandable = _main.SectionDetail.SeriesList.FirstOrDefault(s => !s.IsStandalone);
                if (expandable is not null) await expandable.ActivateCommand.ExecuteAsync(null);
                break;

            // ── M18-H: creator page in Edit mode (shows split/merge/reorder affordances) ──
            case "SectionEditMode":
                await _main.OpenSectionAsync((await FindRichestSeriesAsync()).SectionId);
                // Enter edit mode so the grouping-override affordances are visible.
                _main.SectionDetail.IsEditing = true;
                // Expand the first non-standalone series so episode rows are visible.
                var editExpandable = _main.SectionDetail.SeriesList.FirstOrDefault(s => !s.IsStandalone);
                if (editExpandable is not null) await editExpandable.ActivateCommand.ExecuteAsync(null);
                break;
            case "RenameTool":
                await _main.OpenRenameToolAsync((await FindRichestSeriesAsync()).Series); break;
            case "Player": await PlayAsync(_options.Play!, pip: false); break;
            case "PiP": await PlayAsync(_options.Play!, pip: true); break;
            case "Search": await NavigateSearchAsync(); break;
            case "Queue": await ShowQueueAsync(); break;
            case "PlayerQueue": await PlayWithQueueDrawerAsync(); break;

            // ── M19 player sub-states ─────────────────────────────────────────────
            // Each starts playback then sets a post-settle action to put the player
            // into the target state once the PlayerView is fully in the visual tree.

            // "⋯ More" overflow flyout open (speed row, aspect row, A-B row, set-cover, screenshot).
            case "PlayerMore":
                await PlayAsync(_options.Play!, pip: false);
                _postSettleAction = () => OpenPlayerFlyout("More");
                break;

            // Tracks flyout open (audio list, subtitle list, "+ Sub", normalize toggle).
            case "PlayerTracks":
                await PlayAsync(_options.Play!, pip: false);
                _postSettleAction = () => OpenPlayerFlyout("Tracks");
                break;

            // Volume flyout open (volume slider + mute button).
            case "PlayerVolume":
                await PlayAsync(_options.Play!, pip: false);
                _postSettleAction = () => OpenPlayerFlyout("Volume");
                break;

            // Speed set to 1.5× so RateLabel shows "1.5×".
            case "PlayerSpeed":
                await PlayAsync(_options.Play!, pip: false);
                _postSettleAction = () => _main.Player.SetPlaybackRateCommand.Execute(1.5);
                break;

            // Aspect cycled to 16:9 (second preset — Default→16:9→4:3→Fill).
            case "PlayerAspect":
                await PlayAsync(_options.Play!, pip: false);
                _postSettleAction = () => _main.Player.CycleAspectCommand.Execute(null);
                break;

            // A-B repeat active: set A at ~3 s, B at ~8 s so the bar chip lights up.
            case "PlayerAbRepeat":
                await PlayAsync(_options.Play!, pip: false);
                _postSettleAction = () =>
                {
                    _main.Player.RepeatStartSeconds = 3.0;
                    _main.Player.RepeatEndSeconds   = 8.0;
                };
                break;

            // Skip-feedback badge visible: "−10s" badge shown after SkipBack10.
            case "PlayerSkipFeedback":
                await PlayAsync(_options.Play!, pip: false);
                _postSettleAction = () => _main.Player.SkipBack10Command.Execute(null);
                break;

            // Up-Next countdown card visible: seed a 2-item queue, then call ShowUpNext directly
            // with the second episode so the card renders with a title and 10-second countdown.
            case "PlayerUpNext":
                await NavigatePlayerUpNextAsync();
                break;
            case "SmartViews": _main.ShowSmartViewsCommand.Execute(null); break;
            case "Playlists":  _main.ShowPlaylistsCommand.Execute(null); break;
            case "Watchlist":  _main.ShowWatchlistCommand.Execute(null); break;
            case "Favorites":  _main.ShowFavoritesCommand.Execute(null); break;
            case "History":    _main.ShowHistoryCommand.Execute(null); break;

            // ── C3: skeleton loading state ────────────────────────────────────────
            // Shows Favorites with IsLoading=true (skeleton placeholder visible) so the
            // sweep can confirm the overlay renders. IsLoading is NOT cleared — the screen
            // stays frozen in the loading state for the screenshot sweep.
            case "FavoritesLoading":
                _main.ShowFavoritesCommand.Execute(null);
                _postSettleAction = () => _main.Favorites.IsLoading = true;
                break;

            // ── M18 surfaces ──────────────────────────────────────────────────────

            // Maintenance / Library Health dashboard.
            case "Maintenance":
                _main.ShowMaintenanceCommand.Execute(null);
                break;

            // Duplicate compare/resolve screen (M18-G).
            // Navigates to the DuplicateResolve view type; relies on seed data for real content.
            // Falls back to the Maintenance view if no duplicate group exists in the seeded DB.
            case "DuplicateResolve":
                NavigateDuplicateResolve();
                break;

            // ── M17 surfaces (I2) ────────────────────────────────────────────────

            // Browse with 2 creators pre-selected so the BulkActionBar is visible.
            case "BrowseSelection": await NavigateBrowseSelectionAsync(); break;

            // Command palette open with a pre-filled query ("home").
            case "CommandPalette": NavigateCommandPalette(); break;

            // Browse with the in-page filter bar visible + Compact density + List mode.
            case "BrowseFilter": NavigateBrowseFilter(); break;

            // MultiRename preview page seeded with the richest creator's series.
            case "MultiRename": await NavigateMultiRenameAsync(); break;

            // ── M21 B4 toast state ────────────────────────────────────────────
            // Shows the Home page with a toast in the bottom-right corner so the
            // visual sweep can confirm the overlay renders correctly.
            case "Toast":
                _main.CurrentView = AppView.Home;
                _postSettleAction = () => _main.Toasts.Show("Added to favorites", undo: () => { });
                break;

            default: _main.CurrentView = AppView.Home; break;
        }
    }

    // ── M18-G navigation helper ───────────────────────────────────────────────

    /// <summary>
    /// Navigates to the DuplicateResolve view for the first available duplicate group.
    /// Falls back to the Maintenance page if no groups exist (e.g. no data seeded).
    /// </summary>
    private void NavigateDuplicateResolve()
    {
        // Try to find a section with duplicates; use the library-wide list first.
        var groups = _maintenance.GetDuplicateGroups();
        if (groups.Count > 0)
        {
            // Use the first group's section to open SectionDetail, which then fires ResolveRequested.
            var firstGroup = groups[0];
            var firstSectionId = firstGroup.Videos[0].SectionId;
            // Wire a one-time handler to intercept the ResolveRequested event.
            EventHandler<DuplicateResolveViewModel>? handler = null;
            handler = (_, resolveVm) =>
            {
                _main.SectionDetail.ResolveRequested -= handler;
            };
            _main.SectionDetail.ResolveRequested += handler;
            // Navigate to the section so PossibleDuplicates is loaded.
            // Then manually open the first group's resolve screen.
            _main.SectionDetail.OpenResolveCommand.Execute(firstGroup);
            // After the command fires, CurrentView should be DuplicateResolve.
            return;
        }
        // Fallback: no duplicates seeded, show Maintenance instead.
        _main.ShowMaintenanceCommand.Execute(null);
    }

    // ── M17 navigation helpers ────────────────────────────────────────────────

    /// <summary>
    /// Navigates to Browse in selection mode with the first two creator cards pre-selected
    /// so the BulkActionBar shows "2 selected". Uses the Creators collection that was
    /// loaded during ScanAndReload / SeedDemo.
    /// </summary>
    private async Task NavigateBrowseSelectionAsync()
    {
        _main.CurrentView = AppView.Browse;
        await SettleAsync(isVideo: false);

        // Enter selection mode on the Creators VM.
        _main.Creators.Selection.EnterSelectionModeCommand.Execute(null);

        // Pre-select the first two cards (if available) so the bulk bar is visible.
        var cards = _main.Creators.Creators;
        for (var i = 0; i < Math.Min(2, cards.Count); i++)
            cards[i].IsSelected = true;

        // Give the BulkBar binding a render cycle to reflect the selection count.
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    /// <summary>
    /// Opens the Ctrl+K command palette with a pre-filled query so the result list
    /// is populated for the screenshot. Uses "home" which matches the "Home" action.
    /// </summary>
    private void NavigateCommandPalette()
    {
        _main.CurrentView = AppView.Browse; // show content behind the overlay
        _main.OpenCommandPaletteCommand.Execute(null);
        // Set a query after opening so the palette's RunAsync fires and populates Results.
        if (_main.CommandPalette is not null)
            _main.CommandPalette.Query = "home";
    }

    /// <summary>
    /// Opens Browse with the in-page filter bar visible, Compact density, and List view mode
    /// so the sweep can capture the filter toolbar affordance.
    /// </summary>
    private void NavigateBrowseFilter()
    {
        _main.CurrentView = AppView.Browse;
        if (!_main.Creators.IsFilterVisible)
            _main.Creators.ToggleFilterCommand.Execute(null);
        _main.Creators.SetDensityCompactCommand.Execute(null);
        _main.Creators.SetViewModeListCommand.Execute(null);
    }

    /// <summary>
    /// Opens the MultiRename page seeded with the series ids from the richest creator
    /// (the one with the most series, seeded in SeedDemoAsync).
    /// Falls back to navigating to the Browse page if MultiRename is unavailable.
    /// </summary>
    private async Task NavigateMultiRenameAsync()
    {
        if (_main.MultiRename is null)
        {
            _main.CurrentView = AppView.Browse;
            return;
        }

        // Find the section with the most series — that's the ≥40-series demo creator.
        var allSections = _library.GetSectionSummaries();
        long bestSectionId = 0;
        int bestSeriesCount = 0;
        foreach (var s in allSections)
        {
            var series = _library.GetSeriesForSection(s.SectionId);
            if (series.Count > bestSeriesCount)
            {
                bestSeriesCount = series.Count;
                bestSectionId = s.SectionId;
            }
        }

        if (bestSectionId == 0 || bestSeriesCount == 0)
        {
            _main.CurrentView = AppView.Browse;
            return;
        }

        var seriesIds = _library.GetSeriesForSection(bestSectionId)
                                .Select(s => s.Id)
                                .Take(5)   // limit to 5 for a readable preview screenshot
                                .ToList();

        await _main.OpenMultiRenameAsync(seriesIds);
    }

    /// <summary>
    /// Seeds the Search view with a term matching the first scanned creator so the
    /// Creators section is populated for the screenshot sweep.
    /// </summary>
    private async Task NavigateSearchAsync()
    {
        var summaries = _library.GetSectionSummaries();
        var term = summaries.Count > 0 ? summaries[0].DisplayName : "video";
        _main.Search.Query = term;
        await _main.Search.WaitForIdleAsync();
    }

    private async Task SettleAsync(bool isVideo)
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await Task.Delay(isVideo ? 2500 : 700);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    private void WriteDoneSignal(string message)
    {
        if (_options.DoneSignal is null) return;
        try { File.WriteAllText(_options.DoneSignal, message + Environment.NewLine); }
        catch { }

        // Exit the process after writing the done-signal on the metrics/bench path so that
        // Run-ScaleBench.ps1 (which waits via `| Out-Null`) returns on its own without
        // needing an external kill. Guarded to MetricsOut-present runs only:
        //   - Scale bench: --metrics-out <path> --done-signal <path>  → exit
        //   - Visual sweep: --done-signal <path> only                 → do NOT exit (sweep
        //     captures the window after the done-signal, THEN kills the process; early exit
        //     would close the window before capture).
        // InvokeShutdown posts a WM_QUIT to the Dispatcher message loop and is safe to call
        // from any thread. Environment.Exit(0) is the last resort if libVLC or another
        // non-background native thread prevents the WPF dispatcher from shutting down cleanly.
        if (_options.MetricsOut is not null)
        {
            try
            {
                Application.Current.Dispatcher.InvokeShutdown();
            }
            catch
            {
                Environment.Exit(0);
            }
        }
    }

    // ── M19 player-state helpers ──────────────────────────────────────────────

    /// <summary>
    /// Opens a named Popup flyout on the live <see cref="PlayerView"/> via the
    /// <see cref="PlayerView.OpenFlyoutForHarness"/> harness hook.
    /// No-op when the <see cref="MainWindow"/> or <see cref="PlayerView"/> are not available
    /// (belt-and-suspenders safety; the settle should always ensure both are live).
    /// </summary>
    private static void OpenPlayerFlyout(string which)
    {
        if (Application.Current.MainWindow is MainWindow win)
            win.GetPlayerView()?.OpenFlyoutForHarness(which);
    }

    /// <summary>
    /// Seeds a 2-item play queue and shows the Up-Next card for the second episode
    /// without waiting for natural end-of-video. This lets the sweep capture the
    /// countdown card with a real title and the 10-second initial count.
    ///
    /// Seed ordering: the synthetic queue items are added AFTER ScanAndReload completed
    /// (SeedDemoAsync is called before NavigateAsync), so no re-scan can mark them missing.
    /// </summary>
    private async Task NavigatePlayerUpNextAsync()
    {
        // Start playing the richest available episode (same as PlayAsync).
        var (sectionId, series) = await FindRichestSeriesAsync();
        var episodes = _library.GetEpisodes(series.SeriesId);
        var first    = episodes.FirstOrDefault()
            ?? throw new InvalidOperationException("Richest series has no episodes to play.");

        _main.Player.AutoHideSuppressed = true;
        _main.PlayEpisode(first);

        // Find a "next" episode for the card: prefer the second episode in the same series;
        // fall back to the first episode of any other series in the same section.
        var next = episodes.Count > 1
            ? episodes[1]
            : _library.GetEpisodesForSection(sectionId)
                      .FirstOrDefault(e => e.VideoId != first.VideoId);

        if (next is not null)
        {
            // Schedule ShowUpNext as a post-settle action so the PlayerView is live
            // and the card's Visibility binding fires on a rendered overlay.
            _postSettleAction = () =>
                _main.UpNext.ShowUpNext(next, () => { /* harness: don't actually play next */ });
        }
        else
        {
            // No second episode found — the Up-Next card would not render, making an
            // OK done-signal a silent false positive. Fail loud so the sweep rejects it.
            throw new InvalidOperationException(
                "PlayerUpNext harness state requires a seeded next episode but none was found. " +
                "Seed a multi-episode series or a section with at least two videos.");
        }
    }

    // ---- Metrics capture ----

    /// <summary>
    /// Captures render-scale metrics for the current view and writes them as JSON.
    /// Counts realized containers in the on-screen ListBox (Browse → CreatorsGridListBox;
    /// SectionDetail → SeriesGridListBox) via ItemContainerGenerator.
    /// Falls back to counting 0 if the target ListBox cannot be found in the visual tree
    /// (acceptable: the bench script will show 0 and the gate won't block the non-WPF path).
    /// </summary>
    private async Task WriteMetricsAsync(string metricsPath, string viewName)
    {
        var sw = Stopwatch.StartNew();

        // Extra dispatcher flush to ensure the virtualized list has realized its initial containers.
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await Task.Delay(200);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);

        sw.Stop();

        int nodes = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Try to find the target ListBox by name from the MainWindow's named element scope.
            var win = Application.Current.MainWindow;
            if (win is null) return 0;

            // For Browse → count CreatorsGridListBox realized containers.
            // For SectionDetail → count SeriesGridListBox realized containers.
            var listBoxName = viewName switch
            {
                "SectionDetail" => "SeriesGridListBox",
                _ => "CreatorsGridListBox",
            };

            // Walk the visual tree to find the named ListBox (FindName only finds within the
            // same namescope; for UserControl-hosted elements, walk descendants).
            var lb = FindDescendantByName<ListBox>(win, listBoxName);
            if (lb is null) return 0;

            // Count realized containers via ItemContainerGenerator.
            var gen = lb.ItemContainerGenerator;
            int count = 0;
            for (int i = 0; i < lb.Items.Count; i++)
            {
                if (gen.ContainerFromIndex(i) is not null) count++;
            }
            return count;
        }, DispatcherPriority.Render);

        var metric = new ScaleMetrics
        {
            View = viewName,
            CreatorCount = _library.GetSectionSummaries().Count,
            RenderedNodeCount = nodes,
            InitialRenderMs = sw.ElapsedMilliseconds,
            ManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: true),
        };

        File.WriteAllText(metricsPath, ScaleMetrics.ToJson(new[] { metric }));
    }

    /// <summary>
    /// Walks the visual tree from <paramref name="root"/> to find the first descendant
    /// of type <typeparamref name="T"/> with <see cref="FrameworkElement.Name"/> == <paramref name="name"/>.
    /// Returns null if not found.
    /// </summary>
    private static T? FindDescendantByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T fe && fe.Name == name) return fe;
            var found = FindDescendantByName<T>(child, name);
            if (found is not null) return found;
        }
        return null;
    }

    // ---- Helpers wired to the real APIs ----

    /// <summary>
    /// Adds a folder source via LibraryRepository.UpsertSource (non-dialog path, same as
    /// SourcesViewModel.AddSource but without the IFolderPicker dialog).
    /// </summary>
    private Task AddSourceAsync(string folder)
    {
        var displayName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar))
            is { Length: > 0 } name ? name : folder;
        _library.UpsertSource(folder, displayName);
        _main.Sources.Load();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Triggers MainViewModel.ScanAndReloadCommand (AsyncRelayCommand generated by [RelayCommand]
    /// on the private async Task ScanAndReload() method).
    /// </summary>
    private Task ScanAndReloadAsync()
        => _main.ScanAndReloadCommand.ExecuteAsync(null);

    /// <summary>
    /// Seeds demo data for the visual sweep:
    /// <list type="bullet">
    ///   <item>Marks the richest real episode watched + sets resume position (M12 rails).</item>
    ///   <item>Seeds Favorites/Watchlist/SmartViews/Playlists/History so those pages render non-empty.</item>
    ///   <item><b>M17 additions (I2):</b> seeds ≥30 synthetic creators spanning letters A–Z and
    ///         one "Alphabet Cinema" creator with exactly 42 series so virtualization, the A–Z
    ///         jump-list, and collapse/expand-all are all exercisable from a single harness run.
    ///         All synthetic creators share the "demo" tag for affinity rails.</item>
    /// </list>
    /// All seed values are derived from deterministic indices (no Date.Now/random nondeterminism
    /// that could break other tests).
    /// </summary>
    private Task SeedDemoAsync()
    {
        var sources = _library.GetSources();
        if (sources.Count == 0) return Task.CompletedTask;

        foreach (var source in sources)
        {
            var sections = _library.GetSections(source.Id);
            if (sections.Count == 0) continue;

            // Tag EVERY section with "demo" so that, once a video in one section is marked
            // watched (below), the OTHER unwatched sections share that tag's affinity and
            // surface in the For-you / Recommended-creators / Recommended-videos rails.
            // (Tagging only one section left those rails empty — GetForYou needs a watched,
            // tagged section to build affinity AND a distinct unwatched section sharing it.)
            foreach (var section in sections) _tags.AddTag(section.Id, "demo");

            // Seed watched+resume on the richest series available (the most episodes),
            // not blindly sections[0]'s first series — that section may hold only
            // single-file standalones, leaving the continue-watching rail empty.
            var richest = sections
                .SelectMany(s => _library.GetSeriesForSection(s.Id))
                .Select(se => _library.GetEpisodes(se.Id))
                .Where(eps => eps.Count > 0)
                .OrderByDescending(eps => eps.Count)
                .FirstOrDefault();
            if (richest is null) continue;

            // Mark first episode watched
            _watch.SetWatched(richest[0].VideoId, true);

            // Set a partial resume position on the second episode if present (populates
            // the continue-watching rail with a visibly partial progress bar). Falls back
            // to the first video for a standalone so the rail is still exercised.
            var resumedId = richest.Count > 1 ? richest[1].VideoId : richest[0].VideoId;
            _library.SetResumePosition(resumedId, 12.0);

            // Seed a deterministic duration on the resumed video so the sweep can
            // verify M12 visibly: 12s of 60s → a ~20% progress bar.
            // (Fixtures carry no real duration, so the libVLC backfill can't exercise this
            // on its own; this keeps the visual check deterministic.)
            _library.SetDuration(resumedId, 60.0);

            // ── M16 organize pages: seed so SmartViews/Playlists/Watchlist/Favorites/History
            // all render non-empty in the screenshot sweep. Guard on having ≥1 video.

            // Favorite + watchlist
            _curation.SetFavorite(richest[0].VideoId, true);
            var watchlistId = richest.Count > 1 ? richest[1].VideoId : richest[0].VideoId;
            _curation.SetWatchlist(watchlistId, true, DateTimeOffset.UtcNow);

            // Video + series tags (cascade chips on creator/series pages)
            _tags.AddSeriesTag(richest[0].SeriesId, "demo-series");
            _tags.AddVideoTag(richest[0].VideoId, "demo-video");

            // Demo smart view (show on home) — uses dateAdded/withinDays so it matches any
            // recently-scanned video, making the SmartViews page + Home smart-shelf non-empty
            // regardless of whether the watched flag propagated before seeding.
            _smartViews.Create(
                "Demo · recent",
                new SmartViewDefinition("all", new[] { new SmartRule("dateAdded", "withinDays", "3650") }),
                showOnHome: true,
                DateTimeOffset.UtcNow);

            // Demo playlist with up to two items
            var plId = _playlists.Create("Demo playlist", DateTimeOffset.UtcNow);
            _playlists.AddItem(plId, richest[0].VideoId);
            if (richest.Count > 1) _playlists.AddItem(plId, richest[1].VideoId);

            break; // seed one source is sufficient for demo
        }

        // ── M17 (I2): synthetic creators for virtualization / A–Z / multi-rename sweeps ──
        //
        // Seed ≥30 creators spanning the alphabet so the A–Z jump-list shows lit letters
        // across all 26, and the Browse grid is tall enough to exercise virtualization.
        // Also seed one creator ("Alphabet Cinema") with exactly 42 series so the
        // collapse/expand-all control and multi-rename preview have meaningful content.
        //
        // All data is tied to a dedicated synthetic source so it never conflicts with the
        // real scanned source above (different source id, different root paths).
        SeedAlphabetCreators();

        // ── M18 (J): seed data for Maintenance + DuplicateResolve sweeps ─────────────
        //
        // Seeds: missing videos, orphan series, empty creator, duplicate pairs with
        // size_bytes + duration + resolution so the new surfaces render with CONTENT.
        SeedM18MaintenanceData();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Seeds a synthetic "demo-maintenance" source with data for the M18 Maintenance dashboard
    /// and DuplicateResolve compare screen sweep:
    /// <list type="bullet">
    ///   <item><b>Missing videos:</b> two rows with bogus paths + missing=1, so the
    ///         Maintenance MISSING FILES sub-section renders with content.</item>
    ///   <item><b>Orphan series:</b> a series whose only video is missing, so it appears
    ///         in the ORPHAN SERIES sub-section.</item>
    ///   <item><b>Empty creator:</b> a section with no videos at all, so it appears in
    ///         the EMPTY CREATORS sub-section.</item>
    ///   <item><b>Duplicate pair:</b> two videos with identical size_bytes, duration, and
    ///         resolution so <c>MaintenanceRepository.GetDuplicateGroups()</c> returns them
    ///         and the DuplicateResolve compare screen shows two candidates.</item>
    /// </list>
    /// All synthetic paths are non-existent on disk (begins with \DemoMaintenance\).
    /// Idempotent: UpsertSource/Section/Series/Video are ON CONFLICT DO UPDATE, and
    /// SetVideoMissing / SetSizeBytes / SetDuration / SetResolution are direct UPDATEs.
    /// </summary>
    private void SeedM18MaintenanceData()
    {
        var srcId = _library.UpsertSource(@"\DemoMaintenance", "DemoMaintenance");

        // ── 1. Missing videos: two videos marked missing=1 ──────────────────────
        //    Creator "Ghost Creator" / series "Lost Files" / two missing episodes.
        var ghostSectionId = _library.UpsertSection(srcId, "Ghost Creator");
        var lostSeriesId   = _library.UpsertSeries(ghostSectionId, "Lost Files", isStandalone: false);

        var missingVid1 = _library.UpsertVideo(lostSeriesId, @"\DemoMaintenance\GhostCreator\LostFiles\ghost_ep01.mp4", 1, ".mp4");
        _library.SetVideoMissing(missingVid1);

        var missingVid2 = _library.UpsertVideo(lostSeriesId, @"\DemoMaintenance\GhostCreator\LostFiles\ghost_ep02.mp4", 2, ".mp4");
        _library.SetVideoMissing(missingVid2);

        // ── 2. Orphan series: "Orphan Series" under Ghost Creator has only missing vids ──
        //    Same creator, different series — the series has no non-missing videos → orphan.
        var orphanSeriesId = _library.UpsertSeries(ghostSectionId, "Orphan Series", isStandalone: false);
        var orphanVid      = _library.UpsertVideo(orphanSeriesId, @"\DemoMaintenance\GhostCreator\OrphanSeries\orphan_ep01.mp4", 1, ".mp4");
        _library.SetVideoMissing(orphanVid);

        // ── 3. Empty creator: a section with NO videos at all ──────────────────
        //    UpsertSection creates the row; never add any videos under it.
        _library.UpsertSection(srcId, "Empty Creator");

        // ── 4. Duplicate pair: two videos with identical size_bytes + duration ──
        //    They must BOTH be missing=0 (present) for the duplicate query to pick them up.
        //    They also get resolution so the compare screen renders "1920×1080".
        //    Creator "Duplicate Studio" / two standalone series, one video each.
        var dupSectionId  = _library.UpsertSection(srcId, "Duplicate Studio");
        var dupSeries1    = _library.UpsertSeries(dupSectionId, "Movie Copy A", isStandalone: true);
        var dupSeries2    = _library.UpsertSeries(dupSectionId, "Movie Copy B", isStandalone: true);

        const long DupSizeBytes = 1_234_567_890L;   // ~1.15 GB — visible in compare screen
        const double DupDuration = 3600.0;           // 60 min — rounds to 3600 s

        // Insert present (missing=0) videos; supply size_bytes at upsert time.
        var dupVid1 = _library.UpsertVideo(dupSeries1, @"\DemoMaintenance\DuplicateStudio\movie_copy_a.mp4", 1, ".mp4", DupSizeBytes);
        var dupVid2 = _library.UpsertVideo(dupSeries2, @"\DemoMaintenance\DuplicateStudio\movie_copy_b.mp4", 1, ".mp4", DupSizeBytes);

        // Write duration + resolution for both (so the compare screen shows all three columns).
        _library.SetDuration(dupVid1, DupDuration);
        _library.SetDuration(dupVid2, DupDuration);
        _library.SetResolution(dupVid1, 1920, 1080);
        _library.SetResolution(dupVid2, 1920, 1080);
    }

    /// <summary>
    /// Seeds a synthetic "demo-alphabet" source containing:
    /// <list type="bullet">
    ///   <item>30 creators whose names start with every letter A–Z (some letters get 2 creators
    ///         so the total reaches 30 even with only 26 distinct letters).</item>
    ///   <item>One "Alphabet Cinema" creator with 42 series, each containing 2 episodes,
    ///         for a total of 84 videos — enough for the collapse/expand-all and multi-rename sweeps.</item>
    /// </list>
    /// All file paths are synthetic (\DemoAlphabet\…) and will never be probed/scanned.
    /// Idempotent: UpsertSource/Section/Series/Video are all ON CONFLICT DO UPDATE so
    /// re-running SeedDemo (e.g. after a ScanAndReload) is safe.
    /// </summary>
    private void SeedAlphabetCreators()
    {
        var srcId = _library.UpsertSource(@"\DemoAlphabet", "DemoAlphabet");

        // 30 creator names: A–Z (26) + 4 extras (Bella, Diana, Elena, Frank) to hit ≥30.
        // Names are deterministic strings derived from index — no Date.Now/random.
        string[] creatorNames =
        {
            "Alice A",     "Bella B",     "Carlos C",    "Diana D",
            "Elena E",     "Frank F",     "Grace G",     "Hector H",
            "Iris I",      "James J",     "Kira K",      "Leo L",
            "Maya M",      "Noel N",      "Olivia O",    "Pedro P",
            "Quinn Q",     "Rosa R",      "Sam S",       "Tara T",
            "Uma U",       "Victor V",    "Wendy W",     "Xander X",
            "Yuki Y",      "Zara Z",      "Ana Autumn",  "Bruno Bay",
            "Cleo Cross",  "Dani Dusk",
        };

        foreach (var (name, idx) in creatorNames.Select((n, i) => (n, i)))
        {
            var sectionId = _library.UpsertSection(srcId, name);
            _tags.AddTag(sectionId, "demo");

            // Give each creator 1 standalone video so they appear in summaries.
            var seriesId = _library.UpsertSeries(sectionId, name, isStandalone: true);
            var filePath = $@"\DemoAlphabet\{name}\video_{idx:D2}.mp4";
            _library.UpsertVideo(seriesId, filePath, 1, ".mp4");
        }

        // ── ≥40-series creator: "Alphabet Cinema" ────────────────────────────
        // 42 series, each with 2 episodes. SeriesId is stable across runs (UpsertSeries
        // is ON CONFLICT DO UPDATE by (section_id, base_title)).
        const int SeriesCount = 42;
        var cinemaSectionId = _library.UpsertSection(srcId, "Alphabet Cinema");
        _tags.AddTag(cinemaSectionId, "demo");

        for (var s = 1; s <= SeriesCount; s++)
        {
            var seriesTitle = $"Series {s:D2}";
            var seriesId = _library.UpsertSeries(cinemaSectionId, seriesTitle, isStandalone: false);
            for (var ep = 1; ep <= 2; ep++)
            {
                var filePath = $@"\DemoAlphabet\AlphabetCinema\{seriesTitle}\ep{ep:D2}.mp4";
                _library.UpsertVideo(seriesId, filePath, ep, ".mp4");
            }
        }
    }

    /// <summary>
    /// Finds the series with the most episodes across all sections (so the SectionDetail
    /// and RenameTool shots land on a meaningful multi-episode series rather than a
    /// single-file standalone). Loads each section's series list on demand. Throws if the
    /// scan found no media.
    /// </summary>
    private async Task<(long SectionId, SeriesViewModel Series)> FindRichestSeriesAsync()
    {
        (long SectionId, SeriesViewModel Series)? best = null;
        foreach (var section in _main.Library.Sections)
        {
            if (section.SeriesList.Count == 0)
                await section.LoadSeriesAsync(BrowseSort.Name, CancellationToken.None);

            foreach (var series in section.SeriesList)
            {
                if (best is null || series.EpisodeCount > best.Value.Series.EpisodeCount)
                    best = (section.SectionId, series);
            }
        }

        return best ?? throw new InvalidOperationException("No series found after scan.");
    }

    /// <summary>
    /// Shows the dedicated Settings view.
    /// </summary>
    private void ShowSettings()
        => _main.CurrentView = AppView.Settings;

    /// <summary>
    /// Plays a clip via MainViewModel.PlayEpisode(EpisodeView). Constructs a synthetic
    /// EpisodeView from the clip path (VideoId=0, SeriesId=0 — sufficient for engine.Load).
    /// If pip=true, sets IsPictureInPicture after playback starts (activates PiP mode in PlayerView).
    /// </summary>
    private async Task PlayAsync(string clip, bool pip)
    {
        // Play a REAL scanned episode (DB-backed VideoId + on-disk path) rather than a
        // synthetic EpisodeView: the player's missing-file guard rejects ids that aren't
        // in the library, so a fabricated episode renders the "File not found" banner.
        // The first episode of the richest series (e.g. Big Buck Bunny 1) is a known clip.
        var (_, series) = await FindRichestSeriesAsync();
        var episode = _library.GetEpisodes(series.SeriesId).FirstOrDefault()
            ?? throw new InvalidOperationException("Richest series has no episodes to play.");

        // Keep the auto-hiding controls up so the screenshot sweep captures the transport bar
        // instead of an auto-hidden (controls-faded) frame.
        _main.Player.AutoHideSuppressed = true;
        _main.PlayEpisode(episode);

        if (pip)
        {
            // Let the player initialise before toggling PiP so the MediaPlayer is live.
            await Task.Delay(600);
            _main.IsPictureInPicture = true;
            // Render the floating PiP panel over real content (proves in-window placement +
            // click-through) rather than over a black full-window player backdrop.
            _main.CurrentView = AppView.Home;
        }
    }

    /// <summary>
    /// Navigates to the Queue page with the richest section's episodes loaded into the queue,
    /// so the page shows a populated list with the first item highlighted as now-playing.
    /// </summary>
    private async Task ShowQueueAsync()
    {
        var (sectionId, _) = await FindRichestSeriesAsync();
        var episodes = _library.GetEpisodesForSection(sectionId);
        // Build the queue; PlayAll raises PlayRequested which MainViewModel routes to OpenPlayer.
        // Suppress that here — we want to show the Queue PAGE, not start video playback.
        // Silence play requests by pre-wiring and opening the player ourselves so MainViewModel
        // does not navigate away from the Queue page after we set it.
        _main.PlayQueue.PlayAll(episodes);
        // Navigate to Queue page (PlayAll set CurrentView via PlayRequested → OpenPlayer; reset it).
        _main.CurrentView = AppView.Queue;
    }

    /// <summary>
    /// Plays the richest series' first clip AND opens the in-player queue drawer with the
    /// section's full episode list, so the capture shows the opaque right-hand drawer over
    /// live video. AutoHideSuppressed keeps the transport visible.
    /// </summary>
    private async Task PlayWithQueueDrawerAsync()
    {
        var (sectionId, series) = await FindRichestSeriesAsync();
        var episode = _library.GetEpisodes(series.SeriesId).FirstOrDefault()
            ?? throw new InvalidOperationException("Richest series has no episodes to play.");

        // Keep transport visible for the screenshot.
        _main.Player.AutoHideSuppressed = true;

        // Build the queue from the full section (same section the player clip belongs to),
        // then play the first item.  PlayAll raises PlayRequested → MainViewModel.OpenPlayer.
        var allEps = _library.GetEpisodesForSection(sectionId);
        _main.PlayQueue.PlayAll(allEps);

        // Allow the player to initialise before opening the drawer.
        await Task.Delay(800);
        _main.PlayQueue.IsQueueOpen = true;
    }
}
