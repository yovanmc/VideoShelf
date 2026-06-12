using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed partial class DiscoveryViewModel(
    DiscoveryRepository discovery, LibraryRepository library, TagRepository tags) : ObservableObject
{
    private const int RailLimit = 24;

    public ObservableCollection<ContinueWatchingCardViewModel> ContinueWatching { get; } = [];
    public ObservableCollection<RecencyCardViewModel> RecentlyAdded { get; } = [];
    public ObservableCollection<RecencyCardViewModel> RecentlyWatched { get; } = [];
    public ObservableCollection<SectionCardViewModel> ForYou { get; } = [];
    public ObservableCollection<TagChipViewModel> AvailableTags { get; } = [];
    public ObservableCollection<SectionCardViewModel> TagResults { get; } = [];

    public bool HasContinueWatching => ContinueWatching.Count > 0;
    public bool HasRecentlyAdded => RecentlyAdded.Count > 0;
    public bool HasRecentlyWatched => RecentlyWatched.Count > 0;
    public bool HasForYou => ForYou.Count > 0;
    public bool HasTags => AvailableTags.Count > 0;
    public bool HasTagResults => TagResults.Count > 0;
    public bool IsEmpty =>
        !HasContinueWatching && !HasRecentlyAdded && !HasRecentlyWatched && !HasForYou && !HasTags;

    public event EventHandler<EpisodeView>? PlayRequested;
    public event EventHandler<long>? SectionOpenRequested;

    public async Task LoadAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var data = await Task.Run(() => (
            cont: discovery.GetContinueWatching(RailLimit),
            added: discovery.GetRecentlyAdded(RailLimit),
            watched: discovery.GetRecentlyWatched(RailLimit),
            forYou: discovery.GetForYou(RailLimit, now),
            tagCounts: tags.GetTagCounts()));

        Fill(ContinueWatching, data.cont, MakeContinueCard);
        Fill(RecentlyAdded, data.added, MakeRecencyCard);
        Fill(RecentlyWatched, data.watched, MakeRecencyCard);
        Fill(ForYou, data.forYou, MakeSectionCard);

        AvailableTags.Clear();
        foreach (var tc in data.tagCounts) AvailableTags.Add(new TagChipViewModel(tc.Tag, tc.SectionCount));
        TagResults.Clear();

        RaiseAllHasFlags();
    }

    [RelayCommand]
    private async Task ToggleTag(TagChipViewModel chip)
    {
        chip.IsSelected = !chip.IsSelected;
        var selected = AvailableTags.Where(t => t.IsSelected).Select(t => t.Tag).ToList();
        var results = selected.Count == 0
            ? []
            : await Task.Run(() => discovery.GetSectionsByTags(selected, RailLimit));
        Fill(TagResults, results, MakeSectionCard);
        OnPropertyChanged(nameof(HasTagResults));
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

    private SectionCardViewModel MakeSectionCard(SectionSuggestion s)
    {
        var card = new SectionCardViewModel(s);
        card.OpenInvoked += (_, _) => SectionOpenRequested?.Invoke(this, s.SectionId);
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

    private void RaiseAllHasFlags()
    {
        OnPropertyChanged(nameof(HasContinueWatching));
        OnPropertyChanged(nameof(HasRecentlyAdded));
        OnPropertyChanged(nameof(HasRecentlyWatched));
        OnPropertyChanged(nameof(HasForYou));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(HasTagResults));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
