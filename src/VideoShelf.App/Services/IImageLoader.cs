namespace VideoShelf.App.Services;

using System.Windows.Media;

public interface IImageLoader
{
    /// <summary>Returns a frozen ImageSource decoded at ~decodePixelWidth, or null on failure.
    /// Never throws into the UI (fail-safe placeholder).</summary>
    ImageSource? Load(string? path, int decodePixelWidth);
}
