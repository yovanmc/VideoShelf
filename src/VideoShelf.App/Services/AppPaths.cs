using System;
using System.IO;

namespace VideoShelf.App.Services;

/// <summary>Resolves VideoShelf's on-disk locations. Default root is %LOCALAPPDATA%\VideoShelf.</summary>
public sealed class AppPaths
{
    public string Root { get; }

    public AppPaths()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoShelf"))
    {
    }

    public AppPaths(string root) => Root = root;

    public string DatabasePath => Path.Combine(Root, "library.db");
    public string ThumbnailDirectory => Path.Combine(Root, "thumbs");
    public string CaptureDirectory => Path.Combine(Root, "captures");
    public string SeekPreviewDirectory => Path.Combine(Root, "seek-preview");
    public string RenameManifestDirectory => Path.Combine(Root, "rename-manifests");
}
