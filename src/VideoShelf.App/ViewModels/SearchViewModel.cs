using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SearchViewModel : ObservableObject
{
    private const int ResultLimit = 48;
    private readonly LibraryRepository _library;
    private readonly CreatorCardFactory _cards;

    private CancellationTokenSource? _opCts;
    private Task _pending = Task.CompletedTask;
    private bool _searching;

    public SearchViewModel(LibraryRepository library, CreatorCardFactory cards)
    {
        _library = library;
        _cards = cards;
    }

    public ObservableCollection<CreatorCardViewModel> CreatorResults { get; } = [];
    public ObservableCollection<RecencyCardViewModel> VideoResults { get; } = [];

    [ObservableProperty] private string _query = "";

    public bool HasCreatorResults => CreatorResults.Count > 0;
    public bool HasVideoResults => VideoResults.Count > 0;
    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);
    public bool NoResults => HasQuery && !_searching && !HasCreatorResults && !HasVideoResults;

    public event EventHandler<EpisodeView>? PlayRequested;
    public event Action<long>? OpenCreatorRequested;

    partial void OnQueryChanged(string value)
    {
        _opCts?.Cancel();
        var cts = _opCts = new CancellationTokenSource();
        _pending = RunSearchAsync(value, cts.Token);
    }

    private async Task RunSearchAsync(string query, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                _searching = false;
                CreatorResults.Clear();
                VideoResults.Clear();
                RaiseFlags();
                return;
            }

            _searching = true;
            RaiseFlags();
            await Task.Delay(150, ct);   // debounce keystrokes
            var (creators, videos) = await Task.Run(() =>
                (_library.SearchCreators(query, ResultLimit),
                 _library.SearchVideos(query, ResultLimit)), ct);
            ct.ThrowIfCancellationRequested();

            CreatorResults.Clear();
            foreach (var c in creators) CreatorResults.Add(MakeCreatorCard(c));
            VideoResults.Clear();
            foreach (var v in videos) VideoResults.Add(MakeVideoCard(v));

            _searching = false;
            RaiseFlags();
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer keystroke — swallow (no unobserved fault)
        }
    }

    private CreatorCardViewModel MakeCreatorCard(SectionSummary s)
    {
        var card = _cards.Create(s);
        card.OpenRequested += id => OpenCreatorRequested?.Invoke(id);
        return card;
    }

    private RecencyCardViewModel MakeVideoCard(RecencyItem i)
    {
        var card = new RecencyCardViewModel(i);
        card.PlayInvoked += (_, _) => RaisePlay(i.SeriesId, i.VideoId);
        return card;
    }

    private void RaisePlay(long seriesId, long videoId)
    {
        var ep = _library.GetEpisodes(seriesId).FirstOrDefault(e => e.VideoId == videoId);
        if (ep is not null) PlayRequested?.Invoke(this, ep);
    }

    private void RaiseFlags()
    {
        OnPropertyChanged(nameof(HasCreatorResults));
        OnPropertyChanged(nameof(HasVideoResults));
        OnPropertyChanged(nameof(HasQuery));
        OnPropertyChanged(nameof(NoResults));
    }

    /// Test hook: await the in-flight search.
    public Task WaitForIdleAsync() => _pending;
}
