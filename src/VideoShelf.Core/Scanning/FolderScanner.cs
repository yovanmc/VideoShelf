using System;
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
        // Materialize the top-level listing inside the guard too: an unreadable/locked source root
        // (UnauthorizedAccessException / IOException) must yield an empty scan, never abort the caller.
        IReadOnlyList<string> subDirs;
        try { subDirs = Directory.EnumerateDirectories(sourceRoot).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return sections; }

        foreach (var subDir in subDirs)
        {
            // Fail-safe per section: a single unreadable/locked subfolder (e.g. a denied ACL or a
            // file vanishing mid-enumeration) is SKIPPED so the rest of the library still scans.
            List<ScannedFile> files;
            try
            {
                files = Directory.EnumerateFiles(subDir)
                    .Where(p => VideoExtensions.IsVideo(p))
                    .Select(p => new ScannedFile(p, Path.GetFileName(p), Path.GetExtension(p)))
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (files.Count > 0)
                sections.Add(new ScannedSection(Path.GetFileName(subDir), files));
        }
        return sections;
    }
}
