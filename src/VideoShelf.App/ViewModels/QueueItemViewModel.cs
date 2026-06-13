using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.Core.Models;

namespace VideoShelf.App.ViewModels;

public sealed partial class QueueItemViewModel : ObservableObject
{
    public EpisodeView Episode { get; }
    public QueueItemViewModel(EpisodeView episode) => Episode = episode;

    public string Title => Episode.Title;

    [ObservableProperty] private bool _isNowPlaying;
}
