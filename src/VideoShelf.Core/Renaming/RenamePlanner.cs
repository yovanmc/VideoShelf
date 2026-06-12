// src/VideoShelf.Core/Renaming/RenamePlanner.cs
using System;
using System.Collections.Generic;
using System.IO;
using VideoShelf.Core.Models;

namespace VideoShelf.Core.Renaming;

/// <summary>Pure planner: given a series' videos and a proposed file name per video id, resolves absolute
/// target paths and flags conflicts (missing source, occupied target, duplicate targets, invalid name).</summary>
public sealed class RenamePlanner(IFileSystem fs)
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    /// <summary>Builds a plan. A video id absent from <paramref name="proposedNames"/> keeps its current name.</summary>
    public RenamePlan BuildPlan(IReadOnlyList<Video> videos, IReadOnlyDictionary<long, string> proposedNames)
    {
        var rows = new List<(RenameItem Item, bool Invalid)>(videos.Count);
        var targetCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var v in videos)
        {
            var dir = Path.GetDirectoryName(v.FilePath) ?? "";
            var proposed = (proposedNames.TryGetValue(v.Id, out var name) ? name : Path.GetFileName(v.FilePath))?.Trim() ?? "";
            var invalid = proposed.Length > 0 && proposed.IndexOfAny(InvalidChars) >= 0;
            var newPath = (proposed.Length == 0 || invalid) ? v.FilePath : Path.Combine(dir, proposed);

            rows.Add((new RenameItem(v.Id, v.EpisodeNo, v.FilePath, newPath, RenameItemStatus.Ready), invalid));
            if (!invalid && !PathsEqual(newPath, v.FilePath))
                targetCounts[newPath] = targetCounts.GetValueOrDefault(newPath) + 1;
        }

        var result = new List<RenameItem>(rows.Count);
        foreach (var (item, invalid) in rows)
            result.Add(item with { Status = Classify(item, invalid, targetCounts) });
        return new RenamePlan(result);
    }

    private RenameItemStatus Classify(RenameItem row, bool invalid, Dictionary<string, int> targetCounts)
    {
        if (invalid) return RenameItemStatus.InvalidName;
        if (PathsEqual(row.OldPath, row.NewPath)) return RenameItemStatus.Unchanged;
        if (!fs.FileExists(row.OldPath)) return RenameItemStatus.SourceMissing;
        if (targetCounts.GetValueOrDefault(row.NewPath) > 1) return RenameItemStatus.DuplicateTarget;
        if (fs.FileExists(row.NewPath)) return RenameItemStatus.TargetExists;
        return RenameItemStatus.Ready;
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
