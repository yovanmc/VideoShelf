using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed partial class DiscoveryViewModel(
    DiscoveryRepository discovery, LibraryRepository library, TagRepository tags,
    CreatorCardFactory cards, StatsRepository stats, PlayQueueViewModel playQueue,
    SmartViewRepository smartViews, CurationRepository? curation = null) : ObservableObject
{
    private const int RailLimit = 24;

    public ObservableCollection<ContinueWatchingCardViewModel> ContinueWatching { get; } = [];
    public ObservableCollection<CreatorCardViewModel> RecommendedCreators { get; } = [];
    public ObservableCollection<RecencyCardViewModel> RecommendedVideos { get; } = [];
    public ObservableCollection<RecencyCardViewModel> RecentlyAdded { get; } = [];
    public ObservableCollection<RecencyCardViewModel> RecentlyWatched { get; } = [];
    public ObservableCollection<TagChipViewModel> AvailableTags { get; } = [];
    public ObservableCollection<CreatorCardViewModel> TagResults { get; } = [];
    public ObservableCollection<CreatorWatchCount> TopCreators { get; } = new();
    public ObservableCollection<SmartViewShelfViewModel> SmartShelves { get; } = [];
    public bool HasSmartShelves => SmartShelves.Count > 0;

    public ObservableCollection<RecencyCardViewModel> Favorites { get; } = [];
    public bool HasFavorites => Favorites.Count > 0;

    public ObservableCollection<RecencyCardViewModel> Watchlist { get; } = [];
    public bool HasWatchlist => Watchlist.Count > 0;

    [ObservableProperty] private string _watchedSummary = "";
    [ObservableProperty] private string _inProgressSummary = "";
    [ObservableProperty] private bool _hasStats;
    [ObservableProperty] private bool _hasInProgress;

    public bool HasContinueWatching => ContinueWatching.Count > 0;
    public bool HasRecommendedCreators => RecommendedCreators.Count > 0;
    public bool HasRecommendedVideos => RecommendedVideos.Count > 0;
    public bool HasRecentlyAdded => RecentlyAdded.Count > 0;
    public bool HasRecentlyWatched => RecentlyWatched.Count > 0;
    public bool HasTags => AvailableTags.Count > 0;
    public bool HasTagResults => TagResults.Count > 0;
    public bool IsEmpty =>
        !HasContinueWatching && !HasRecommendedCreators && !HasRecommendedVideos
        && !HasRecentlyAdded && !HasRecentlyWatched && !HasTags;

    private Dictionary<long, SectionSummary> _summaryById = new();

    public event EventHandler<EpisodeView>? PlayRequested;
    public event EventHandler<long>? SectionOpenRequested;

    public async Task LoadAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var data = await Task.Run(() =>
        {
            var cont = discovery.GetContinueWatching(RailLimit);
            return (
                cont,
                forYou: discovery.GetForYou(RailLimit, now),
                recVideos: discovery.GetRecommendedVideos(RailLimit, now),
                added: discovery.GetRecentlyAdded(RailLimit),
                watched: discovery.GetRecentlyWatched(RailLimit),
                tagCounts: tags.GetTagCounts(),
                summaries: library.GetSectionSummaries(),
                libStats: stats.GetLibraryStats(),
                topCreators: stats.GetTopCreatorsByWatched(5),
                favItems: curation?.GetFavorites(RailLimit) ?? [],
                watchItems: curation?.GetWatchlist(RailLimit) ?? []);
        });

        var smartData = await Task.Run(() =>
            smartViews.GetHomeViews()
                .Select(v => (view: v, items: smartViews.GetMatchingVideos(v.Definition, RailLimit, now)))
                .ToList());

        _summaryById = data.summaries.ToDictionary(s => s.SectionId);

        Fill(ContinueWatching, data.cont, MakeContinueCard);
        FillCreators(RecommendedCreators, data.forYou);
        Fill(RecommendedVideos, data.recVideos, MakeRecencyCard);
        Fill(RecentlyAdded, data.added, MakeRecencyCard);
        Fill(RecentlyWatched, data.watched, MakeRecencyCard);
        Fill(Favorites, data.favItems, MakeRecencyCard);
        Fill(Watchlist, data.watchItems, MakeRecencyCard);

        AvailableTags.Clear();
        foreach (var tc in data.tagCounts) AvailableTags.Add(new TagChipViewModel(tc.Tag, tc.SectionCount));
        TagResults.Clear();

        var s = data.libStats;
        WatchedSummary = $"{s.WatchedVideos} of {s.TotalVideos} watched · {FormatDuration(s.WatchedDurationSeconds)}";
        HasInProgress = s.InProgressVideos > 0;
        InProgressSummary = HasInProgress ? $"{s.InProgressVideos} in progress" : "";
        TopCreators.Clear();
        foreach (var c in data.topCreators) TopCreators.Add(c);
        HasStats = s.TotalVideos > 0;

        SmartShelves.Clear();
        foreach (var (view, items) in smartData)
            SmartShelves.Add(new SmartViewShelfViewModel(view.Name, items.Select(MakeRecencyCard)));

        RaiseAllHasFlags();
    }

    [RelayCommand]
    private void EnqueueVideo(long videoId)
    {
        var ep = library.GetEpisode(videoId);
        if (ep is not null) playQueue.Enqueue(ep);
    }

    [RelayCommand]
    private void PlayVideoNext(long videoId)
    {
        var ep = library.GetEpisode(videoId);
        if (ep is not null) playQueue.PlayNext(ep);
    }

    [RelayCommand]
    private async Task ToggleTag(TagChipViewModel chip)
    {
        chip.IsSelected = !chip.IsSelected;
        var selected = AvailableTags.Where(t => t.IsSelected).Select(t => t.Tag).ToList();
        var results = selected.Count == 0
            ? Array.Empty<SectionSuggestion>()
            : await Task.Run(() => discovery.GetSectionsByTags(selected, RailLimit));
        TagResults.Clear();
        foreach (var s in results)
            if (_summaryById.TryGetValue(s.SectionId, out var summary))
                TagResults.Add(MakeCreatorCard(summary));
        OnPropertyChanged(nameof(HasTagResults));
    }

    private void FillCreators(ObservableCollection<CreatorCardViewModel> target, IReadOnlyList<SectionSuggestion> items)
    {
        target.Clear();
        foreach (var s in items)
            if (_summaryById.TryGetValue(s.SectionId, out var summary))
                target.Add(MakeCreatorCard(summary));
    }

    private CreatorCardViewModel MakeCreatorCard(SectionSummary summary)
    {
        var card = cards.Create(summary);
        card.OpenRequested += id => SectionOpenRequested?.Invoke(this, id);
        return card;
    }

    private ContinueWatchingCardViewModel MakeContinueCard(ContinueWatchingItem i)
    {
        var card = new ContinueWatchingCardViewModel(i);
        card.PlayInvoked += (_, _) => RaisePlay(i.SeriesId, i.VideoId);
        return card;
    }

    private RecencyCardViewModel MakeRecencyCard(RecencyItem i)
    {
        var card = new RecencyCardViewModel(i);
        card.PlayInvoked += (_, _) => RaisePlay(i.SeriesId, i.VideoId);
        return card;
    }

    private void RaisePlay(long seriesId, long videoId)
    {
        var episode = library.GetEpisodes(seriesId).FirstOrDefault(e => e.VideoId == videoId);
        if (episode is not null) PlayRequested?.Invoke(this, episode);
    }

    private static void Fill<TItem, TCard>(
        ObservableCollection<TCard> target, IReadOnlyList<TItem> items, Func<TItem, TCard> make)
    {
        target.Clear();
        foreach (var i in items) target.Add(make(i));
    }

    private static void Fill<TItem, TExtra, TCard>(
        ObservableCollection<TCard> target, IReadOnlyList<TItem> items, IReadOnlyList<TExtra> extras, Func<TItem, TExtra, TCard> make)
    {
        target.Clear();
        for (var idx = 0; idx < items.Count; idx++) target.Add(make(items[idx], extras[idx]));
    }

    private void RaiseAllHasFlags()
    {
        OnPropertyChanged(nameof(HasContinueWatching));
        OnPropertyChanged(nameof(HasRecommendedCreators));
        OnPropertyChanged(nameof(HasRecommendedVideos));
        OnPropertyChanged(nameof(HasRecentlyAdded));
        OnPropertyChanged(nameof(HasRecentlyWatched));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(HasTagResults));
        OnPropertyChanged(nameof(HasSmartShelves));
        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(HasWatchlist));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static string FormatDuration(double seconds)
    {
        var total = (int)Math.Round(seconds);
        var h = total / 3600;
        var m = (total % 3600) / 60;
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }
}
