// tests/VideoShelf.Core.Tests/InMemoryFileSystem.cs
using System;
using System.Collections.Generic;
using System.IO;
using VideoShelf.Core.Renaming;

namespace VideoShelf.Core.Tests;

/// <summary>In-memory <see cref="IFileSystem"/> for rename tests. Move throws if the target exists
/// (mirrors the 2-arg File.Move contract). Paths are normalized with Path.GetFullPath.</summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryFileSystem(params string[] initialFiles)
    {
        foreach (var f in initialFiles) _files[Norm(f)] = "";
    }

    private static string Norm(string p) => Path.GetFullPath(p);

    public void AddFile(string path, string contents = "") => _files[Norm(path)] = contents;

    public bool FileExists(string path) => _files.ContainsKey(Norm(path));
    public bool DirectoryExists(string path) => _dirs.Contains(Norm(path));
    public void CreateDirectory(string path) => _dirs.Add(Norm(path));

    public void Move(string sourcePath, string destinationPath)
    {
        var src = Norm(sourcePath);
        var dst = Norm(destinationPath);
        if (!_files.ContainsKey(src)) throw new FileNotFoundException("source not found", src);
        if (_files.ContainsKey(dst)) throw new IOException($"target exists: {dst}");
        _files[dst] = _files[src];
        _files.Remove(src);
    }

    public string ReadAllText(string path) => _files[Norm(path)];
    public void WriteAllText(string path, string contents) => _files[Norm(path)] = contents;

    /// <summary>
    /// Returns the byte-length of the stored string content encoded as UTF-8.
    /// An empty string represents a zero-byte file (which the keeper gate rejects).
    /// Returns -1 when the file does not exist in the in-memory store.
    /// </summary>
    public long GetFileLength(string path)
    {
        var key = Norm(path);
        if (!_files.TryGetValue(key, out var content)) return -1L;
        return System.Text.Encoding.UTF8.GetByteCount(content);
    }
}
