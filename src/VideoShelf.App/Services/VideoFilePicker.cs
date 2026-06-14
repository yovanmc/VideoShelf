using Microsoft.Win32;

namespace VideoShelf.App.Services;

public interface IVideoFilePicker
{
    /// <summary>Opens a file dialog filtered to common video files; returns the chosen path or null.</summary>
    string? PickVideo(string? initialFolder = null);
}

public sealed class VideoFilePicker : IVideoFilePicker
{
    public string? PickVideo(string? initialFolder = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select replacement video file",
            Filter = "Video files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.m4v;*.ts;*.flv;*.webm;*.mpg;*.mpeg|All files|*.*",
            Multiselect = false,
            CheckFileExists = true,
        };
        if (!string.IsNullOrWhiteSpace(initialFolder))
            dialog.InitialDirectory = initialFolder;

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
