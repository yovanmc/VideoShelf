namespace VideoShelf.App.Scale;

/// <summary>Deterministic synthetic-library plan. No I/O — turned into rows by the seeder.</summary>
public sealed record StressLibrarySpec(IReadOnlyList<StressCreator> Creators)
{
    // 25 first names × 25 last names = 625 unique combos — enough for 500 creators with no suffix.
    private static readonly string[] FirstNames =
    [
        "Alice", "Ben", "Carlos", "Diana", "Ethan",
        "Fiona", "Gabriel", "Hannah", "Ivan", "Julia",
        "Kevin", "Laura", "Marcus", "Natalie", "Oscar",
        "Priya", "Quinn", "Rachel", "Samuel", "Tara",
        "Umar", "Violet", "Will", "Xena", "Yasmin"
    ];

    private static readonly string[] LastNames =
    [
        "Adams", "Brooks", "Chen", "Davis", "Evans",
        "Foster", "Garcia", "Harris", "Iyer", "Jones",
        "Kim", "Lee", "Martin", "Nguyen", "Ortiz",
        "Patel", "Quinn", "Rivera", "Smith", "Torres",
        "Ueda", "Vargas", "Wang", "Xavier", "Young"
    ];

    /// <summary>
    /// Returns a deterministic person name for creator index <paramref name="c"/>.
    /// Cycles through FirstNames × LastNames; appends a numeric suffix only if the
    /// full combo count is exceeded (> 625 creators).
    /// </summary>
    private static string CreatorName(int c)
    {
        int f = FirstNames.Length;   // 25
        int l = LastNames.Length;    // 25
        int combos = f * l;          // 625

        int cycle = c / combos;
        int idx   = c % combos;
        string first = FirstNames[idx % f];
        string last  = LastNames[idx / f % l];
        return cycle == 0 ? $"{first} {last}" : $"{first} {last} {cycle + 1}";
    }

    public static StressLibrarySpec Generate(int creators, int biggestSeries, int totalVideos, int seed)
    {
        if (creators <= 0 || biggestSeries <= 0 || totalVideos < creators)
            throw new ArgumentException("totalVideos must be >= creators and counts must be positive.");

        var rng = new Random(seed);
        var list = new List<StressCreator>(creators);

        // Distribute series so exactly one creator hits `biggestSeries`; the rest taper.
        for (int c = 0; c < creators; c++)
        {
            int seriesCount = c == 0 ? biggestSeries : 1 + rng.Next(0, Math.Max(1, biggestSeries / 8));
            var series = new List<StressSeries>(seriesCount);
            for (int s = 0; s < seriesCount; s++)
                series.Add(new StressSeries($"C{c:D4}S{s:D3}", new List<StressEpisode>()));
            list.Add(new StressCreator(CreatorName(c), series));
        }

        // Spread the remaining episodes round-robin across all series until totalVideos is hit.
        int placed = 0;
        var flatSeries = list.SelectMany(c => c.Series).ToList();
        while (placed < totalVideos)
        {
            var s = flatSeries[placed % flatSeries.Count];
            int epNo = s.Episodes.Count + 1;
            s.Episodes.Add(new StressEpisode(epNo, $"{s.BaseTitle}/{s.BaseTitle} {epNo:D3}.mp4"));
            placed++;
        }
        return new StressLibrarySpec(list);
    }
}

public sealed record StressCreator(string Name, List<StressSeries> Series);
public sealed record StressSeries(string BaseTitle, List<StressEpisode> Episodes)
{
    public int EpisodeCount => Episodes.Count;
}
public sealed record StressEpisode(int EpisodeNo, string RelativePath);
