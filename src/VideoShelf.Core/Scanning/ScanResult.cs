namespace VideoShelf.Core.Scanning;

/// <summary>
/// Summary of changes detected by a single <see cref="ScanService.ScanSource"/> call.
/// </summary>
/// <param name="Added">Files whose <c>file_path</c> did not exist in the DB before this scan.</param>
/// <param name="Updated">Files that existed and were already present (missing=0), re-seen this scan.</param>
/// <param name="Restored">Files that existed but were missing (missing=1) and are now found.</param>
/// <param name="Missing">Videos still missing after the scan (existed before, not found on disk now).</param>
public sealed record ScanResult(int Added, int Updated, int Restored, int Missing);
