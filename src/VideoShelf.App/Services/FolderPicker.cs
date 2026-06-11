using Microsoft.Win32;

namespace VideoShelf.App.Services;

public sealed class FolderPicker : IFolderPicker
{
    public string? PickFolder(string? initialFolder = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Add a video source folder",
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(initialFolder))
            dialog.InitialDirectory = initialFolder;

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
