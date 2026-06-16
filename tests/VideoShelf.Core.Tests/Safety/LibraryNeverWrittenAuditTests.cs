using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shouldly;
using Xunit;

namespace VideoShelf.Core.Tests.Safety;

/// <summary>
/// B5 — capstone source-grep audit guarding the "the library is never written" invariant.
///
/// This test walks up from the test assembly to the repo root, scans every
/// <c>src/**/*.cs</c> file for file-MUTATING APIs, and asserts each hit lives in an
/// ALLOWLISTED file — one of the justified writers below, every one of which writes only to
/// app-data / output / the M5-manifest-backed in-library rename, NEVER blindly into a
/// source/library folder.
///
/// Any new mutating call in an un-allowlisted file fails this test with a message naming the
/// offender, forcing a human to classify the writer (and, if it writes into a library folder,
/// STOP rather than allowlist it).
///
/// NOTE: <c>Directory.CreateDirectory</c> is deliberately NOT a scanned token — it only ever
/// creates app-data / output directories (covers, thumbs, app root, manifests, harness paths),
/// never a destructive library write — so scanning it would add noise without adding safety.
///
/// NOTE: cover / seek-preview / candidate-frame PNG bytes are written by the NATIVE libVLC
/// snapshot engine (TakeSnapshot), not by any scanned managed API, so those writers
/// (PlayerViewModel, CreatorFramePickerViewModel, LibVlcThumbnailService) carry no scanned
/// token and need no allowlist entry. Their output-path scope is pinned separately by B4
/// (FramePickerWriteScopeTests) — always under the app-data covers/seek-preview dir.
/// </summary>
public sealed class LibraryNeverWrittenAuditTests
{
    // File-mutating APIs we audit. (Directory.CreateDirectory intentionally excluded — see class doc.)
    private static readonly string[] MutatingTokens =
    {
        "File.Delete(",
        "File.Move(",
        "File.Copy(",
        "File.WriteAllText(",
        "File.WriteAllBytes(",
        "File.WriteAllLines(",
        "File.AppendAllText(",
        "File.Create(",
        "File.OpenWrite(",
        "Directory.Delete(",
        "SendToRecycleBin",
        "DeleteFile(",            // Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile (recycle)
        "FileMode.Create",
        "FileMode.OpenOrCreate",
        "FileMode.Append",
        "new StreamWriter",
        "new FileStream",
        "new BinaryWriter",
    };

    /// <summary>
    /// The justified writers. Key = bare filename; value = one-line justification.
    /// Every one writes only to app-data / output / the manifest-backed in-library rename.
    /// </summary>
    private static readonly Dictionary<string, string> Allowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RealFileSystem.cs"]                = "IFileSystem seam for the rename executor (in-library rename is intentional + M5 manifest-backed/undoable).",
        ["CrashReporter.cs"]                 = "M25 crash logger — writes crash reports into the app-data diagnostics folder.",
        ["HarnessRunner.cs"]                 = "Test/fixture harness tooling — writes the done-signal + metrics-out files (never a library path).",
        ["ThumbnailCache.cs"]                = "Thumbnail/bitmap cache writer — place-then-rename into the app-data thumbs cache dir.",
        ["IRecycleBinService.cs"]            = "Recoverable Recycle-Bin service — recycle (not hard-delete); keeper-gated by the caller.",
        ["DuplicateResolveViewModel.cs"]     = "Calls SendToRecycleBin for losers, gated on a present + non-zero keeper (recoverable).",
    };

    [Fact]
    public void AllFileMutatingCalls_LiveInAnAllowlistedWriter()
    {
        var srcRoot = FindSrcRoot();
        srcRoot.ShouldNotBeNull("could not locate the repo src/ directory from the test assembly");

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot!, "*.cs", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            // Skip auto-generated obj/bin artifacts if any leaked under src.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                foreach (var token in MutatingTokens)
                {
                    if (!line.Contains(token, StringComparison.Ordinal)) continue;
                    if (Allowlist.ContainsKey(fileName)) continue; // justified writer

                    offenders.Add($"{fileName}:{i + 1}  «{token}»  {line.Trim()}");
                    break; // one report per line is enough
                }
            }
        }

        offenders.ShouldBeEmpty(
            "Found file-mutating call(s) in non-allowlisted file(s). Classify each writer: if it " +
            "writes into a SOURCE/LIBRARY folder, STOP — do not allowlist it. Otherwise add the " +
            "filename + a one-line justification to the Allowlist in this test:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void EveryAllowlistedFile_StillExists_AndStillContainsAMutatingCall()
    {
        // Keeps the allowlist honest: a stale entry (file gone, or no longer a writer) must be removed.
        var srcRoot = FindSrcRoot();
        srcRoot.ShouldNotBeNull();

        var allCsFiles = Directory
            .EnumerateFiles(srcRoot!, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        foreach (var (allowName, _) in Allowlist)
        {
            var match = allCsFiles.FirstOrDefault(f =>
                Path.GetFileName(f).Equals(allowName, StringComparison.OrdinalIgnoreCase));
            match.ShouldNotBeNull($"allowlisted writer '{allowName}' no longer exists under src/ — remove the stale entry");

            var text = File.ReadAllText(match!);
            MutatingTokens.Any(t => text.Contains(t, StringComparison.Ordinal))
                .ShouldBeTrue($"allowlisted writer '{allowName}' no longer contains any mutating call — remove the stale entry");
        }
    }

    /// <summary>Walks up from the test base directory to find the repo's <c>src</c> folder.</summary>
    private static string? FindSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(candidate) &&
                Directory.Exists(Path.Combine(candidate, "VideoShelf.Core")))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
