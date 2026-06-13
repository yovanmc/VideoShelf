using Microsoft.Win32;

namespace VideoShelf.App.Services;

public interface ISubtitleFilePicker
{
    /// <summary>Opens a file dialog filtered to subtitle files; returns the chosen path or null.</summary>
    string? PickSubtitle(string? initialFolder = null);
}

public sealed class SubtitleFilePicker : ISubtitleFilePicker
{
    public string? PickSubtitle(string? initialFolder = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select subtitle file",
            Filter = "Subtitles|*.srt;*.ass;*.ssa;*.vtt;*.sub|All files|*.*",
            Multiselect = false,
            CheckFileExists = true,
        };
        if (!string.IsNullOrWhiteSpace(initialFolder))
            dialog.InitialDirectory = initialFolder;

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
