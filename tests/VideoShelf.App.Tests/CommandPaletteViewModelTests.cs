using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public sealed class CommandPaletteViewModelTests : IDisposable
{
    // ── Fixture ────────────────────────────────────────────────────────────────

    private readonly AppTempDb _db;
    private readonly LibraryRepository _lib;

    // Spy state for navigation/play callbacks.
    private readonly List<long> _navigatedSections = [];
    private readonly List<long> _playedVideoIds    = [];
    private bool _closedFired;

    // Static action registry (11 entries: Home/Browse/Settings/etc.)
    private readonly List<(string Label, string Icon, Action Execute)> _actions;

    // The VM under test.
    private readonly CommandPaletteViewModel _vm;

    // Section + video seeded for DB-result tests.
    private long _sectionId;
    private long _videoId;

    public CommandPaletteViewModelTests()
    {
        _db  = new AppTempDb();
        _lib = new LibraryRepository(_db.Db);

        // Seed: one creator + one video.
        var srcId = _lib.UpsertSource(@"C:\V", "V");
        _sectionId = _lib.UpsertSection(srcId, "NatGeo");
        var seriesId = _lib.UpsertSeries(_sectionId, "Planet Earth", false);
        _videoId = _lib.UpsertVideo(seriesId, @"C:\V\NatGeo\e01.mp4", 1, ".mp4");

        // Build a simple action registry (3 entries sufficient for unit tests).
        _actions =
        [
            ("Home",         "Home24",     () => { }),
            ("Browse",       "Apps24",     () => { }),
            ("Settings",     "Settings24", () => { }),
        ];

        _vm = new CommandPaletteViewModel(
            _lib,
            actionRegistryFactory: () => _actions,
            openSection: id => { _navigatedSections.Add(id); return Task.CompletedTask; },
            playVideo:   id => _playedVideoIds.Add(id));

        _vm.CloseRequested += (_, _) => _closedFired = true;
    }

    public void Dispose() => _db.Dispose();

    // ── Reset / initial state ──────────────────────────────────────────────────

    [Fact]
    public void Initial_state_has_empty_query_and_no_results()
    {
        _vm.Query.ShouldBe(string.Empty);
        _vm.Results.ShouldBeEmpty();
        _vm.SelectedIndex.ShouldBe(-1);
    }

    [Fact]
    public void Reset_clears_query_results_and_selection()
    {
        _vm.Reset();
        _vm.Query.ShouldBe(string.Empty);
        _vm.Results.ShouldBeEmpty();
        _vm.SelectedIndex.ShouldBe(-1);
    }

    // ── Empty query shows all actions ─────────────────────────────────────────

    [Fact]
    public async Task Empty_query_after_debounce_shows_all_actions()
    {
        // Set a non-empty query first so the PropertyChanged fires when we clear it.
        _vm.Query = "x";
        await _vm.WaitForIdleAsync();

        _vm.Query = "";
        await _vm.WaitForIdleAsync();

        // empty query → all 3 actions, sorted by label.
        _vm.Results.Count.ShouldBe(3);
        _vm.Results.ShouldAllBe(r => r.Kind == PaletteItemKind.Action);
    }

    // ── Action matching ────────────────────────────────────────────────────────

    [Fact]
    public async Task Query_matching_action_label_returns_action_result()
    {
        _vm.Query = "home";
        await _vm.WaitForIdleAsync();

        _vm.Results.ShouldContain(r => r.Label == "Home" && r.Kind == PaletteItemKind.Action);
    }

    [Fact]
    public async Task Query_not_matching_any_action_or_db_returns_empty()
    {
        _vm.Query = "zzzyyyxxx";
        await _vm.WaitForIdleAsync();
        _vm.Results.ShouldBeEmpty();
    }

    // ── Creator DB results ────────────────────────────────────────────────────

    [Fact]
    public async Task Query_matching_creator_name_returns_creator_result()
    {
        _vm.Query = "NatGeo";
        await _vm.WaitForIdleAsync();

        _vm.Results.ShouldContain(r => r.Label == "NatGeo" && r.Kind == PaletteItemKind.Creator);
    }

    // ── Series DB results ─────────────────────────────────────────────────────

    [Fact]
    public async Task Query_matching_series_title_returns_series_result()
    {
        _vm.Query = "Planet";
        await _vm.WaitForIdleAsync();

        _vm.Results.ShouldContain(r => r.Label == "Planet Earth" && r.Kind == PaletteItemKind.Series);
    }

    [Fact]
    public async Task Series_result_has_episode_count_sub_label()
    {
        _vm.Query = "Planet Earth";
        await _vm.WaitForIdleAsync();

        var item = _vm.Results.FirstOrDefault(r => r.Kind == PaletteItemKind.Series);
        item.ShouldNotBeNull();
        item!.SubLabel.ShouldNotBeNullOrEmpty();
        item.SubLabel.ShouldContain("ep");
    }

    [Fact]
    public async Task Series_result_appears_between_creator_and_video_in_sort_order()
    {
        _vm.Query = "Planet";
        await _vm.WaitForIdleAsync();

        // Should have at least a series and video result for "Planet Earth".
        var seriesIdx = _vm.Results.IndexOf(_vm.Results.First(r => r.Kind == PaletteItemKind.Series));
        var videoIdx  = _vm.Results.IndexOf(_vm.Results.First(r => r.Kind == PaletteItemKind.Video));
        seriesIdx.ShouldBeLessThan(videoIdx);
    }

    // ── Video DB results ──────────────────────────────────────────────────────

    [Fact]
    public async Task Query_matching_video_series_returns_video_result()
    {
        _vm.Query = "Planet";
        await _vm.WaitForIdleAsync();

        _vm.Results.ShouldContain(r => r.Kind == PaletteItemKind.Video);
    }

    // ── Execute commands ──────────────────────────────────────────────────────

    [Fact]
    public void ExecuteItem_on_action_runs_action_and_fires_CloseRequested()
    {
        bool actionFired = false;
        var item = new PaletteItemViewModel(
            "Home", "Home24", PaletteItemKind.Action,
            () => actionFired = true);

        _vm.ExecuteItemCommand.Execute(item);

        actionFired.ShouldBeTrue();
        _closedFired.ShouldBeTrue();
    }

    [Fact]
    public void ExecuteItem_on_creator_calls_openSection_and_fires_CloseRequested()
    {
        var item = new PaletteItemViewModel(
            "NatGeo", "Apps24", PaletteItemKind.Creator,
            () => _ = Task.Run(() => _navigatedSections.Add(_sectionId)));

        _vm.ExecuteItemCommand.Execute(item);

        _closedFired.ShouldBeTrue();
    }

    [Fact]
    public void CloseCommand_fires_CloseRequested_without_executing()
    {
        _vm.CloseCommand.Execute(null);
        _closedFired.ShouldBeTrue();
        _navigatedSections.ShouldBeEmpty();
        _playedVideoIds.ShouldBeEmpty();
    }

    // ── Navigation: arrow keys + selection ────────────────────────────────────

    [Fact]
    public async Task MoveDown_advances_selection_index()
    {
        _vm.Query = "home";
        await _vm.WaitForIdleAsync();
        _vm.Results.Count.ShouldBeGreaterThan(0);

        _vm.SelectedIndex = 0;
        _vm.MoveDownCommand.Execute(null);
        // Wraps to 0 since there's only one result.
        _vm.SelectedIndex.ShouldBe(0);
    }

    [Fact]
    public async Task MoveUp_wraps_from_zero_to_last()
    {
        // Trigger a query change so the debounce runs.
        _vm.Query = "x";
        await _vm.WaitForIdleAsync();
        _vm.Query = "";
        await _vm.WaitForIdleAsync();
        _vm.Results.Count.ShouldBe(3);

        _vm.SelectedIndex = 0;
        _vm.MoveUpCommand.Execute(null);
        _vm.SelectedIndex.ShouldBe(2); // wrap to last
    }

    [Fact]
    public async Task MoveDown_wraps_from_last_to_first()
    {
        // Trigger a query change so the debounce runs.
        _vm.Query = "x";
        await _vm.WaitForIdleAsync();
        _vm.Query = "";
        await _vm.WaitForIdleAsync();
        _vm.Results.Count.ShouldBe(3);

        _vm.SelectedIndex = 2; // last
        _vm.MoveDownCommand.Execute(null);
        _vm.SelectedIndex.ShouldBe(0); // wrap to first
    }

    [Fact]
    public async Task ExecuteSelected_runs_selected_item_and_fires_close()
    {
        // Populate results by querying for "home".
        _vm.Query = "home";
        await _vm.WaitForIdleAsync();

        _vm.Results.Count.ShouldBeGreaterThan(0);
        _vm.SelectedIndex = 0;
        // The item at index 0 is the Home action (prefix match, score 0.9).
        _vm.ExecuteSelectedCommand.Execute(null);
        _closedFired.ShouldBeTrue();
    }
}
