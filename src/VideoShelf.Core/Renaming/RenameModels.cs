// src/VideoShelf.Core/Renaming/RenameModels.cs
using System.Collections.Generic;
using System.IO;

namespace VideoShelf.Core.Renaming;

/// <summary>Why a single row will or won't be renamed.</summary>
public enum RenameItemStatus
{
    Unchanged,       // old == new, nothing to do
    Ready,           // will be renamed
    SourceMissing,   // source file is gone from disk
    TargetExists,    // a different existing file already occupies the target path
    DuplicateTarget, // two rows in this batch resolve to the same target name
    InvalidName,     // proposed name is empty / contains illegal characters
}

/// <summary>One planned rename: stable video id + old/new absolute paths and a status.</summary>
public sealed record RenameItem(long VideoId, int EpisodeNo, string OldPath, string NewPath, RenameItemStatus Status)
{
    public string OldName => Path.GetFileName(OldPath);
    public string NewName => Path.GetFileName(NewPath);
    public bool WillRename => Status == RenameItemStatus.Ready;
}

/// <summary>The planned renames for one series, with conflicts already flagged.</summary>
public sealed record RenamePlan(IReadOnlyList<RenameItem> Items)
{
    public int ReadyCount
    {
        get { var c = 0; foreach (var i in Items) if (i.WillRename) c++; return c; }
    }
    public bool HasReady => ReadyCount > 0;
}
