using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests;

public sealed class SectionDetailViewModelTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    private sealed record Fx(AppTempDb Db, LibraryRepository Lib, TagRepository Tags,
        SectionDetailViewModel Vm, long SectionId);

    private static Fx NewFx()
    {
        var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var tags = new TagRepository(db.Db);
        var watch = new WatchRepository(db.Db);
        var art = new CreatorArtRepository(db.Db);
        var settings = new SettingsRepository(db.Db);
        var src = lib.UpsertSource(@"C:\m", "M");
        var sec = lib.UpsertSection(src, "Creator A");
        lib.UpsertVideo(lib.UpsertSeries(sec, "Show", false), @"C:\m\Show\e01.mkv", 1, "mkv");
        var playQueue = new PlayQueueViewModel(lib, settings);
        var vm = new SectionDetailViewModel(lib, tags, watch, new NullThumbs(), art, new FakeImagePicker(null), playQueue);
        return new Fx(db, lib, tags, vm, sec);
    }

    [Fact]
    public async Task LoadAsync_loads_name_series_and_existing_tags()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddTag(f.SectionId, "comedy");
        await f.Vm.LoadAsync(f.SectionId);
        f.Vm.DisplayName.ShouldBe("Creator A");
        f.Vm.SeriesList.ShouldNotBeEmpty();
        f.Vm.Tags.ShouldBe(new[] { "comedy" });
        f.Vm.IsEditing.ShouldBeFalse();
    }

    [Fact]
    public async Task ToggleEdit_flips_IsEditing()
    {
        var f = NewFx(); using var _d = f.Db;
        await f.Vm.LoadAsync(f.SectionId);
        f.Vm.IsEditing.ShouldBeFalse();
        f.Vm.ToggleEditCommand.Execute(null);
        f.Vm.IsEditing.ShouldBeTrue();
        f.Vm.ToggleEditCommand.Execute(null);
        f.Vm.IsEditing.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadAsync_sets_VideoCount()
    {
        var f = NewFx(); using var _d = f.Db;
        await f.Vm.LoadAsync(f.SectionId);
        // One video was upserted in NewFx
        f.Vm.VideoCount.ShouldBe(1);
    }

    [Fact]
    public async Task LoadAsync_BackgroundImagePath_is_null_with_no_override_and_NullThumbs()
    {
        var f = NewFx(); using var _d = f.Db;
        await f.Vm.LoadAsync(f.SectionId);
        // NullThumbs returns null and no override set → BackgroundImagePath is null
        f.Vm.BackgroundImagePath.ShouldBeNull();
    }

    [Fact]
    public async Task AddTag_persists_and_appears_in_collection()
    {
        var f = NewFx(); using var _d = f.Db;
        await f.Vm.LoadAsync(f.SectionId);
        f.Vm.TagInput = "Drama";
        f.Vm.AddTagCommand.Execute(null);
        f.Vm.Tags.ShouldContain("drama");
        f.Tags.GetTags(f.SectionId).ShouldContain("drama");
        f.Vm.TagInput.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveTag_persists_removal()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddTag(f.SectionId, "comedy");
        await f.Vm.LoadAsync(f.SectionId);
        f.Vm.RemoveTagCommand.Execute("comedy");
        f.Vm.Tags.ShouldNotContain("comedy");
        f.Tags.GetTags(f.SectionId).ShouldNotContain("comedy");
    }

    [Fact]
    public async Task AddSuggestion_adds_and_clears_input()
    {
        var f = NewFx(); using var _d = f.Db;
        await f.Vm.LoadAsync(f.SectionId);
        f.Vm.AddSuggestionCommand.Execute("drama");
        f.Vm.Tags.ShouldContain("drama");
        f.Vm.TagInput.ShouldBeEmpty();
    }

    [Fact]
    public async Task TagInput_change_updates_suggestions_excluding_already_applied()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.GetSources()[0];
        var other = f.Lib.UpsertSection(src.Id, "Creator B");
        f.Tags.AddTag(other, "comedy");
        f.Tags.AddTag(other, "comic relief");
        f.Tags.AddTag(f.SectionId, "comedy");      // already applied here
        await f.Vm.LoadAsync(f.SectionId);
        f.Vm.TagInput = "com";
        f.Vm.Suggestions.ShouldContain("comic relief");
        f.Vm.Suggestions.ShouldNotContain("comedy"); // already applied -> excluded
    }

    [Fact]
    public async Task PlayAll_builds_queue_from_section_episodes()
    {
        var db = new AppTempDb(); using var _d = db;
        var lib = new LibraryRepository(db.Db);
        var tags = new TagRepository(db.Db);
        var watch = new WatchRepository(db.Db);
        var art = new CreatorArtRepository(db.Db);
        var settings = new SettingsRepository(db.Db);
        var playQueue = new PlayQueueViewModel(lib, settings);
        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "Creator");
        var s1 = lib.UpsertSeries(sec, "Alpha", false);
        lib.UpsertVideo(s1, @"C:\V\Creator\Alpha 1.mp4", 1, ".mp4");
        lib.UpsertVideo(s1, @"C:\V\Creator\Alpha 2.mp4", 2, ".mp4");
        var s2 = lib.UpsertSeries(sec, "Beta", true);
        lib.UpsertVideo(s2, @"C:\V\Creator\Beta.mp4", 1, ".mp4");

        var vm = new SectionDetailViewModel(lib, tags, watch, new NullThumbs(), art, new FakeImagePicker(null), playQueue);
        await vm.LoadAsync(sec);

        vm.PlayAllCommand.Execute(null);

        playQueue.HasQueue.ShouldBeTrue();
        playQueue.Items.Count.ShouldBe(3);
    }

    // ── E3: CollapseAll / ExpandAll ──────────────────────────────────────────

    private static async Task<(AppTempDb Db, SectionDetailViewModel Vm)> NewMultiSeriesFx()
    {
        var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var tags = new TagRepository(db.Db);
        var watch = new WatchRepository(db.Db);
        var art = new CreatorArtRepository(db.Db);
        var settings = new SettingsRepository(db.Db);
        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "Creator Multi");
        // Three non-standalone series with one episode each
        var s1 = lib.UpsertSeries(sec, "Series A", false);
        lib.UpsertVideo(s1, @"C:\V\Creator\A\e01.mkv", 1, "mkv");
        var s2 = lib.UpsertSeries(sec, "Series B", false);
        lib.UpsertVideo(s2, @"C:\V\Creator\B\e01.mkv", 1, "mkv");
        var s3 = lib.UpsertSeries(sec, "Series C", false);
        lib.UpsertVideo(s3, @"C:\V\Creator\C\e01.mkv", 1, "mkv");
        var playQueue = new PlayQueueViewModel(lib, settings);
        var vm = new SectionDetailViewModel(lib, tags, watch, new NullThumbs(), art, new FakeImagePicker(null), playQueue);
        await vm.LoadAsync(sec);
        return (db, vm);
    }

    [Fact]
    public async Task CollapseAll_sets_all_series_IsExpanded_to_false()
    {
        var (db, vm) = await NewMultiSeriesFx(); using var _d = db;
        // Manually expand two of three
        vm.SeriesList[0].IsExpanded = true;
        vm.SeriesList[1].IsExpanded = true;
        vm.SeriesList[0].IsExpanded.ShouldBeTrue();

        vm.CollapseAllCommand.Execute(null);

        foreach (var s in vm.SeriesList)
            s.IsExpanded.ShouldBeFalse();
    }

    [Fact]
    public async Task ExpandAll_sets_all_non_standalone_series_IsExpanded_to_true()
    {
        var (db, vm) = await NewMultiSeriesFx(); using var _d = db;
        // All start collapsed
        foreach (var s in vm.SeriesList)
            s.IsExpanded.ShouldBeFalse();

        await vm.ExpandAllCommand.ExecuteAsync(null);

        // All non-standalone series should be expanded
        foreach (var s in vm.SeriesList.Where(s => !s.IsStandalone))
            s.IsExpanded.ShouldBeTrue();
    }

    [Fact]
    public async Task ExpandAll_triggers_episode_load_for_all_non_standalone_series()
    {
        var (db, vm) = await NewMultiSeriesFx(); using var _d = db;
        // Before expand: no episodes loaded (lazy)
        foreach (var s in vm.SeriesList)
            s.Episodes.ShouldBeEmpty();

        await vm.ExpandAllCommand.ExecuteAsync(null);

        // After expand: each non-standalone series has episodes loaded
        foreach (var s in vm.SeriesList.Where(s => !s.IsStandalone))
            s.Episodes.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task CollapseAll_after_ExpandAll_collapses_all()
    {
        var (db, vm) = await NewMultiSeriesFx(); using var _d = db;

        await vm.ExpandAllCommand.ExecuteAsync(null);
        foreach (var s in vm.SeriesList.Where(s => !s.IsStandalone))
            s.IsExpanded.ShouldBeTrue();

        vm.CollapseAllCommand.Execute(null);

        foreach (var s in vm.SeriesList)
            s.IsExpanded.ShouldBeFalse();
    }

    [Fact]
    public async Task ExpandAll_on_already_expanded_is_idempotent()
    {
        var (db, vm) = await NewMultiSeriesFx(); using var _d = db;

        // Expand once to load episodes
        await vm.ExpandAllCommand.ExecuteAsync(null);
        var episodeCounts = vm.SeriesList.Select(s => s.Episodes.Count).ToArray();

        // Expand again — should not duplicate episodes or throw
        await vm.ExpandAllCommand.ExecuteAsync(null);

        var episodeCountsAfter = vm.SeriesList.Select(s => s.Episodes.Count).ToArray();
        episodeCountsAfter.ShouldBe(episodeCounts);
    }

    [Fact]
    public async Task CollapseAll_does_not_unload_episodes()
    {
        var (db, vm) = await NewMultiSeriesFx(); using var _d = db;

        // Expand to load episodes
        await vm.ExpandAllCommand.ExecuteAsync(null);
        foreach (var s in vm.SeriesList.Where(s => !s.IsStandalone))
            s.Episodes.ShouldNotBeEmpty();

        // Collapse: episodes remain in memory (just visually hidden)
        vm.CollapseAllCommand.Execute(null);
        foreach (var s in vm.SeriesList.Where(s => !s.IsStandalone))
            s.Episodes.ShouldNotBeEmpty();
    }
}

public sealed class SectionDetailCreatorArtTests
{
    private sealed class FakePicker(string? result) : IImagePicker
    {
        public string? PickImage(string? initialFolder = null) => result;
    }

    private static SectionDetailViewModel CreateVm(AppTempDb temp, IImagePicker picker, CreatorArtRepository art)
    {
        var lib = new LibraryRepository(temp.Db);
        var tags = new TagRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var settings = new SettingsRepository(temp.Db);
        var playQueue = new PlayQueueViewModel(lib, settings);
        return new SectionDetailViewModel(lib, tags, watch, new NullThumbs(), art, picker, playQueue);
    }

    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task SetCreatorArt_picks_and_persists_then_exposes_path()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var art = new CreatorArtRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");

        var vm = CreateVm(temp, new FakePicker(@"C:\pics\a.png"), art);
        await vm.LoadAsync(sectionId);

        await vm.SetCreatorArtCommand.ExecuteAsync(null);

        art.GetArtPath(sectionId).ShouldBe(@"C:\pics\a.png");
        vm.CreatorArtPath.ShouldBe(@"C:\pics\a.png");
    }

    [Fact]
    public async Task SetCreatorArt_sets_BackgroundImagePath_to_override()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var art = new CreatorArtRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");

        var vm = CreateVm(temp, new FakePicker(@"C:\pics\a.png"), art);
        await vm.LoadAsync(sectionId);

        await vm.SetCreatorArtCommand.ExecuteAsync(null);

        vm.BackgroundImagePath.ShouldBe(@"C:\pics\a.png");
    }

    [Fact]
    public async Task SetCreatorArt_noop_when_picker_cancelled()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var art = new CreatorArtRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");

        var vm = CreateVm(temp, new FakePicker(null), art);
        await vm.LoadAsync(sectionId);

        await vm.SetCreatorArtCommand.ExecuteAsync(null);

        art.GetArtPath(sectionId).ShouldBeNull();
    }

    [Fact]
    public async Task ClearCreatorArt_removes_override()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var art = new CreatorArtRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");
        art.SetArtPath(sectionId, @"C:\pics\a.png");

        var vm = CreateVm(temp, new FakePicker(null), art);
        await vm.LoadAsync(sectionId);
        await vm.ClearCreatorArtCommand.ExecuteAsync(null);

        art.GetArtPath(sectionId).ShouldBeNull();
        vm.CreatorArtPath.ShouldBeNull();
    }

    [Fact]
    public async Task ClearCreatorArt_re_resolves_BackgroundImagePath_to_null_when_no_seed()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var art = new CreatorArtRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");
        art.SetArtPath(sectionId, @"C:\pics\a.png");

        var vm = CreateVm(temp, new FakePicker(null), art);
        await vm.LoadAsync(sectionId);
        // Before clear: BackgroundImagePath == override
        vm.BackgroundImagePath.ShouldBe(@"C:\pics\a.png");

        await vm.ClearCreatorArtCommand.ExecuteAsync(null);

        // After clear: no seed (no videos in this section), NullThumbs → null
        vm.BackgroundImagePath.ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_refreshes_CreatorArtPath_from_db()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var art = new CreatorArtRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");
        art.SetArtPath(sectionId, @"C:\pics\existing.png");

        var vm = CreateVm(temp, new FakePicker(null), art);
        await vm.LoadAsync(sectionId);

        vm.CreatorArtPath.ShouldBe(@"C:\pics\existing.png");
        vm.HasCreatorArt.ShouldBeTrue();
    }

    [Fact]
    public async Task LoadAsync_BackgroundImagePath_equals_override_when_art_set()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var art = new CreatorArtRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");
        art.SetArtPath(sectionId, @"C:\pics\existing.png");

        var vm = CreateVm(temp, new FakePicker(null), art);
        await vm.LoadAsync(sectionId);

        // Override path takes precedence → BackgroundImagePath == override
        vm.BackgroundImagePath.ShouldBe(@"C:\pics\existing.png");
    }

    [Fact]
    public async Task SetCreatorArt_before_LoadAsync_is_noop()
    {
        // SectionId defaults to 0; executing the command must not touch the DB or throw.
        using var temp = new AppTempDb();
        var art = new CreatorArtRepository(temp.Db);
        var vm = CreateVm(temp, new FakePicker(@"C:\pics\a.png"), art);

        // No LoadAsync called — SectionId is still 0.
        await vm.SetCreatorArtCommand.ExecuteAsync(null);
        // DB untouched: GetArtPath(0) should remain null (section 0 does not exist).
        art.GetArtPath(0).ShouldBeNull();
    }
}
