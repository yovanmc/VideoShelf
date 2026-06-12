// src/VideoShelf.Core/Renaming/CanonicalNamer.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace VideoShelf.Core.Renaming;

/// <summary>Builds canonical "&lt;Base Title&gt; &lt;NN&gt;.ext" file names that re-parse to the same
/// (title, episode) via TitleParser — so a rescan re-groups them identically.</summary>
public static class CanonicalNamer
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    /// <summary>Minimum zero-pad width for a set of episode numbers (>= 2 so natural sort holds to 99).</summary>
    public static int PadWidth(IEnumerable<int> episodeNumbers)
    {
        var max = 0;
        foreach (var n in episodeNumbers)
            if (n > max) max = n;
        var digits = max <= 0 ? 1 : (int)Math.Floor(Math.Log10(max)) + 1;
        return Math.Max(2, digits);
    }

    /// <summary>Replaces characters illegal in a file name with spaces, collapses whitespace, trims.</summary>
    public static string SanitizeTitle(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title)
            sb.Append(Array.IndexOf(InvalidChars, ch) >= 0 ? ' ' : ch);
        return string.Join(' ',
            sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>Builds the canonical file name (with extension). A null <paramref name="episodeNo"/> means a
    /// standalone — no number is appended. <paramref name="extension"/> may or may not include the leading dot.</summary>
    public static string Build(string baseTitle, int? episodeNo, string extension, int padWidth)
    {
        var title = SanitizeTitle(baseTitle);
        if (title.Length == 0) title = "untitled";
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        if (episodeNo is null)
            return title + ext;
        var num = episodeNo.Value.ToString(CultureInfo.InvariantCulture).PadLeft(padWidth, '0');
        return $"{title} {num}{ext}";
    }
}
