using Shouldly;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests;

public class ResumePolicyTests
{
    [Fact]
    public void ShouldSave_false_before_interval_elapses()
    {
        var p = new ResumePolicy();
        p.ShouldSaveOnTick(lastSavedAtSeconds: 10.0, currentSeconds: 13.0).ShouldBeFalse();
    }

    [Fact]
    public void ShouldSave_true_once_interval_elapses()
    {
        var p = new ResumePolicy();
        p.ShouldSaveOnTick(lastSavedAtSeconds: 10.0, currentSeconds: 15.0).ShouldBeTrue();
    }

    [Fact]
    public void IsNearEnd_true_within_completion_window()
    {
        var p = new ResumePolicy();
        // 98% through a 100s video → treated as finished.
        p.IsNearEnd(currentSeconds: 98.0, lengthSeconds: 100.0).ShouldBeTrue();
    }

    [Fact]
    public void IsNearEnd_false_mid_video()
    {
        var p = new ResumePolicy();
        p.IsNearEnd(currentSeconds: 40.0, lengthSeconds: 100.0).ShouldBeFalse();
    }

    [Fact]
    public void IsNearEnd_false_when_length_unknown()
    {
        var p = new ResumePolicy();
        p.IsNearEnd(currentSeconds: 40.0, lengthSeconds: 0.0).ShouldBeFalse();
    }

    [Fact]
    public void ShouldOfferResume_false_for_trivial_position()
    {
        var p = new ResumePolicy();
        // < the minimum meaningful resume position.
        p.ShouldOfferResume(savedSeconds: 2.0, lengthSeconds: 100.0).ShouldBeFalse();
    }

    [Fact]
    public void ShouldOfferResume_false_when_saved_is_near_end()
    {
        var p = new ResumePolicy();
        p.ShouldOfferResume(savedSeconds: 99.0, lengthSeconds: 100.0).ShouldBeFalse();
    }

    [Fact]
    public void ShouldOfferResume_true_for_meaningful_midpoint()
    {
        var p = new ResumePolicy();
        p.ShouldOfferResume(savedSeconds: 50.0, lengthSeconds: 100.0).ShouldBeTrue();
    }
}
