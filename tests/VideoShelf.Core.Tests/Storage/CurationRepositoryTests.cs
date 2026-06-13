using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class CurationRepositoryTests
{
    private static long SeedVideo(TempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        return lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
    }

    // ─── IsFavorite / SetFavorite ───────────────────────────────────────────

    [Fact]
    public void IsFavorite_returns_false_by_default()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var curation = new CurationRepository(temp.Db);

        curation.IsFavorite(videoId).ShouldBeFalse();
    }

    [Fact]
    public void SetFavorite_true_then_IsFavorite_returns_true()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var curation = new CurationRepository(temp.Db);

        curation.SetFavorite(videoId, true);

        curation.IsFavorite(videoId).ShouldBeTrue();
    }

    [Fact]
    public void SetFavorite_false_clears_the_flag()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var curation = new CurationRepository(temp.Db);

        curation.SetFavorite(videoId, true);
        curation.SetFavorite(videoId, false);

        curation.IsFavorite(videoId).ShouldBeFalse();
    }

    // ─── GetRating / SetRating ──────────────────────────────────────────────

    [Fact]
    public void GetRating_returns_0_by_default()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var curation = new CurationRepository(temp.Db);

        curation.GetRating(videoId).ShouldBe(0);
    }

    [Fact]
    public void SetRating_persists_value_and_GetRating_returns_it()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var curation = new CurationRepository(temp.Db);

        curation.SetRating(videoId, 4);

        curation.GetRating(videoId).ShouldBe(4);
    }

    [Fact]
    public void SetRating_clamps_negative_to_0()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var curation = new CurationRepository(temp.Db);

        curation.SetRating(videoId, -3);

        curation.GetRating(videoId).ShouldBe(0);
    }

    [Fact]
    public void SetRating_clamps_above_5_to_5()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var curation = new CurationRepository(temp.Db);

        curation.SetRating(videoId, 99);

        curation.GetRating(videoId).ShouldBe(5);
    }

    // ─── GetFavorites ───────────────────────────────────────────────────────

    [Fact]
    public void GetFavorites_returns_only_is_favorite_and_not_missing_videos()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var ser = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var vid1 = lib.UpsertVideo(ser, @"C:\V\S\a.mp4", 1, ".mp4");
        var vid2 = lib.UpsertVideo(ser, @"C:\V\S\b.mp4", 2, ".mp4");
        var vid3 = lib.UpsertVideo(ser, @"C:\V\S\c.mp4", 3, ".mp4");
        var curation = new CurationRepository(temp.Db);

        curation.SetFavorite(vid1, true);
        // vid2: not favorite
        curation.SetFavorite(vid3, true);
        // Mark vid3 as missing
        using (var conn = temp.Db.Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE videos SET missing=1 WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", vid3);
            cmd.ExecuteNonQuery();
        }

        var results = curation.GetFavorites(50);

        results.Count.ShouldBe(1);
        results[0].VideoId.ShouldBe(vid1);
    }

    [Fact]
    public void GetFavorites_respects_limit()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var ser = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var curation = new CurationRepository(temp.Db);

        for (var i = 1; i <= 5; i++)
        {
            var vid = lib.UpsertVideo(ser, $@"C:\V\S\ep{i:D2}.mp4", i, ".mp4");
            curation.SetFavorite(vid, true);
        }

        var results = curation.GetFavorites(3);

        results.Count.ShouldBe(3);
    }

    // ─── InWatchlist / SetWatchlist ─────────────────────────────────────────

    [Fact]
    public void InWatchlist_returns_false_by_default()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var curation = new CurationRepository(temp.Db);

        curation.InWatchlist(videoId).ShouldBeFalse();
    }

    [Fact]
    public void SetWatchlist_true_stamps_watchlist_at_and_InWatchlist_returns_true()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var curation = new CurationRepository(temp.Db);
        var now = DateTimeOffset.UtcNow;

        curation.SetWatchlist(videoId, true, now);

        curation.InWatchlist(videoId).ShouldBeTrue();

        // Verify watchlist_at was actually written
        using var conn = temp.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT watchlist_at FROM videos WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", videoId);
        var stored = cmd.ExecuteScalar() as string;
        stored.ShouldNotBeNullOrEmpty();
        DateTimeOffset.Parse(stored!).ShouldBe(now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SetWatchlist_false_clears_flag_and_nulls_watchlist_at()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var curation = new CurationRepository(temp.Db);

        curation.SetWatchlist(videoId, true, DateTimeOffset.UtcNow);
        curation.SetWatchlist(videoId, false, DateTimeOffset.UtcNow);

        curation.InWatchlist(videoId).ShouldBeFalse();

        // Verify watchlist_at was nulled
        using var conn = temp.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT watchlist_at FROM videos WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.ExecuteScalar().ShouldBe(DBNull.Value);
    }

    // ─── GetWatchlist ───────────────────────────────────────────────────────

    [Fact]
    public void GetWatchlist_returns_only_in_watchlist_and_not_missing_videos()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var ser = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var vid1 = lib.UpsertVideo(ser, @"C:\V\S\a.mp4", 1, ".mp4");
        var vid2 = lib.UpsertVideo(ser, @"C:\V\S\b.mp4", 2, ".mp4");
        var vid3 = lib.UpsertVideo(ser, @"C:\V\S\c.mp4", 3, ".mp4");
        var curation = new CurationRepository(temp.Db);
        var t = DateTimeOffset.UtcNow;

        curation.SetWatchlist(vid1, true, t);
        // vid2: not in watchlist
        curation.SetWatchlist(vid3, true, t);
        // Mark vid3 as missing
        using (var conn = temp.Db.Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE videos SET missing=1 WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", vid3);
            cmd.ExecuteNonQuery();
        }

        var results = curation.GetWatchlist(50);

        results.Count.ShouldBe(1);
        results[0].VideoId.ShouldBe(vid1);
    }

    [Fact]
    public void GetWatchlist_respects_limit()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var ser = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var curation = new CurationRepository(temp.Db);
        var t = DateTimeOffset.UtcNow;

        for (var i = 1; i <= 5; i++)
        {
            var vid = lib.UpsertVideo(ser, $@"C:\V\S\ep{i:D2}.mp4", i, ".mp4");
            curation.SetWatchlist(vid, true, t.AddSeconds(i));
        }

        var results = curation.GetWatchlist(3);

        results.Count.ShouldBe(3);
    }

    [Fact]
    public void GetWatchlist_orders_by_watchlist_at_desc()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var ser = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var vid1 = lib.UpsertVideo(ser, @"C:\V\S\a.mp4", 1, ".mp4");
        var vid2 = lib.UpsertVideo(ser, @"C:\V\S\b.mp4", 2, ".mp4");
        var curation = new CurationRepository(temp.Db);
        var earlier = DateTimeOffset.UtcNow.AddHours(-1);
        var later = DateTimeOffset.UtcNow;

        // vid2 added later → should sort first
        curation.SetWatchlist(vid1, true, earlier);
        curation.SetWatchlist(vid2, true, later);

        var results = curation.GetWatchlist(50);

        results[0].VideoId.ShouldBe(vid2);
        results[1].VideoId.ShouldBe(vid1);
    }

    [Fact]
    public void GetFavorites_returns_items_in_recency_order()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var ser = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var vid1 = lib.UpsertVideo(ser, @"C:\V\S\a.mp4", 1, ".mp4");
        var vid2 = lib.UpsertVideo(ser, @"C:\V\S\b.mp4", 2, ".mp4");
        var curation = new CurationRepository(temp.Db);

        curation.SetFavorite(vid1, true);
        curation.SetFavorite(vid2, true);
        // Set a more recent resume_updated_at on vid1 so it sorts first
        lib.SetResumePosition(vid1, 10.0);

        var results = curation.GetFavorites(50);

        // vid1 has a resume_updated_at set, so it sorts above vid2 (which has none)
        results[0].VideoId.ShouldBe(vid1);
        results[1].VideoId.ShouldBe(vid2);
    }
}
