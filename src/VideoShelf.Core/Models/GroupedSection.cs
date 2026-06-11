using System.Collections.Generic;

namespace VideoShelf.Core.Models;

/// <summary>One episode within a grouped series: the original file name plus its resolved episode number.</summary>
public sealed record GroupedEpisode(string FileName, int EpisodeNumber);

/// <summary>A series (or standalone) detected within a section.</summary>
public sealed record GroupedSeries(string BaseTitle, bool IsStandalone, IReadOnlyList<GroupedEpisode> Episodes);

/// <summary>All series/standalones detected within a single section folder.</summary>
public sealed record GroupedSection(IReadOnlyList<GroupedSeries> Series);
