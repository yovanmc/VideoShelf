using System;
using System.Globalization;
using VideoShelf.Core.Models;

namespace VideoShelf.Core.Naming;

/// <summary>
/// Derives a base title + optional episode number from a filename stem.
/// Rule: the episode marker is the FIRST whitespace-delimited token after the first token
/// that parses as a positive integer. The base title is everything before it (whitespace
/// collapsed); everything from the marker onward is dropped. No such token => unnumbered.
/// </summary>
public static class TitleParser
{
    public static ParsedTitle Parse(string stem)
    {
        var tokens = stem.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return new ParsedTitle(stem.Trim(), null);

        for (var i = 1; i < tokens.Length; i++)
        {
            if (int.TryParse(tokens[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0)
                return new ParsedTitle(string.Join(' ', tokens[..i]), n);
        }
        return new ParsedTitle(string.Join(' ', tokens), null);
    }
}
