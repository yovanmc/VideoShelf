using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class EpisodeViewModel(EpisodeView model, WatchRepository watch) : ObservableObject
{
    public long VideoId => model.VideoId;
    public string Title => model.Title;
    public int EpisodeNo => model.EpisodeNo;
    public string FilePath => model.FilePath;
    public bool IsMissing => model.Missing;

    [ObservableProperty]
    private bool _watched = model.Watched;

    [RelayCommand]
    private void ToggleWatched()
    {
        Watched = !Watched;
        watch.SetWatched(model.VideoId, Watched);
    }
}
