// src/VideoShelf.Core/Renaming/RealFileSystem.cs
using System.IO;

namespace VideoShelf.Core.Renaming;

/// <summary>Production <see cref="IFileSystem"/> over System.IO. The only place the rename tool touches disk.
/// Move uses the 2-arg File.Move, which throws if the destination exists — defensive by default.</summary>
public sealed class RealFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void Move(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
}
