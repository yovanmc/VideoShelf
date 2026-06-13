using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class EpisodeViewModel(EpisodeView model, WatchRepository watch, TagRepository? tags = null) : ObservableObject
{
    public TagEditorViewModel? VideoTagEditor { get; } = tags != null ? new TagEditorViewModel(tags) : null;

    public long VideoId => model.VideoId;
    public string Title => model.Title;
    public int EpisodeNo => model.EpisodeNo;
    public string FilePath => model.FilePath;
    public bool IsMissing => model.Missing;

    [ObservableProperty]
    private bool _watched = model.Watched;

    public event System.EventHandler? WatchedChanged;

    [RelayCommand]
    private void ToggleWatched()
    {
        Watched = !Watched;
        watch.SetWatched(model.VideoId, Watched);
        WatchedChanged?.Invoke(this, System.EventArgs.Empty);
    }

    /// <summary>Raised when the user asks to play this episode; the shell routes it to the player.</summary>
    public event System.EventHandler<EpisodeView>? PlayRequested;

    [RelayCommand]
    private void Play() => PlayRequested?.Invoke(this, model);
}
