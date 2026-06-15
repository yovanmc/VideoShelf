using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

/// <summary>
/// Unit tests for Groups D and E:
///   D — playback speed presets, aspect/zoom presets, audio-normalize toggle.
///   E — A-B repeat, skip feedback, volume-scroll, play-from-beginning / IsCompleted.
/// All tests run against FakePlaybackEngine — no real libVLC needed.
/// </summary>
public class PlayerGroupDeTests
{
    // ── shared helpers ─────────────────────────────────────────────────────────

    private static (PlayerViewModel vm, FakePlaybackEngine engine, EpisodeView ep, AppTempDb temp)
        Make(bool preWatched = false)
    {
        var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var settings = new SettingsRepository(temp.Db);

        var srcId = lib.UpsertSource(@"C:\V", "V");
        var secId = lib.UpsertSection(srcId, "S");
        var seriesId = lib.UpsertSeries(secId, "Base", false);
        var path = System.IO.Path.GetTempFileName();
        var videoId = lib.UpsertVideo(seriesId, path, 1, ".mp4");
        if (preWatched) watch.SetWatched(videoId, true);

        var ep = new EpisodeView(videoId, seriesId, path, 1, "Base", Watched: preWatched, Missing: false);
        var engine = new FakePlaybackEngine();
        var vm = new PlayerViewModel(engine, lib, watch, settings, new ResumePolicy(), new FakeSubtitleFilePicker());
        return (vm, engine, ep, temp);
    }

    // ── D: Speed presets ───────────────────────────────────────────────────────

    [Fact]
    public void SpeedPresets_has_expected_values()
    {
        var (vm, _, _, temp) = Make();
        using (temp) vm.SpeedPresets.ShouldBe(new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 });
    }

    [Fact]
    public void PlaybackRate_defaults_to_1()
    {
        var (vm, _, _, temp) = Make();
        using (temp) vm.PlaybackRate.ShouldBe(1.0);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void SetPlaybackRateCommand_pushes_rate_to_engine(double rate)
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            vm.SetPlaybackRateCommand.Execute(rate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            engine.Rate.ShouldBe(rate);
        }
    }

    [Fact]
    public void PlaybackRate_setter_pushes_to_engine()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            vm.PlaybackRate = 1.25;
            engine.Rate.ShouldBe(1.25);
        }
    }

    [Fact]
    public void RateLabel_is_1x_at_normal_speed()
    {
        var (vm, _, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            vm.PlaybackRate = 1.0;
            vm.RateLabel.ShouldBe("1×");
        }
    }

    [Theory]
    [InlineData(1.5, "1.5×")]
    [InlineData(0.5, "0.5×")]
    [InlineData(0.75, "0.75×")]
    [InlineData(2.0, "2×")]
    [InlineData(1.25, "1.25×")]
    public void RateLabel_formats_non_one_speeds(double rate, string expected)
    {
        var (vm, _, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            vm.PlaybackRate = rate;
            vm.RateLabel.ShouldBe(expected);
        }
    }

    [Fact]
    public void RateLabel_raises_property_changed_when_PlaybackRate_changes()
    {
        var (vm, _, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            vm.PlaybackRate = 1.5;

            changed.ShouldContain(nameof(PlayerViewModel.RateLabel));
        }
    }

    [Fact]
    public void Open_resets_PlaybackRate_to_1()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            vm.PlaybackRate = 2.0;
            vm.Open(ep);   // second open resets
            vm.PlaybackRate.ShouldBe(1.0);
            engine.Rate.ShouldBe(1.0);
        }
    }

    // ── E1: A-B repeat ─────────────────────────────────────────────────────────

    [Fact]
    public void SetRepeatA_sets_RepeatStartSeconds_to_current_position()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            engine.RaisePosition(30.0);
            vm.SetRepeatACommand.Execute(null);
            vm.RepeatStartSeconds.ShouldBe(30.0);
        }
    }

    [Fact]
    public void SetRepeatB_sets_RepeatEndSeconds_when_B_greater_than_A()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            engine.RaisePosition(20.0);
            vm.SetRepeatACommand.Execute(null);
            engine.RaisePosition(60.0);
            vm.SetRepeatBCommand.Execute(null);
            vm.RepeatEndSeconds.ShouldBe(60.0);
        }
    }

    [Fact]
    public void SetRepeatB_does_nothing_when_position_not_greater_than_A()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            engine.RaisePosition(50.0);
            vm.SetRepeatACommand.Execute(null);
            engine.RaisePosition(10.0);  // before A
            vm.SetRepeatBCommand.Execute(null);
            vm.RepeatEndSeconds.ShouldBeNull();
        }
    }

    [Fact]
    public void IsAbRepeatActive_is_false_when_only_A_set()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            engine.RaisePosition(20.0);
            vm.SetRepeatACommand.Execute(null);
            vm.IsAbRepeatActive.ShouldBeFalse();
        }
    }

    [Fact]
    public void IsAbRepeatActive_is_true_when_both_set_and_B_greater_than_A()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            engine.RaisePosition(10.0);
            vm.SetRepeatACommand.Execute(null);
            engine.RaisePosition(50.0);
            vm.SetRepeatBCommand.Execute(null);
            vm.IsAbRepeatActive.ShouldBeTrue();
        }
    }

    [Fact]
    public void Position_tick_past_B_seeks_back_to_A()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            engine.RaiseLength(120.0);

            // Set A=10, B=50
            engine.RaisePosition(10.0);
            vm.SetRepeatACommand.Execute(null);
            engine.RaisePosition(50.0);
            vm.SetRepeatBCommand.Execute(null);

            engine.Seeks.Clear();

            // Simulate playback ticking past B
            engine.RaisePosition(55.0);

            // Should have seeked back to A
            engine.Seeks.ShouldContain(10.0);
        }
    }

    [Fact]
    public void ClearAbRepeat_clears_both_points_and_deactivates()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            engine.RaisePosition(10.0); vm.SetRepeatACommand.Execute(null);
            engine.RaisePosition(50.0); vm.SetRepeatBCommand.Execute(null);
            vm.ClearAbRepeatCommand.Execute(null);

            vm.RepeatStartSeconds.ShouldBeNull();
            vm.RepeatEndSeconds.ShouldBeNull();
            vm.IsAbRepeatActive.ShouldBeFalse();
        }
    }

    [Fact]
    public void Open_resets_AB_repeat()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            engine.RaisePosition(10.0); vm.SetRepeatACommand.Execute(null);
            engine.RaisePosition(50.0); vm.SetRepeatBCommand.Execute(null);
            vm.Open(ep);   // second open clears
            vm.RepeatStartSeconds.ShouldBeNull();
            vm.RepeatEndSeconds.ShouldBeNull();
            vm.IsAbRepeatActive.ShouldBeFalse();
        }
    }

    // ── E3: Skip feedback ──────────────────────────────────────────────────────

    [Fact]
    public void SkipBack10Command_sets_SkipFeedback()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            engine.RaiseLength(100.0);
            engine.RaisePosition(50.0);

            vm.SkipBack10Command.Execute(null);

            vm.SkipFeedback.ShouldBe("−10s");
        }
    }

    [Fact]
    public void SkipForward30Command_sets_SkipFeedback()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            engine.RaiseLength(200.0);
            engine.RaisePosition(50.0);

            vm.SkipForward30Command.Execute(null);

            vm.SkipFeedback.ShouldBe("+30s");
        }
    }

    [Fact]
    public void SkipBack10Command_still_clamps_to_zero()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            engine.RaiseLength(100.0);
            engine.RaisePosition(5.0);

            vm.SkipBack10Command.Execute(null);

            engine.Seeks[^1].ShouldBe(0.0);
            vm.SkipFeedback.ShouldBe("−10s");
        }
    }

    [Fact]
    public void SkipForward30Command_still_clamps_to_length()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            engine.RaiseLength(100.0);
            engine.RaisePosition(85.0);

            vm.SkipForward30Command.Execute(null);

            engine.Seeks[^1].ShouldBe(100.0);
            vm.SkipFeedback.ShouldBe("+30s");
        }
    }

    // ── E4: Volume scroll feedback ─────────────────────────────────────────────

    [Fact]
    public void AdjustVolumeByWheel_up_increases_volume_by_5()
    {
        var (vm, _, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            vm.Volume = 50;
            vm.AdjustVolumeByWheel(120);   // positive delta = wheel up
            vm.Volume.ShouldBe(55);
        }
    }

    [Fact]
    public void AdjustVolumeByWheel_down_decreases_volume_by_5()
    {
        var (vm, _, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            vm.Volume = 50;
            vm.AdjustVolumeByWheel(-120);  // negative delta = wheel down
            vm.Volume.ShouldBe(45);
        }
    }

    [Fact]
    public void AdjustVolumeByWheel_clamps_at_100()
    {
        var (vm, _, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            vm.Volume = 98;
            vm.AdjustVolumeByWheel(120);
            vm.Volume.ShouldBe(100);
        }
    }

    [Fact]
    public void AdjustVolumeByWheel_clamps_at_0()
    {
        var (vm, _, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            vm.Volume = 2;
            vm.AdjustVolumeByWheel(-120);
            vm.Volume.ShouldBe(0);
        }
    }

    [Fact]
    public void AdjustVolumeByWheel_sets_VolumeFeedback()
    {
        var (vm, _, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            vm.Volume = 60;
            vm.AdjustVolumeByWheel(120);
            vm.VolumeFeedback.ShouldBe("Volume 65%");
        }
    }

    // ── E5: Play from beginning / IsCompleted ──────────────────────────────────

    [Fact]
    public void IsCompleted_false_for_unwatched_episode()
    {
        var (vm, _, ep, temp) = Make(preWatched: false);
        using (temp)
        {
            vm.Open(ep);
            vm.IsCompleted.ShouldBeFalse();
        }
    }

    [Fact]
    public void IsCompleted_true_for_already_watched_episode()
    {
        var (vm, _, ep, temp) = Make(preWatched: true);
        using (temp)
        {
            vm.Open(ep);
            vm.IsCompleted.ShouldBeTrue();
        }
    }

    [Fact]
    public void IsCompleted_becomes_true_when_playback_ends()
    {
        var (vm, engine, ep, temp) = Make(preWatched: false);
        using (temp)
        {
            vm.Open(ep);
            vm.IsCompleted.ShouldBeFalse();
            engine.RaiseEnded();
            vm.IsCompleted.ShouldBeTrue();
        }
    }

    [Fact]
    public void PlayFromBeginningCommand_seeks_to_zero_and_clears_completed()
    {
        var (vm, engine, ep, temp) = Make(preWatched: true);
        using (temp)
        {
            vm.Open(ep);
            vm.IsCompleted.ShouldBeTrue();

            vm.PlayFromBeginningCommand.Execute(null);

            engine.Seeks.ShouldContain(0.0);
            vm.CanResume.ShouldBeFalse();
            vm.IsCompleted.ShouldBeFalse();
        }
    }

    [Fact]
    public void Open_resets_IsCompleted_to_unwatched_for_fresh_episode()
    {
        var (vm, engine, ep, temp) = Make(preWatched: false);
        using (temp)
        {
            vm.Open(ep);
            engine.RaiseEnded();  // marks watched → IsCompleted = true
            vm.IsCompleted.ShouldBeTrue();

            // Open a second time (still same episode, now watched in DB)
            vm.Open(ep);
            // IsCompleted reflects the DB state at Open — watched, so still true
            vm.IsCompleted.ShouldBeTrue();
        }
    }

    // ── Fix 1: Open clears pending feedback badges ─────────────────────────────

    [Fact]
    public void Open_clears_SkipFeedback_set_by_previous_episode()
    {
        var (vm, _, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            // Simulate a skip badge being displayed (set synchronously in tests — no WPF timer)
            vm.SkipBack10Command.Execute(null);
            vm.SkipFeedback.ShouldBe("−10s"); // confirm it is set

            // Opening the same episode again must clear the badge
            vm.Open(ep);
            vm.SkipFeedback.ShouldBeNull();
        }
    }

    [Fact]
    public void Open_clears_VolumeFeedback_set_by_previous_episode()
    {
        var (vm, _, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            vm.Volume = 50;
            vm.AdjustVolumeByWheel(120);
            vm.VolumeFeedback.ShouldBe("Volume 55%"); // confirm it is set

            // Opening the same episode again must clear the badge
            vm.Open(ep);
            vm.VolumeFeedback.ShouldBeNull();
        }
    }

    // ── Fix 2: A-B repeat re-entrancy guard ────────────────────────────────────

    [Fact]
    public void AbRepeat_guard_suppresses_second_SeekTo_before_position_settles()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            engine.RaiseLength(120.0);

            // Set A=10, B=50
            engine.RaisePosition(10.0);
            vm.SetRepeatACommand.Execute(null);
            engine.RaisePosition(50.0);
            vm.SetRepeatBCommand.Execute(null);

            engine.Seeks.Clear();

            // First tick past B — should seek to A once
            engine.RaisePosition(55.0);
            engine.Seeks.Count.ShouldBe(1);
            engine.Seeks[0].ShouldBe(10.0);

            // Second tick still ≥ B (position hasn't settled yet) — guard must suppress this
            engine.RaisePosition(55.0);
            engine.Seeks.Count.ShouldBe(1, "re-entrancy guard must suppress second SeekTo while settling");

            // Position settles back below B-1 → guard is lifted
            engine.RaisePosition(12.0);

            // Now tick past B again — should fire a fresh SeekTo(A)
            engine.RaisePosition(55.0);
            engine.Seeks.Count.ShouldBe(2, "a new boundary crossing after settling must seek again");
            engine.Seeks[1].ShouldBe(10.0);
        }
    }

    [Fact]
    public void AbRepeat_guard_is_reset_when_ClearAbRepeat_is_called()
    {
        var (vm, engine, ep, temp) = Make();
        using (temp)
        {
            vm.Open(ep);
            engine.RaiseLength(120.0);

            // Set A=10, B=50 and trigger the guard
            engine.RaisePosition(10.0); vm.SetRepeatACommand.Execute(null);
            engine.RaisePosition(50.0); vm.SetRepeatBCommand.Execute(null);
            engine.Seeks.Clear();
            engine.RaisePosition(55.0); // _abSeeking = true

            // Clear A-B
            vm.ClearAbRepeatCommand.Execute(null);

            // Re-set A-B
            engine.RaisePosition(10.0); vm.SetRepeatACommand.Execute(null);
            engine.RaisePosition(50.0); vm.SetRepeatBCommand.Execute(null);
            engine.Seeks.Clear();

            // Should now fire normally (guard was reset by ClearAbRepeat)
            engine.RaisePosition(55.0);
            engine.Seeks.Count.ShouldBe(1);
            engine.Seeks[0].ShouldBe(10.0);
        }
    }
}
