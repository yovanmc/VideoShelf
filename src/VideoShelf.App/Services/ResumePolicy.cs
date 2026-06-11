namespace VideoShelf.App.Services;

/// <summary>Pure timing/threshold rules for resume persistence and completion detection.
/// Kept separate from the player view-model so the rules are unit-testable without a timer.</summary>
public sealed class ResumePolicy
{
    /// <summary>Persist the resume position at most this often during playback.</summary>
    public double SaveIntervalSeconds { get; init; } = 5.0;

    /// <summary>Fraction of the media considered "finished" (auto-mark watched, clear resume).</summary>
    public double CompletionFraction { get; init; } = 0.97;

    /// <summary>Positions below this are too trivial to bother resuming from.</summary>
    public double MinResumeSeconds { get; init; } = 5.0;

    public bool ShouldSaveOnTick(double lastSavedAtSeconds, double currentSeconds)
        => currentSeconds - lastSavedAtSeconds >= SaveIntervalSeconds;

    public bool IsNearEnd(double currentSeconds, double lengthSeconds)
        => lengthSeconds > 0 && currentSeconds >= lengthSeconds * CompletionFraction;

    public bool ShouldOfferResume(double savedSeconds, double lengthSeconds)
        => savedSeconds >= MinResumeSeconds && !IsNearEnd(savedSeconds, lengthSeconds);
}
