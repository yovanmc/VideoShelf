namespace VideoShelf.Core.Models;
public sealed record Series(long Id, long SectionId, string BaseTitle, string SortKey, bool IsStandalone);
