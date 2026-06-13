namespace VideoShelf.Core.Search;

/// <summary>
/// Pure static fuzzy ranker for the command palette.
/// Scores a (query, candidateLabel) pair on [0, 1] — returns 0 for a non-match.
/// Rules (deterministic, no I/O):
///   1. Case-insensitive.
///   2. Non-subsequence → 0 (excluded).
///   3. Exact match → 1.0.
///   4. Prefix match (candidate starts with query) → 0.9.
///   5. Word-boundary match (a word inside candidate starts with query) → 0.75.
///   6. General subsequence match → linear fraction of characters consumed:
///      (matched chars / candidate length).
/// Higher score = better match.
/// </summary>
public static class PaletteRanker
{
    /// <summary>Returns a score in [0, 1]. 0 means no match (should be excluded).</summary>
    public static double Score(string query, string candidateLabel)
    {
        if (string.IsNullOrEmpty(query))
            return 0;

        var q = query.Trim();
        if (q.Length == 0) return 0;

        var c = candidateLabel ?? string.Empty;

        // Use StringComparison.OrdinalIgnoreCase throughout.
        if (string.Equals(q, c, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        if (c.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            return 0.9;

        if (StartsWithWordBoundary(q, c))
            return 0.75;

        // Subsequence check: q chars must appear in c in order.
        int qi = 0, ci = 0;
        while (qi < q.Length && ci < c.Length)
        {
            if (char.ToUpperInvariant(q[qi]) == char.ToUpperInvariant(c[ci]))
                qi++;
            ci++;
        }
        if (qi < q.Length)
            return 0; // not a subsequence

        // Score proportional to density — reward shorter candidates with same subsequence.
        // Use (matchedChars / candidateLength) bounded to avoid returning too close to 0.9.
        double rawFraction = (double)q.Length / c.Length;
        // Cap at 0.70 so subsequence < word-boundary < prefix.
        return Math.Min(0.70, 0.15 + rawFraction * 0.55);
    }

    /// <summary>Returns true if the candidate has a word that starts with <paramref name="query"/>.</summary>
    private static bool StartsWithWordBoundary(string query, string candidate)
    {
        // Split candidate on non-alphanumeric characters (spaces, dashes, underscores, etc.)
        // and check if any word starts with query.
        int wordStart = 0;
        bool inWord = false;
        for (int i = 0; i <= candidate.Length; i++)
        {
            bool isWordChar = i < candidate.Length && (char.IsLetterOrDigit(candidate[i]) || candidate[i] == '\'');
            if (isWordChar && !inWord)
            {
                wordStart = i;
                inWord = true;
            }
            else if (!isWordChar && inWord)
            {
                // We just ended a word at [wordStart..i).
                // Skip the first word (the prefix check already handled position 0).
                if (wordStart > 0)
                {
                    int wordLen = i - wordStart;
                    if (wordLen >= query.Length &&
                        candidate.AsSpan(wordStart, wordLen).StartsWith(
                            query.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                inWord = false;
            }
        }
        return false;
    }
}
