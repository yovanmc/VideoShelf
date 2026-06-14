using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Motion;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SourcesViewModel : ObservableObject
{
    private readonly LibraryRepository _library;
    private readonly IFolderPicker _picker;
    private readonly IConfirmService _confirm;
    private readonly IToastService? _toasts;

    /// <summary>
    /// <paramref name="confirm"/> is optional so existing test call-sites that pass only
    /// (library, picker) continue to compile. When omitted the <see cref="AlwaysConfirmService"/>
    /// fallback is used — removes proceed without a dialog, matching pre-E2 behaviour for legacy tests.
    /// Production DI passes the real <see cref="ConfirmService"/>.
    /// </summary>
    public SourcesViewModel(
        LibraryRepository library,
        IFolderPicker picker,
        IConfirmService? confirm = null,
        IToastService? toasts = null)
    {
        _library = library;
        _picker  = picker;
        _confirm = confirm ?? AlwaysConfirmService.Instance;
        _toasts  = toasts;
    }

    // ── Private sentinel: always-confirm for legacy test paths ────────────────
    private sealed class AlwaysConfirmService : IConfirmService
    {
        public static readonly AlwaysConfirmService Instance = new();
        public bool Confirm(string title, string message) => true;
    }

    // ── Observable state ──────────────────────────────────────────────────────

    public ObservableCollection<Source> Sources { get; } = [];

    /// <summary>
    /// Optional callback invoked after undo-remove re-adds a source.
    /// MainViewModel wires this to trigger a full scan+reload.
    /// Unit tests that only verify the source-list seam leave it null.
    /// </summary>
    public Action? OnSourceRestored { get; set; }

    // Snapshot held between RemoveSource and UndoRemove.
    private Source? _removedSource;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoRemoveCommand))]
    private bool _canUndoRemove;

    // ── Commands ──────────────────────────────────────────────────────────────

    public void Load()
    {
        Sources.Clear();
        foreach (var s in _library.GetSources())
            Sources.Add(s);
    }

    [RelayCommand]
    private void AddSource()
    {
        var folder = _picker.PickFolder();
        if (string.IsNullOrWhiteSpace(folder))
            return;

        var displayName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar))
            is { Length: > 0 } name ? name : folder;
        _library.UpsertSource(folder, displayName);
        Load();
    }

    [RelayCommand]
    private void RemoveSource(Source? source)
    {
        if (source is null)
            return;

        // E2: gate removal behind a confirm dialog (DB-index-only — never touches the filesystem).
        if (!_confirm.Confirm(
                "Remove source",
                "Remove this source? Your video files are not deleted."))
            return;

        // Snapshot before removal so UndoRemove can re-add.
        _removedSource = source;
        _library.RemoveSource(source.Id);
        Load();
        CanUndoRemove = true;
        _toasts?.Show("Source removed", undo: () => UndoRemoveCommand.Execute(null), ToastKind.Warning);
    }

    [RelayCommand(CanExecute = nameof(CanUndoRemove))]
    private void UndoRemove()
    {
        if (_removedSource is null)
            return;

        _library.UpsertSource(_removedSource.RootPath, _removedSource.DisplayName);
        _removedSource = null;
        Load();
        CanUndoRemove = false;

        // Trigger rescan in the parent shell (MainViewModel wires this up at startup).
        OnSourceRestored?.Invoke();
    }
}
