using Microsoft.Win32;

namespace VideoShelf.App.Services;

public interface IImagePicker
{
    /// <summary>Returns the chosen image path, or null if cancelled.</summary>
    string? PickImage(string? initialFolder = null);
}

public sealed class ImagePicker : IImagePicker
{
    public string? PickImage(string? initialFolder = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select creator image",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            Multiselect = false,
            CheckFileExists = true,
        };
        if (!string.IsNullOrWhiteSpace(initialFolder))
            dialog.InitialDirectory = initialFolder;

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
