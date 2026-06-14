// src/VideoShelf.Core/Renaming/IFileSystem.cs
namespace VideoShelf.Core.Renaming;

/// <summary>Filesystem seam so rename planning/execution is unit-testable with an in-memory fake.</summary>
public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    /// <summary>Renames/moves a file. MUST throw if the destination already exists (never overwrite).</summary>
    void Move(string sourcePath, string destinationPath);
    string ReadAllText(string path);
    void WriteAllText(string path, string contents);
    /// <summary>
    /// Returns the byte length of an existing file.
    /// Returns -1 if the file does not exist or cannot be stat'd.
    /// Used by the duplicate-resolve keeper-safety gate (verify keeper is present and non-zero).
    /// </summary>
    long GetFileLength(string path);
}
