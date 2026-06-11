namespace VideoShelf.Core.Models;
/// <summary>One video file found on disk during a scan.</summary>
public sealed record ScannedFile(string FullPath, string FileName, string Extension);
