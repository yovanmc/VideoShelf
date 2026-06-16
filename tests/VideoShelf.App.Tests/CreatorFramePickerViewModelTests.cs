using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shouldly;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using Xunit;

namespace VideoShelf.App.Tests;

/// <summary>
/// Unit tests for the pure helpers on <see cref="CreatorFramePickerViewModel"/>.
/// No libVLC, no DB, no disk I/O required.
/// </summary>
public class CreatorFramePickerViewModelTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static Series MakeSeries(long id, long sectionId = 1, string title = "")
        => new(id, sectionId, title.Length > 0 ? title : $"Series{id}",
               $"series{id}", IsStandalone: false);

    private static Video MakeVideo(long id, long seriesId, string path, bool missing = false)
        => new(id, seriesId, path, EpisodeNo: 1, RawFilename: Path.GetFileName(path),
               Format: "mkv", Duration: 60.0, ThumbnailPath: null,
               Watched: false, AddedAt: "", Missing: missing);

    // ── SelectCandidateVideos ─────────────────────────────────────────────────

    [Fact]
    public void SelectCandidateVideos_empty_input_returns_empty()
    {
        var result = CreatorFramePickerViewModel.SelectCandidateVideos([], max: 5);
        result.ShouldBeEmpty();
    }

    [Fact]
    public void SelectCandidateVideos_max_zero_returns_empty()
    {
        var s = MakeSeries(1);
        var v = MakeVideo(1, 1, @"C:\v\a.mkv");
        var result = CreatorFramePickerViewModel.SelectCandidateVideos([(s, v)], max: 0);
        result.ShouldBeEmpty();
    }

    [Fact]
    public void SelectCandidateVideos_returns_at_most_max()
    {
        var s = MakeSeries(1);
        var videos = Enumerable.Range(1, 20)
            .Select(i => (s, MakeVideo(i, 1, $@"C:\v\{i}.mkv")))
            .ToList();

        var result = CreatorFramePickerViewModel.SelectCandidateVideos(videos, max: 5);

        result.Count.ShouldBe(5);
    }

    [Fact]
    public void SelectCandidateVideos_fewer_than_max_returns_all()
    {
        var s = MakeSeries(1);
        var videos = new List<(Series, Video)>
        {
            (s, MakeVideo(1, 1, @"C:\v\a.mkv")),
            (s, MakeVideo(2, 1, @"C:\v\b.mkv")),
        };

        var result = CreatorFramePickerViewModel.SelectCandidateVideos(videos, max: 10);

        result.Count.ShouldBe(2);
    }

    [Fact]
    public void SelectCandidateVideos_spreads_across_series_round_robin()
    {
        // Series A has 5 videos, Series B has 5 videos; max=4 → should be 2 from each.
        var sA = MakeSeries(1, title: "A");
        var sB = MakeSeries(2, title: "B");

        var videos = new List<(Series, Video)>();
        for (int i = 1; i <= 5; i++)
            videos.Add((sA, MakeVideo(i, 1, $@"C:\v\A{i}.mkv")));
        for (int i = 6; i <= 10; i++)
            videos.Add((sB, MakeVideo(i, 2, $@"C:\v\B{i}.mkv")));

        var result = CreatorFramePickerViewModel.SelectCandidateVideos(videos, max: 4);

        result.Count.ShouldBe(4);
        result.Count(r => r.Series.Id == 1).ShouldBe(2);
        result.Count(r => r.Series.Id == 2).ShouldBe(2);
    }

    [Fact]
    public void SelectCandidateVideos_small_series_does_not_block_large_series()
    {
        // Series A has 1 video, Series B has 10; max=6 → A gets 1, B gets 5.
        var sA = MakeSeries(1, title: "A");
        var sB = MakeSeries(2, title: "B");

        var videos = new List<(Series, Video)>();
        videos.Add((sA, MakeVideo(1, 1, @"C:\v\A1.mkv")));
        for (int i = 2; i <= 11; i++)
            videos.Add((sB, MakeVideo(i, 2, $@"C:\v\B{i}.mkv")));

        var result = CreatorFramePickerViewModel.SelectCandidateVideos(videos, max: 6);

        result.Count.ShouldBe(6);
        result.Count(r => r.Series.Id == 1).ShouldBe(1);
        result.Count(r => r.Series.Id == 2).ShouldBe(5);
    }

    [Fact]
    public void SelectCandidateVideos_single_series_fills_all_slots()
    {
        var s = MakeSeries(1);
        var videos = Enumerable.Range(1, 10)
            .Select(i => (s, MakeVideo(i, 1, $@"C:\v\{i}.mkv")))
            .ToList();

        var result = CreatorFramePickerViewModel.SelectCandidateVideos(videos, max: 8);

        result.Count.ShouldBe(8);
        result.ShouldAllBe(r => r.Series.Id == 1);
    }

    [Fact]
    public void SelectCandidateVideos_preserves_series_order_of_first_appearance()
    {
        // Series B is listed first in input — its entries should appear first.
        var sB = MakeSeries(2, title: "B");
        var sA = MakeSeries(1, title: "A");

        var videos = new List<(Series, Video)>
        {
            (sB, MakeVideo(10, 2, @"C:\v\B1.mkv")),
            (sA, MakeVideo(20, 1, @"C:\v\A1.mkv")),
        };

        var result = CreatorFramePickerViewModel.SelectCandidateVideos(videos, max: 2);

        result[0].Series.Id.ShouldBe(2); // B first
        result[1].Series.Id.ShouldBe(1);
    }

    // ── BuildCandidateFramePath ───────────────────────────────────────────────

    [Fact]
    public void BuildCandidateFramePath_is_under_covers_dir()
    {
        var coversDir = @"C:\AppData\VideoShelf\covers";
        var path = CreatorFramePickerViewModel.BuildCandidateFramePath(sectionId: 7, coversDir);

        path.ShouldStartWith(coversDir + Path.DirectorySeparatorChar);
        path.ShouldEndWith(".png");
    }

    [Fact]
    public void BuildCandidateFramePath_contains_section_id()
    {
        var path = CreatorFramePickerViewModel.BuildCandidateFramePath(sectionId: 42, @"C:\covers");
        Path.GetFileName(path).ShouldStartWith("creator_42_");
    }

    [Fact]
    public void BuildCandidateFramePath_is_unique_across_calls()
    {
        var p1 = CreatorFramePickerViewModel.BuildCandidateFramePath(1, @"C:\covers");
        var p2 = CreatorFramePickerViewModel.BuildCandidateFramePath(1, @"C:\covers");
        p1.ShouldNotBe(p2);
    }

    [Fact]
    public void BuildCandidateFramePath_never_writes_into_library_folder()
    {
        // Covers dir is always under AppData, not the library root.
        // This test asserts the path is ONLY under the provided coversDir.
        var coversDir = @"C:\Users\user\AppData\Local\VideoShelf\covers";
        var libraryDir = @"D:\MyVideos\Creator";

        var path = CreatorFramePickerViewModel.BuildCandidateFramePath(1, coversDir);

        path.ShouldStartWith(coversDir);
        path.ShouldNotStartWith(libraryDir);
    }
}
