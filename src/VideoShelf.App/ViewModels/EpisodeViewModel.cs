using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class EpisodeViewModel(
    EpisodeView model,
    WatchRepository watch,
    TagRepository? tags = null,
    CurationRepository? curation = null) : ObservableObject
{
    public TagEditorViewModel? VideoTagEditor { get; } = tags != null ? new TagEditorViewModel(tags) : null;

    public long VideoId => model.VideoId;
    public string Title => model.Title;
    public int EpisodeNo => model.EpisodeNo;
    public string FilePath => model.FilePath;
    public bool IsMissing => model.Missing;

    public bool HasCuration => curation is not null;

    [ObservableProperty]
    private bool _watched = model.Watched;

    [ObservableProperty]
    private bool _isFavorite = curation?.IsFavorite(model.VideoId) ?? false;

    [ObservableProperty]
    private int _rating = curation?.GetRating(model.VideoId) ?? 0;

    public event System.EventHandler? WatchedChanged;

    [RelayCommand]
    private void ToggleWatched()
    {
        Watched = !Watched;
        watch.SetWatched(model.VideoId, Watched);
        WatchedChanged?.Invoke(this, System.EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (curation is null) return;
        IsFavorite = !IsFavorite;
        curation.SetFavorite(model.VideoId, IsFavorite);
    }

    [RelayCommand]
    private void SetRating(object? param)
    {
        if (curation is null) return;
        var r = param switch
        {
            int i => i,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 0,
        };
        var clamped = System.Math.Max(0, System.Math.Min(5, r));
        Rating = clamped;
        curation.SetRating(model.VideoId, clamped);
    }

    /// <summary>Raised when the user asks to play this episode; the shell routes it to the player.</summary>
    public event System.EventHandler<EpisodeView>? PlayRequested;

    [RelayCommand]
    private void Play() => PlayRequested?.Invoke(this, model);
}
