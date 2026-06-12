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
        var src = lib.UpsertSource(@"C:\m", "M");
        var sec = lib.UpsertSection(src, "Creator A");
        lib.UpsertVideo(lib.UpsertSeries(sec, "Show", false), @"C:\m\Show\e01.mkv", 1, "mkv");
        var vm = new SectionDetailViewModel(lib, tags, watch, new NullThumbs(), art, new FakeImagePicker(null));
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
        return new SectionDetailViewModel(lib, tags, watch, new NullThumbs(), art, picker);
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

        vm.SetCreatorArtCommand.Execute(null);

        art.GetArtPath(sectionId).ShouldBe(@"C:\pics\a.png");
        vm.CreatorArtPath.ShouldBe(@"C:\pics\a.png");
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

        vm.SetCreatorArtCommand.Execute(null);

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
        vm.ClearCreatorArtCommand.Execute(null);

        art.GetArtPath(sectionId).ShouldBeNull();
        vm.CreatorArtPath.ShouldBeNull();
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
}
