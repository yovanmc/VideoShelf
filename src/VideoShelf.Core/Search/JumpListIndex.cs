using System;
using System.Collections.Generic;

namespace VideoShelf.Core.Search;

/// <summary>
/// Pure, stateless helper for the A–Z jump-list on the Browse creator grid.
/// Non-alpha leading characters (digits, symbols, etc.) are excluded from the
/// letter buckets; the caller is responsible for handling a "#" bucket if desired.
/// All comparisons are case-insensitive using the invariant culture.
/// </summary>
public static class JumpListIndex
{
    /// <summary>
    /// Returns the index of the first name in <paramref name="names"/> whose first character
    /// (case-insensitive) matches <paramref name="letter"/>.  Returns -1 if none.
    /// </summary>
    /// <param name="names">Ordered list of creator display names (the order mirrors the ListBox items).</param>
    /// <param name="letter">The target ASCII letter (A–Z or a–z).</param>
    public static int FirstIndexForLetter(IReadOnlyList<string> names, char letter)
    {
        var target = char.ToUpperInvariant(letter);
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i].Length == 0) continue;
            if (char.ToUpperInvariant(names[i][0]) == target)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Returns the set of letters (A–Z) for which at least one name in
    /// <paramref name="names"/> starts with that letter (case-insensitive).
    /// The returned collection is in alphabetical order.
    /// </summary>
    public static IReadOnlyList<char> AvailableLetters(IReadOnlyList<string> names)
    {
        var seen = new bool[26];
        foreach (var name in names)
        {
            if (name.Length == 0) continue;
            var c = char.ToUpperInvariant(name[0]);
            if (c >= 'A' && c <= 'Z')
                seen[c - 'A'] = true;
        }

        var result = new List<char>(26);
        for (var i = 0; i < 26; i++)
        {
            if (seen[i]) result.Add((char)('A' + i));
        }
        return result;
    }
}
