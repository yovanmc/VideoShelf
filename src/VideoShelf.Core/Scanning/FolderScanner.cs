using System.Collections.Generic;
using System.IO;
using System.Linq;
using VideoShelf.Core.Models;
using VideoShelf.Core.Naming;

namespace VideoShelf.Core.Scanning;

/// <summary>A section folder found under a source root, with its video files.</summary>
public sealed record ScannedSection(string FolderName, IReadOnlyList<ScannedFile> Files);

/// <summary>
/// Scans a single source root: each immediate subfolder is a section; its video files (one level
/// deep, per the spec's "flat" sections) become ScannedFiles. Folders with no video files are omitted.
/// </summary>
public static class FolderScanner
{
    public static IReadOnlyList<ScannedSection> Scan(string sourceRoot)
    {
        if (!Directory.Exists(sourceRoot))
            return [];

        var sections = new List<ScannedSection>();
        foreach (var subDir in Directory.EnumerateDirectories(sourceRoot))
        {
            var files = Directory.EnumerateFiles(subDir)
                .Where(p => VideoExtensions.IsVideo(p))
                .Select(p => new ScannedFile(p, Path.GetFileName(p), Path.GetExtension(p)))
                .ToList();
            if (files.Count > 0)
                sections.Add(new ScannedSection(Path.GetFileName(subDir), files));
        }
        return sections;
    }
}
