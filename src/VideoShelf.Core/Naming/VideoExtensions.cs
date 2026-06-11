using System;
using System.Collections.Generic;
using System.IO;

namespace VideoShelf.Core.Naming;

/// <summary>The set of file extensions VideoShelf treats as playable video (libVLC handles all of these).</summary>
public static class VideoExtensions
{
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".flv",
        ".ts", ".m2ts", ".mts", ".mpg", ".mpeg", ".vob", ".ogv", ".3gp", ".divx",
    };

    public static bool IsVideo(string fileName)
        => Known.Contains(Path.GetExtension(fileName));
}
