namespace VideoShelf.App.Services;

/// <summary>Abstracts the OS folder-chooser so source management is testable without UI.</summary>
public interface IFolderPicker
{
    /// <summary>Returns the chosen folder's full path, or null if the user cancelled.</summary>
    string? PickFolder(string? initialFolder = null);
}
