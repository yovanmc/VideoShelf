namespace VideoShelf.Core.Discovery;

public static class DiscoveryScoring
{
    /// <summary>Exponential recency decay: 1.0 at age 0, halves every <paramref name="halfLifeDays"/>. Future events clamp to 1.0.</summary>
    public static double RecencyWeight(DateTimeOffset eventTime, DateTimeOffset now, double halfLifeDays)
    {
        var ageDays = (now - eventTime).TotalDays;
        if (ageDays <= 0) return 1.0;
        return Math.Pow(0.5, ageDays / halfLifeDays);
    }

    /// <summary>Sum of recency weights per tag across the watch history.</summary>
    public static IReadOnlyDictionary<string, double> BuildTagAffinity(
        IEnumerable<WatchedTag> events, DateTimeOffset now, double halfLifeDays)
    {
        var affinity = new Dictionary<string, double>();
        foreach (var e in events)
        {
            var w = RecencyWeight(e.WatchedAt, now, halfLifeDays);
            affinity[e.Tag] = affinity.TryGetValue(e.Tag, out var cur) ? cur + w : w;
        }
        return affinity;
    }

    /// <summary>
    /// Section relevance: summed affinity of overlapping tags, modulated toward mostly-unwatched content.
    /// Zero when there is no tag overlap.
    /// </summary>
    public static double ScoreSection(
        IReadOnlyList<string> sectionTags, IReadOnlyDictionary<string, double> affinity,
        int unwatchedCount, int episodeCount)
    {
        double overlap = 0;
        foreach (var t in sectionTags)
            if (affinity.TryGetValue(t, out var a)) overlap += a;
        if (overlap <= 0) return 0;
        var unwatchedRatio = episodeCount <= 0 ? 0 : (double)unwatchedCount / episodeCount;
        return overlap * (0.5 + 0.5 * unwatchedRatio);
    }
}
