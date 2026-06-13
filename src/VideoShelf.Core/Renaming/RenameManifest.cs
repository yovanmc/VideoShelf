// src/VideoShelf.Core/Renaming/RenameManifest.cs
using System.Collections.Generic;

namespace VideoShelf.Core.Renaming;

public sealed record RenameManifestEntry(long VideoId, string OldPath, string NewPath);

/// <summary>
/// Crash-safe undo record for one Apply: written to disk BEFORE any file moves.
/// <para>
/// <c>SeriesId</c> is nullable to support multi-series batch renames (Group H).
/// A <c>null</c> SeriesId means the manifest covers videos across multiple series;
/// a non-null value preserves the original single-series behavior (back-compatible JSON:
/// old manifests with a numeric <c>SeriesId</c> field still deserialize correctly; new
/// multi-series manifests serialize the JSON null literal which is ignored on Undo).
/// The <see cref="VideoShelf.Core.Renaming.RenameExecutor.Undo"/> path is purely id-based
/// and does not use <c>SeriesId</c> — the field exists for audit/display only.
/// </para>
/// </summary>
public sealed record RenameManifest(
    string BatchId,
    long? SeriesId,
    string CreatedAtUtc,
    IReadOnlyList<RenameManifestEntry> Entries);
