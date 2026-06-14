using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VideoShelf.Core.Models;

namespace VideoShelf.Core.Naming;

/// <summary>
/// An override row from the <c>grouping_overrides</c> table, keyed by bare file name (not full path).
/// The repository returns dictionaries keyed by <c>Path.GetFileName(file_path)</c> so the grouper
/// can look up overrides without knowing the section's root path.
/// </summary>
/// <param name="FilePath">The full file path stored in the DB (used for identity; bare-filename keying
/// is the responsibility of the repository that returns this in a dictionary).</param>
/// <param name="OverrideBaseTitle">When non-null, replaces the parsed base title before grouping.</param>
/// <param name="OverrideEpisodeNo">When non-null, replaces the parsed episode number before sorting.</param>
public sealed record GroupingOverride(string FilePath, string? OverrideBaseTitle, int? OverrideEpisodeNo);

/// <summary>
/// Groups a section's file names into series and standalones using TitleParser.
/// Files sharing a base title (case-insensitive) form one series; a group of one is a standalone.
/// Within a series: the unnumbered file is episode 1; numbered files keep their number; ties
/// break by natural filename order.
/// </summary>
public static class SectionGrouper
{
    /// <summary>
    /// Groups file names with no overrides applied. Delegates to the two-argument overload
    /// with an empty dictionary so all existing callers compile unchanged.
    /// </summary>
    public static GroupedSection Group(IEnumerable<string> fileNames)
        => Group(fileNames, new Dictionary<string, GroupingOverride>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Groups file names, applying per-file overrides before grouping and sorting.
    /// <para>
    /// Override semantics (all expressed as <see cref="GroupingOverride"/> rows — no new table):
    /// <list type="bullet">
    ///   <item><b>Split</b> a video out of series X into series Y: set <c>OverrideBaseTitle='Y'</c>.</item>
    ///   <item><b>Merge</b> series B into series A: set <c>OverrideBaseTitle='&lt;A.base_title&gt;'</c>
    ///         for every file in B.</item>
    ///   <item><b>Manual order</b>: set <c>OverrideEpisodeNo=N</c> for the file.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Keying: <paramref name="overridesByFileName"/> is keyed by <b>bare file name</b>
    /// (i.e. <c>Path.GetFileName(file_path)</c>). The repository method
    /// <c>GetGroupingOverrides(sectionId)</c> is responsible for this projection so the
    /// grouper does not need to know section root paths.
    /// </para>
    /// </summary>
    public static GroupedSection Group(
        IEnumerable<string> fileNames,
        IReadOnlyDictionary<string, GroupingOverride> overridesByFileName)
    {
        var natural = new NaturalComparer();

        var groups = fileNames
            .Select(f =>
            {
                var parsed = TitleParser.Parse(Path.GetFileNameWithoutExtension(f));
                // Apply overrides: replace base title and/or episode number when present.
                if (overridesByFileName.TryGetValue(f, out var ov))
                {
                    var baseTitle = ov.OverrideBaseTitle ?? parsed.BaseTitle;
                    var episodeNo = ov.OverrideEpisodeNo ?? parsed.EpisodeNumber;
                    parsed = new VideoShelf.Core.Models.ParsedTitle(baseTitle, episodeNo);
                }
                return (File: f, Parsed: parsed);
            })
            .GroupBy(x => x.Parsed.BaseTitle, StringComparer.OrdinalIgnoreCase);

        var series = new List<GroupedSeries>();
        foreach (var group in groups)
        {
            var items = group.ToList();
            var isStandalone = items.Count == 1;

            var ordered = items
                .OrderBy(x => x.Parsed.EpisodeNumber ?? 1)
                .ThenBy(x => x.File, natural)
                .ToList();

            var episodes = new List<GroupedEpisode>();
            for (var i = 0; i < ordered.Count; i++)
            {
                var number = ordered[i].Parsed.EpisodeNumber ?? (i + 1);
                episodes.Add(new GroupedEpisode(ordered[i].File, number));
            }

            // Use the first item's base title (preserves the casing seen first, or the override casing).
            series.Add(new GroupedSeries(items[0].Parsed.BaseTitle, isStandalone, episodes));
        }

        return new GroupedSection(series);
    }
}
