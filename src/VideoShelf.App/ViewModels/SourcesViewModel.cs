using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SourcesViewModel(LibraryRepository library, IFolderPicker picker)
    : ObservableObject
{
    public ObservableCollection<Source> Sources { get; } = [];

    public void Load()
    {
        Sources.Clear();
        foreach (var s in library.GetSources())
            Sources.Add(s);
    }

    [RelayCommand]
    private void AddSource()
    {
        var folder = picker.PickFolder();
        if (string.IsNullOrWhiteSpace(folder))
            return;

        var displayName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar))
            is { Length: > 0 } name ? name : folder;
        library.UpsertSource(folder, displayName);
        Load();
    }

    [RelayCommand]
    private void RemoveSource(Source? source)
    {
        if (source is null)
            return;
        library.RemoveSource(source.Id);
        Load();
    }
}
