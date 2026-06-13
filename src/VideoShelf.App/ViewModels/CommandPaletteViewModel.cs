using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Search;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>
/// ViewModel for the Ctrl+K command palette overlay.
/// Aggregates actions, creators, and videos into one ranked list.
/// The palette is debounced (120ms) and runs the DB search off-thread,
/// then marshals results back to the UI thread before mutating Results.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private const int DbResultLimit = 10;
    private const int DebounceMs   = 120;

    private readonly LibraryRepository _library;

    // Lazy factory: called on first query so MainViewModel is fully constructed.
    private readonly Func<IReadOnlyList<(string Label, string Icon, Action Execute)>> _actionRegistryFactory;
    private IReadOnlyList<(string Label, string Icon, Action Execute)>? _actionsCache;
    private IReadOnlyList<(string Label, string Icon, Action Execute)> Actions
        => _actionsCache ??= _actionRegistryFactory();

    // Debounce — mirrors SearchViewModel's CTS pattern.
    private CancellationTokenSource? _opCts;
    private Task _pending = Task.CompletedTask;

    // Called when the palette wants to navigate to a creator or play a video —
    // wired by MainViewModel.
    private readonly Func<long, Task> _openSection;
    private readonly Action<long>     _playVideo;   // videoId → GetEpisode → PlayEpisode in MVM

    // Raised when the palette executes an item and should close itself.
    public event EventHandler? CloseRequested;

    public CommandPaletteViewModel(
        LibraryRepository library,
        Func<IReadOnlyList<(string Label, string Icon, Action Execute)>> actionRegistryFactory,
        Func<long, Task> openSection,
        Action<long> playVideo)
    {
        _library               = library;
        _actionRegistryFactory = actionRegistryFactory;
        _openSection           = openSection;
        _playVideo             = playVideo;
    }

    // ── Public state ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private int _selectedIndex = -1;

    /// <summary>The flat, ranked result list shown by the overlay.</summary>
    public ObservableCollection<PaletteItemViewModel> Results { get; } = [];

    // ── Query change → debounced search ──────────────────────────────────────

    partial void OnQueryChanged(string value)
    {
        _opCts?.Cancel();
        var cts = _opCts = new CancellationTokenSource();
        _pending = RunAsync(value, cts.Token);
    }

    private async Task RunAsync(string query, CancellationToken ct)
    {
        try
        {
            await Task.Delay(DebounceMs, ct);

            // Run DB reads off the UI thread.
            var q = query.Trim();
            var (creators, videos) = string.IsNullOrEmpty(q)
                ? ([], [])
                : await Task.Run(
                    () => (_library.SearchCreators(q, DbResultLimit),
                           _library.SearchVideos(q, DbResultLimit)),
                    ct);

            ct.ThrowIfCancellationRequested();

            // ── Back on the UI thread — mutate the observable collection ─────
            Results.Clear();

            var items = new List<PaletteItemViewModel>();

            // 1. Score and filter actions.
            if (!string.IsNullOrEmpty(q))
            {
                foreach (var (label, icon, exec) in Actions)
                {
                    var s = PaletteRanker.Score(q, label);
                    if (s > 0)
                    {
                        var captured = exec;  // capture for lambda
                        items.Add(new PaletteItemViewModel(label, icon, PaletteItemKind.Action, captured, s));
                    }
                }
            }
            else
            {
                // Empty query → show all actions ordered by label.
                foreach (var (label, icon, exec) in Actions.OrderBy(a => a.Label))
                {
                    var captured = exec;
                    items.Add(new PaletteItemViewModel(label, icon, PaletteItemKind.Action, captured, 1.0));
                }
            }

            // 2. Creator DB results (DB already filtered; rank by label score for ordering).
            foreach (var c in creators)
            {
                var s = string.IsNullOrEmpty(q) ? 1.0 : PaletteRanker.Score(q, c.DisplayName);
                var sectionId = c.SectionId;
                items.Add(new PaletteItemViewModel(
                    c.DisplayName, "Apps24", PaletteItemKind.Creator,
                    () => _ = _openSection(sectionId),
                    Math.Max(s, 0.01)));  // always include DB hits (already filtered by LIKE)
            }

            // 3. Video DB results.
            foreach (var v in videos)
            {
                var label = v.IsStandalone ? v.SeriesTitle : $"{v.SeriesTitle} — Ep {v.EpisodeNo}";
                var s = string.IsNullOrEmpty(q) ? 1.0 : PaletteRanker.Score(q, label);
                var videoId = v.VideoId;
                items.Add(new PaletteItemViewModel(
                    label, "Play24", PaletteItemKind.Video,
                    () => _playVideo(videoId),
                    Math.Max(s, 0.01),
                    subLabel: v.IsStandalone ? null : v.SeriesTitle));
            }

            // 4. Sort: actions first (by score desc), then creators, then videos —
            //    within each kind sort by score descending.
            var sorted = items
                .OrderBy(i => i.Kind)
                .ThenByDescending(i => i.Score)
                .ToList();

            foreach (var item in sorted)
                Results.Add(item);

            SelectedIndex = Results.Count > 0 ? 0 : -1;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke — swallow.
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Executes the given item and closes the palette.</summary>
    [RelayCommand]
    private void ExecuteItem(PaletteItemViewModel item)
    {
        item.Execute();
        Close();
    }

    /// <summary>Executes the currently selected item (Enter key).</summary>
    [RelayCommand]
    private void ExecuteSelected()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count)
            ExecuteItem(Results[SelectedIndex]);
    }

    /// <summary>Move selection one row up.</summary>
    [RelayCommand]
    private void MoveUp()
    {
        if (Results.Count == 0) return;
        SelectedIndex = SelectedIndex <= 0
            ? Results.Count - 1
            : SelectedIndex - 1;
    }

    /// <summary>Move selection one row down.</summary>
    [RelayCommand]
    private void MoveDown()
    {
        if (Results.Count == 0) return;
        SelectedIndex = SelectedIndex >= Results.Count - 1
            ? 0
            : SelectedIndex + 1;
    }

    /// <summary>Close the palette without executing anything (Esc).</summary>
    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reset state when the palette is shown (called by MainViewModel.OpenCommandPalette).</summary>
    public void Reset()
    {
        // Cancel any in-flight search.
        _opCts?.Cancel();
        _opCts = null;
        Query = string.Empty;
        Results.Clear();
        SelectedIndex = -1;
    }

    /// <summary>Test hook: wait for the current in-flight query to finish.</summary>
    public Task WaitForIdleAsync() => _pending;
}
