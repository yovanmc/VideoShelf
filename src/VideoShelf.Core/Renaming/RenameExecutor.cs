// src/VideoShelf.Core/Renaming/RenameExecutor.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VideoShelf.Core.Storage;

namespace VideoShelf.Core.Renaming;

/// <summary>Executes a confirmed <see cref="RenamePlan"/>: writes an undo manifest first, then renames files on
/// disk and repaths the DB off stable video ids. Crash-safe and reversible by design.</summary>
public sealed class RenameExecutor(IFileSystem fs, LibraryRepository library)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public RenameResult Apply(RenamePlan plan, long seriesId, string manifestDirectory)
    {
        var ready = new List<RenameItem>();
        foreach (var i in plan.Items) if (i.WillRename) ready.Add(i);
        if (ready.Count == 0)
            return new RenameResult(0, plan.Items.Count, null, Array.Empty<string>());

        // Re-verify against "now" — fail safe if the disk changed since planning.
        var actionable = new List<RenameItem>();
        var errors = new List<string>();
        foreach (var i in ready)
        {
            if (!fs.FileExists(i.OldPath)) { errors.Add($"{i.OldName}: source missing at apply time"); continue; }
            if (fs.FileExists(i.NewPath)) { errors.Add($"{i.NewName}: target already exists at apply time"); continue; }
            actionable.Add(i);
        }
        if (actionable.Count == 0)
            return new RenameResult(0, plan.Items.Count, null, errors);

        // 1) Write the undo manifest BEFORE any move, so a crash mid-batch is recoverable.
        var batchId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
        var entries = new List<RenameManifestEntry>(actionable.Count);
        foreach (var i in actionable) entries.Add(new RenameManifestEntry(i.VideoId, i.OldPath, i.NewPath));
        var manifest = new RenameManifest(batchId, seriesId, DateTimeOffset.UtcNow.ToString("O"), entries);

        if (!fs.DirectoryExists(manifestDirectory)) fs.CreateDirectory(manifestDirectory);
        var manifestPath = Path.Combine(manifestDirectory, $"rename-{batchId}.json");
        WriteAtomic(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

        // 2) Move each file, then repath the DB off the stable video id.
        var renamed = 0;
        foreach (var i in actionable)
        {
            try { fs.Move(i.OldPath, i.NewPath); }      // 2-arg move never overwrites
            catch (Exception ex) { errors.Add($"{i.OldName} -> {i.NewName}: {ex.Message}"); continue; }
            library.UpdateVideoPath(i.VideoId, i.OldPath, i.NewPath);
            renamed++;
        }

        return new RenameResult(renamed, plan.Items.Count - renamed, manifestPath, errors);
    }

    /// <summary>Reverses the renames in a manifest: moves new->old where the new file still exists and old is free,
    /// repaths the DB back. Tolerant of partially-applied batches.</summary>
    public RenameResult Undo(string manifestPath)
    {
        if (!fs.FileExists(manifestPath))
            return new RenameResult(0, 0, manifestPath, new[] { "manifest not found" });

        var manifest = JsonSerializer.Deserialize<RenameManifest>(fs.ReadAllText(manifestPath), JsonOptions);
        if (manifest is null)
            return new RenameResult(0, 0, manifestPath, new[] { "manifest unreadable" });

        var reverted = 0;
        var skipped = 0;
        var errors = new List<string>();
        foreach (var e in manifest.Entries)
        {
            if (!fs.FileExists(e.NewPath)) { skipped++; continue; }                 // move never happened
            if (fs.FileExists(e.OldPath)) { skipped++; errors.Add($"{Path.GetFileName(e.OldPath)}: original path occupied"); continue; }
            try { fs.Move(e.NewPath, e.OldPath); }
            catch (Exception ex) { errors.Add($"undo {Path.GetFileName(e.NewPath)}: {ex.Message}"); continue; }
            library.UpdateVideoPath(e.VideoId, e.NewPath, e.OldPath);
            reverted++;
        }
        return new RenameResult(reverted, skipped, manifestPath, errors);
    }

    private void WriteAtomic(string path, string contents)
    {
        var tmp = path + ".tmp";
        fs.WriteAllText(tmp, contents);
        fs.Move(tmp, path); // batchId is unique, so path does not pre-exist
    }
}
