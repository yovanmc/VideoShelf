namespace VideoShelf.Core.Playback;

public static class SubtitleSidecars
{
    public static readonly string[] Extensions = { ".srt", ".ass", ".ssa", ".vtt", ".sub" };

    /// <summary>Given a video path and the list of files sitting in its folder, returns the sibling
    /// paths that are subtitle sidecars for it: a file whose extension is a subtitle extension AND whose
    /// name-without-extension equals the video's base name OR starts with "&lt;base&gt;." (language-tagged,
    /// e.g. movie.en.srt). Case-insensitive. Never returns the video itself.</summary>
    public static IReadOnlyList<string> Find(string videoPath, IEnumerable<string> siblingFilePaths)
    {
        var baseName = System.IO.Path.GetFileNameWithoutExtension(videoPath);
        var result = new List<string>();
        foreach (var sib in siblingFilePaths)
        {
            if (string.Equals(sib, videoPath, StringComparison.OrdinalIgnoreCase)) continue;
            var ext = System.IO.Path.GetExtension(sib);
            if (!Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase))) continue;
            var sibName = System.IO.Path.GetFileNameWithoutExtension(sib); // e.g. "movie" or "movie.en"
            if (string.Equals(sibName, baseName, StringComparison.OrdinalIgnoreCase)
                || sibName.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase))
                result.Add(sib);
        }
        return result;
    }
}
