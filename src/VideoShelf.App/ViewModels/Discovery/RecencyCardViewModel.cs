using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Discovery;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed partial class RecencyCardViewModel(RecencyItem item) : ObservableObject, ISelectableCard
{
    public long VideoId => item.VideoId;
    public long SeriesId => item.SeriesId;
    public string SeriesTitle => item.SeriesTitle;
    public string EpisodeLabel => item.IsStandalone ? item.SeriesTitle : $"Episode {item.EpisodeNo}";
    public bool Watched => item.Watched;
    public string? ThumbnailSeedPath => item.ThumbnailSeedPath;
    public string? ChapterLabel => null;
    public bool HasChapter => false;

    [ObservableProperty] private string? thumbnailPath;

    /// <summary>True when this card is selected in the multi-select grid.
    /// The hosting VM subscribes to PropertyChanged and routes changes to
    /// <see cref="SelectionViewModel{T}.OnItemSelectionChanged"/> — no back-ref is stored here.</summary>
    [ObservableProperty] private bool isSelected;

    public event EventHandler? PlayInvoked;
    [RelayCommand] private void Play() => PlayInvoked?.Invoke(this, EventArgs.Empty);
}
