using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SearchViewModel : ObservableObject, IBulkSelectionSource
{
    private const int ResultLimit = 48;
    private readonly LibraryRepository _library;
    private readonly CreatorCardFactory _cards;
    private readonly IThumbnailService? _thumbnails;
    private readonly IImageLoader? _imageLoader;

    private CancellationTokenSource? _opCts;
    private Task _pending = Task.CompletedTask;
    private bool _searching;

    public SearchViewModel(LibraryRepository library, CreatorCardFactory cards,
        IThumbnailService? thumbnails = null, IImageLoader? imageLoader = null)
    {
        _library = library;
        _cards = cards;
        _thumbnails = thumbnails;
        _imageLoader = imageLoader;
    }

    public ObservableCollection<CreatorCardViewModel> CreatorResults { get; } = [];
    public ObservableCollection<RecencyCardViewModel> VideoResults { get; } = [];

    private readonly SelectionViewModel<RecencyCardViewModel> _selection = new();

    /// <summary>Per-page selection state for multi-select over the video result group.</summary>
    public SelectionViewModel<RecencyCardViewModel> Selection => _selection;

    // ── IBulkSelectionSource ─────────────────────────────────────────────────
    bool IBulkSelectionSource.HasSelection => Selection.HasSelection;
    IReadOnlyList<long> IBulkSelectionSource.GetSelectedVideoIds() => GetSelectedVideoIds();
    public event EventHandler? SelectionChanged;
    void IBulkSelectionSource.ClearSelection() => Selection.ClearSelectionCommand.Execute(null);
    void IBulkSelectionSource.ExitSelectionMode() => Selection.ExitSelectionModeCommand.Execute(null);

    /// <summary>Returns video ids for all currently selected video-result cards.</summary>
    public IReadOnlyList<long> GetSelectedVideoIds()
        => Selection.SelectedItems.Select(c => c.VideoId).ToList();

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
                UnsubscribeVideoResults();
                CreatorResults.Clear();
                VideoResults.Clear();
                Selection.ExitSelectionModeCommand.Execute(null);
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
            UnsubscribeVideoResults();
            Selection.ExitSelectionModeCommand.Execute(null);
            VideoResults.Clear();
            foreach (var v in videos)
            {
                var card = MakeVideoCard(v);
                card.PropertyChanged += OnVideoCardPropertyChanged;
                VideoResults.Add(card);
            }

            _searching = false;
            RaiseFlags();
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer keystroke — swallow (no unobserved fault)
        }
    }

    private void UnsubscribeVideoResults()
    {
        foreach (var card in VideoResults)
            card.PropertyChanged -= OnVideoCardPropertyChanged;
    }

    private void OnVideoCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecencyCardViewModel.IsSelected) &&
            sender is RecencyCardViewModel card)
        {
            Selection.OnItemSelectionChanged(card);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
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
        var card = new RecencyCardViewModel(i, _thumbnails, _imageLoader);
        card.PlayInvoked += (_, _) => RaisePlay(i.SeriesId, i.VideoId);
        _ = card.LoadImageAsync(CancellationToken.None);
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
