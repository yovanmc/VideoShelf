using System;
using System.Collections.Generic;
using System.Linq;
using VideoShelf.Core.Models;

namespace VideoShelf.Core.Scanning;

/// <summary>
/// M18-F: Pure matcher for auto-find relink. Given a missing video and a list of
/// candidate file infos, finds the single unambiguous match by size_bytes
/// (and rounded duration if both are known). Returns null if 0 or >1 matches.
/// </summary>
public static class RelinkMatcher
{
    /// <param name="missing">The missing video row (carries SizeBytes / DurationSeconds if previously probed).</param>
    /// <param name="candidates">
    /// File infos from a directory walk: path, size in bytes, optional duration.
    /// Must not include the missing video's own path (caller responsibility).
    /// </param>
    /// <returns>
    /// The single matching candidate path when exactly one file matches; otherwise null.
    /// </returns>
    public static string? FindCandidate(MissingVideo missing, IReadOnlyList<CandidateFile> candidates)
    {
        if (missing.SizeBytes is null || missing.SizeBytes <= 0)
            return null; // no size info — cannot auto-match

        var matched = candidates
            .Where(c => c.SizeBytes == missing.SizeBytes.Value)
            .ToList();

        // If both missing and candidate carry duration info, narrow further.
        if (missing.DurationSeconds is not null)
        {
            int missingRounded = (int)Math.Round(missing.DurationSeconds.Value);
            var withDuration = matched
                .Where(c => c.DurationSeconds is not null &&
                            (int)Math.Round(c.DurationSeconds.Value) == missingRounded)
                .ToList();

            // Only use the duration-narrowed set when it is non-empty; otherwise
            // fall back to size-only matches (duration may not be probed on all candidates).
            if (withDuration.Count > 0)
                matched = withDuration;
        }

        return matched.Count == 1 ? matched[0].FilePath : null;
    }
}

/// <summary>A scanned file candidate for auto-find relink.</summary>
public sealed record CandidateFile(
    string FilePath,
    long SizeBytes,
    double? DurationSeconds = null);
