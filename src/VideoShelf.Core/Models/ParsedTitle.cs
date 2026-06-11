namespace VideoShelf.Core.Models;

/// <summary>Result of parsing a filename stem: a normalized base title and an optional episode number.</summary>
public sealed record ParsedTitle(string BaseTitle, int? EpisodeNumber);
