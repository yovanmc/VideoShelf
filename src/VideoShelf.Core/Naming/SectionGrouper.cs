using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VideoShelf.Core.Models;

namespace VideoShelf.Core.Naming;

/// <summary>
/// Groups a section's file names into series and standalones using TitleParser.
/// Files sharing a base title (case-insensitive) form one series; a group of one is a standalone.
/// Within a series: the unnumbered file is episode 1; numbered files keep their number; ties
/// break by natural filename order.
/// </summary>
public static class SectionGrouper
{
    public static GroupedSection Group(IEnumerable<string> fileNames)
    {
        var natural = new NaturalComparer();

        var groups = fileNames
            .Select(f => (File: f, Parsed: TitleParser.Parse(Path.GetFileNameWithoutExtension(f))))
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

            // Use the first item's base title (preserves the casing seen first).
            series.Add(new GroupedSeries(items[0].Parsed.BaseTitle, isStandalone, episodes));
        }

        return new GroupedSection(series);
    }
}
