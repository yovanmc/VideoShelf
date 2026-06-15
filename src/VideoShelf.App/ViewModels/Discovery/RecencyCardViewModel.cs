using System.Windows.Media;
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

    /// <summary>Alias for <see cref="Watched"/>: used by VideoCard's watched-checkmark badge
    /// so the card template can bind a uniform <c>IsWatched</c> property across all card VMs.</summary>
    public bool IsWatched => item.Watched;

    /// <summary>False for recency cards (no resume position exposed): the progress bar
    /// and %-text overlay are not applicable here.</summary>
    public bool HasProgress => false;

    public string? ThumbnailSeedPath => item.ThumbnailSeedPath;
    [ObservableProperty] private string? thumbnailPath;

    /// <summary>Frozen ImageSource for the card cover; always null for recency cards
    /// (thumbnail loading is not currently wired for this card type — placeholder shown).</summary>
    public ImageSource? Cover => null;

    /// <summary>True when this card is selected in the multi-select grid.
    /// The hosting VM subscribes to PropertyChanged and routes changes to
    /// <see cref="SelectionViewModel{T}.OnItemSelectionChanged"/> — no back-ref is stored here.</summary>
    [ObservableProperty] private bool isSelected;

    public event EventHandler? PlayInvoked;
    [RelayCommand] private void Play() => PlayInvoked?.Invoke(this, EventArgs.Empty);
}
