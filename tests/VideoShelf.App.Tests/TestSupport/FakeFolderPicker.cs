using System.Collections.Generic;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests.TestSupport;

/// <summary>Returns queued folders in order; null when exhausted (simulating cancel).</summary>
public sealed class FakeFolderPicker : IFolderPicker
{
    private readonly Queue<string?> _queued;

    public FakeFolderPicker(params string?[] folders) => _queued = new Queue<string?>(folders);

    public string? PickFolder(string? initialFolder = null)
        => _queued.Count > 0 ? _queued.Dequeue() : null;
}
