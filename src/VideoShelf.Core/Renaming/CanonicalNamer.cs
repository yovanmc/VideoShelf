// src/VideoShelf.Core/Renaming/CanonicalNamer.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace VideoShelf.Core.Renaming;

/// <summary>Provides context for rendering a file-name template: the creator and series names.</summary>
/// <param name="Creator">Creator / section display name.</param>
/// <param name="Series">Series / base-title.</param>
public sealed record TemplateContext(string Creator, string Series);

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

    /// <summary>
    /// Pure template renderer. Supported tokens: <c>{creator}</c>, <c>{series}</c>, <c>{NN}</c>.
    /// Literals pass through unchanged. The final stem is sanitized via <see cref="SanitizeTitle"/>
    /// before the extension is appended.
    /// <para>
    /// The default template <c>"{series} {NN}"</c> reproduces today's canonical per-series behavior,
    /// so a single-series rename is a special case of the template path.
    /// </para>
    /// <para>
    /// <b>Rescan-stability invariant:</b> the rendered name must still re-parse to a stable
    /// (title, episode) via <c>TitleParser</c>. This is satisfied as long as the creator/series names
    /// contain no leading-whitespace-delimited pure-integer tokens before the episode number. The template
    /// places the episode number as the final token, so re-parse always finds it last.
    /// </para>
    /// </summary>
    /// <param name="template">Template string, e.g. <c>"{series} {NN}"</c> or <c>"{creator} - {series} - {NN}"</c>.</param>
    /// <param name="ctx">Creator and series names.</param>
    /// <param name="episodeNo">Episode number; null for standalones (the <c>{NN}</c> token is replaced with an empty string and trailing space is trimmed).</param>
    /// <param name="ext">File extension, with or without a leading dot.</param>
    /// <param name="padWidth">Zero-pad width for the episode number.</param>
    /// <returns>The sanitized file name with extension.</returns>
    public static string RenderTemplate(string template, TemplateContext ctx, int? episodeNo, string ext, int padWidth)
    {
        var num = episodeNo is null
            ? ""
            : episodeNo.Value.ToString(CultureInfo.InvariantCulture).PadLeft(padWidth, '0');

        var stem = template
            .Replace("{creator}", ctx.Creator, StringComparison.OrdinalIgnoreCase)
            .Replace("{series}", ctx.Series, StringComparison.OrdinalIgnoreCase)
            .Replace("{NN}", num, StringComparison.OrdinalIgnoreCase);

        // Trim any leading/trailing whitespace introduced by an absent episode token.
        stem = stem.Trim();

        var sanitized = SanitizeTitle(stem);
        if (sanitized.Length == 0) sanitized = "untitled";

        var extension = ext.StartsWith('.') ? ext : "." + ext;
        return sanitized + extension;
    }
}
