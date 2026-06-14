using System.Collections.Generic;

namespace VideoShelf.App.Services;

/// <summary>
/// Abstraction for sending files to the OS Recycle Bin (recoverable deletion).
/// Kept tiny so a <see cref="FakeRecycleBinService"/> can record calls in unit tests
/// without touching disk.
/// </summary>
public interface IRecycleBinService
{
    /// <summary>
    /// Sends <paramref name="filePath"/> to the Recycle Bin.
    /// Returns <c>true</c> on success, <c>false</c> on failure (file not found, access denied, etc.).
    /// Does NOT throw for recoverable failures; the caller surfaces the error.
    /// </summary>
    bool SendToRecycleBin(string filePath);
}

/// <summary>
/// Concrete implementation using <c>Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile</c>
/// with <c>RecycleOption.SendToRecycleBin</c>.
/// The <c>Microsoft.VisualBasic</c> assembly ships with the .NET runtime — no new NuGet package.
/// This is a recoverable deletion; the file can be restored from the Recycle Bin.
/// </summary>
public sealed class RecycleBinService : IRecycleBinService
{
    /// <inheritdoc/>
    public bool SendToRecycleBin(string filePath)
    {
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                filePath,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Test double for <see cref="IRecycleBinService"/>. Records every path passed to
/// <see cref="SendToRecycleBin"/> and returns <see cref="NextResult"/> (default <c>true</c>).
/// </summary>
public sealed class FakeRecycleBinService : IRecycleBinService
{
    /// <summary>All paths that have been sent to the bin (in call order).</summary>
    public List<string> Recycled { get; } = new();

    /// <summary>Return value for the next <see cref="SendToRecycleBin"/> call. Default <c>true</c>.</summary>
    public bool NextResult { get; set; } = true;

    /// <inheritdoc/>
    public bool SendToRecycleBin(string filePath)
    {
        if (NextResult) Recycled.Add(filePath);
        return NextResult;
    }
}
