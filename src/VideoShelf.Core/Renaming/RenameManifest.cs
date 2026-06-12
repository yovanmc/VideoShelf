// src/VideoShelf.Core/Renaming/RenameManifest.cs
using System.Collections.Generic;

namespace VideoShelf.Core.Renaming;

public sealed record RenameManifestEntry(long VideoId, string OldPath, string NewPath);

/// <summary>Crash-safe undo record for one Apply: written to disk BEFORE any file moves.</summary>
public sealed record RenameManifest(
    string BatchId,
    long SeriesId,
    string CreatedAtUtc,
    IReadOnlyList<RenameManifestEntry> Entries);
