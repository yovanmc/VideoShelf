using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests.ViewModels;

public sealed class TagEditorViewModelTests
{
    // ── Fixture helpers ──────────────────────────────────────────────────────

    private sealed record Fx(
        AppTempDb Db,
        LibraryRepository Lib,
        TagRepository Tags,
        TagEditorViewModel Vm,
        long SectionId,
        long SeriesId,
        long VideoId);

    private static Fx NewFx()
    {
        var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var tags = new TagRepository(db.Db);

        var sourceId = lib.UpsertSource(@"C:\m", "M");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");
        var seriesId = lib.UpsertSeries(sectionId, "Show A", isStandalone: false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\m\Creator A\Show A\ep01.mkv", 1, "mkv");

        var vm = new TagEditorViewModel(tags);
        return new Fx(db, lib, tags, vm, sectionId, seriesId, videoId);
    }

    // ── Load fills Tags from the correct table ───────────────────────────────

    [Fact]
    public void Load_Section_fills_Tags_from_section_tags()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddTag(f.SectionId, "comedy");
        f.Tags.AddTag(f.SectionId, "drama");
        f.Vm.Load(TagLevel.Section, f.SectionId);
        f.Vm.Tags.ShouldBe(new[] { "comedy", "drama" }, ignoreOrder: true);
    }

    [Fact]
    public void Load_Series_fills_Tags_from_series_tags()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddSeriesTag(f.SeriesId, "thriller");
        f.Vm.Load(TagLevel.Series, f.SeriesId);
        f.Vm.Tags.ShouldContain("thriller");
        f.Vm.Tags.ShouldNotContain("comedy");
    }

    [Fact]
    public void Load_Video_fills_Tags_from_video_tags()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddVideoTag(f.VideoId, "action");
        f.Vm.Load(TagLevel.Video, f.VideoId);
        f.Vm.Tags.ShouldContain("action");
    }

    // ── Load populates Inherited with correct source labels ──────────────────

    [Fact]
    public void Load_Series_Inherited_has_section_tags_labelled_from_Creator()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddTag(f.SectionId, "comedy");
        f.Vm.Load(TagLevel.Series, f.SeriesId);
        f.Vm.Inherited.ShouldContain(x => x.Tag == "comedy" && x.SourceLabel == "from Creator");
    }

    [Fact]
    public void Load_Video_Inherited_includes_series_tags_labelled_from_Series()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddSeriesTag(f.SeriesId, "drama");
        f.Vm.Load(TagLevel.Video, f.VideoId);
        f.Vm.Inherited.ShouldContain(x => x.Tag == "drama" && x.SourceLabel == "from Series");
    }

    [Fact]
    public void Load_Video_Inherited_includes_section_tags_labelled_from_Creator()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddTag(f.SectionId, "action");
        f.Vm.Load(TagLevel.Video, f.VideoId);
        f.Vm.Inherited.ShouldContain(x => x.Tag == "action" && x.SourceLabel == "from Creator");
    }

    [Fact]
    public void Load_Section_Inherited_is_empty()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddTag(f.SectionId, "comedy");
        f.Vm.Load(TagLevel.Section, f.SectionId);
        f.Vm.Inherited.ShouldBeEmpty();
    }

    // ── Inherited tag also applied at this level → NOT double-listed ─────────

    [Fact]
    public void Load_Series_inherited_tag_also_applied_is_not_in_Inherited()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddTag(f.SectionId, "shared");       // parent → would be inherited
        f.Tags.AddSeriesTag(f.SeriesId, "shared");  // also applied at this level
        f.Vm.Load(TagLevel.Series, f.SeriesId);
        f.Vm.Tags.ShouldContain("shared");
        // Must NOT appear in Inherited as well
        f.Vm.Inherited.ShouldNotContain(x => x.Tag == "shared");
    }

    [Fact]
    public void Load_Video_inherited_tag_also_applied_is_not_in_Inherited()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddSeriesTag(f.SeriesId, "shared");  // parent → would be inherited
        f.Tags.AddVideoTag(f.VideoId, "shared");    // also applied at this level
        f.Vm.Load(TagLevel.Video, f.VideoId);
        f.Vm.Tags.ShouldContain("shared");
        f.Vm.Inherited.ShouldNotContain(x => x.Tag == "shared");
    }

    // ── AddTag persists to the correct table ─────────────────────────────────

    [Fact]
    public void AddTag_Section_writes_to_section_tags_and_appears_in_Tags()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Vm.Load(TagLevel.Section, f.SectionId);
        f.Vm.TagInput = "Comedy";
        f.Vm.AddTagCommand.Execute(null);
        f.Vm.Tags.ShouldContain("comedy");
        f.Tags.GetTags(f.SectionId).ShouldContain("comedy");
        f.Vm.TagInput.ShouldBeEmpty();
    }

    [Fact]
    public void AddTag_Series_writes_to_series_tags()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Vm.Load(TagLevel.Series, f.SeriesId);
        f.Vm.TagInput = "Thriller";
        f.Vm.AddTagCommand.Execute(null);
        f.Vm.Tags.ShouldContain("thriller");
        f.Tags.GetSeriesTags(f.SeriesId).ShouldContain("thriller");
        f.Vm.TagInput.ShouldBeEmpty();
    }

    [Fact]
    public void AddTag_Video_writes_to_video_tags()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Vm.Load(TagLevel.Video, f.VideoId);
        f.Vm.TagInput = "Action";
        f.Vm.AddTagCommand.Execute(null);
        f.Vm.Tags.ShouldContain("action");
        f.Tags.GetVideoTags(f.VideoId).ShouldContain("action");
        f.Vm.TagInput.ShouldBeEmpty();
    }

    [Fact]
    public void AddTag_raises_Changed()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Vm.Load(TagLevel.Section, f.SectionId);
        int raised = 0;
        f.Vm.Changed += () => raised++;
        f.Vm.TagInput = "drama";
        f.Vm.AddTagCommand.Execute(null);
        raised.ShouldBe(1);
    }

    // ── RemoveTag removes from the correct table and raises Changed ──────────

    [Fact]
    public void RemoveTag_Section_deletes_from_section_tags_and_raises_Changed()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddTag(f.SectionId, "comedy");
        f.Vm.Load(TagLevel.Section, f.SectionId);
        int raised = 0;
        f.Vm.Changed += () => raised++;
        f.Vm.RemoveTagCommand.Execute("comedy");
        f.Vm.Tags.ShouldNotContain("comedy");
        f.Tags.GetTags(f.SectionId).ShouldNotContain("comedy");
        raised.ShouldBe(1);
    }

    [Fact]
    public void RemoveTag_Series_deletes_from_series_tags_and_raises_Changed()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddSeriesTag(f.SeriesId, "drama");
        f.Vm.Load(TagLevel.Series, f.SeriesId);
        int raised = 0;
        f.Vm.Changed += () => raised++;
        f.Vm.RemoveTagCommand.Execute("drama");
        f.Vm.Tags.ShouldNotContain("drama");
        f.Tags.GetSeriesTags(f.SeriesId).ShouldNotContain("drama");
        raised.ShouldBe(1);
    }

    [Fact]
    public void RemoveTag_Video_deletes_from_video_tags_and_raises_Changed()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddVideoTag(f.VideoId, "action");
        f.Vm.Load(TagLevel.Video, f.VideoId);
        int raised = 0;
        f.Vm.Changed += () => raised++;
        f.Vm.RemoveTagCommand.Execute("action");
        f.Vm.Tags.ShouldNotContain("action");
        f.Tags.GetVideoTags(f.VideoId).ShouldNotContain("action");
        raised.ShouldBe(1);
    }

    // ── Suggestions filter by input substring, exclude applied + inherited ───

    [Fact]
    public void Suggestions_filter_by_TagInput_substring()
    {
        var f = NewFx(); using var _d = f.Db;
        // Add tags at the section level of a SIBLING section so they appear in
        // GetAllTagsAcrossLevels() but are NOT inherited by our target series.
        var sourceId = f.Lib.GetSources()[0].Id;
        var otherSection = f.Lib.UpsertSection(sourceId, "Creator B");
        f.Tags.AddTag(otherSection, "comedy");
        f.Tags.AddTag(otherSection, "comic relief");
        f.Tags.AddTag(otherSection, "drama");
        f.Vm.Load(TagLevel.Series, f.SeriesId);  // none applied, none inherited from sibling section
        f.Vm.TagInput = "com";
        f.Vm.Suggestions.ShouldContain("comedy");
        f.Vm.Suggestions.ShouldContain("comic relief");
        f.Vm.Suggestions.ShouldNotContain("drama");
    }

    [Fact]
    public void Suggestions_exclude_already_applied_tags()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddSeriesTag(f.SeriesId, "drama");  // applied at this level
        f.Vm.Load(TagLevel.Series, f.SeriesId);
        f.Vm.TagInput = "drama";
        f.Vm.Suggestions.ShouldNotContain("drama");
    }

    [Fact]
    public void Suggestions_exclude_inherited_tags()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddTag(f.SectionId, "comedy");  // will be inherited for series
        f.Vm.Load(TagLevel.Series, f.SeriesId);
        // "comedy" is in _allTags but should be excluded from Suggestions (already in Inherited)
        f.Vm.TagInput = "comedy";
        f.Vm.Suggestions.ShouldNotContain("comedy");
    }

    // ── AddSuggestion shorthand ──────────────────────────────────────────────

    [Fact]
    public void AddSuggestion_sets_input_and_adds_tag()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Vm.Load(TagLevel.Section, f.SectionId);
        f.Vm.AddSuggestionCommand.Execute("drama");
        f.Vm.Tags.ShouldContain("drama");
        f.Vm.TagInput.ShouldBeEmpty();
    }
}
