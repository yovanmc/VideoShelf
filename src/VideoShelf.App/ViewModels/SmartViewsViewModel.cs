using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

// ── SmartRuleRowViewModel ────────────────────────────────────────────────────

public sealed partial class SmartRuleRowViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> FieldOptions =
        new[] { "tag", "creator", "watched", "dateAdded", "duration" };

    private static ObservableCollection<string> OpsFor(string field) => field switch
    {
        "tag"       => new ObservableCollection<string> { "is", "isNot" },
        "creator"   => new ObservableCollection<string> { "is", "isNot" },
        "watched"   => new ObservableCollection<string> { "is" },
        "dateAdded" => new ObservableCollection<string> { "withinDays", "beforeDays" },
        "duration"  => new ObservableCollection<string> { "gt", "lt" },
        _           => new ObservableCollection<string> { "is" },
    };

    [ObservableProperty]
    private string _field = "tag";

    [ObservableProperty]
    private string _op = "is";

    [ObservableProperty]
    private string _value = string.Empty;

    public ObservableCollection<string> OpOptions { get; private set; } = OpsFor("tag");

    partial void OnFieldChanged(string value)
    {
        OpOptions = OpsFor(value);
        OnPropertyChanged(nameof(OpOptions));
        Op = OpOptions[0];
    }
}

// ── SmartViewListItemViewModel ───────────────────────────────────────────────

public sealed class SmartViewListItemViewModel
{
    public long Id { get; }
    public string Name { get; }
    public string RuleSummary { get; }
    public bool ShowOnHome { get; }
    public SmartView Model { get; }

    public SmartViewListItemViewModel(SmartView model,
        IReadOnlyDictionary<long, string>? creatorNames = null)
    {
        Model = model;
        Id = model.Id;
        Name = model.Name;
        ShowOnHome = model.ShowOnHome;
        RuleSummary = SmartRuleProse.Describe(model.Definition, creatorNames);
    }
}

// ── SmartViewsViewModel ──────────────────────────────────────────────────────

public sealed partial class SmartViewsViewModel : ObservableObject
{
    private readonly SmartViewRepository _smartViews;
    private readonly TagRepository _tags;
    private readonly LibraryRepository _library;

    public SmartViewsViewModel(SmartViewRepository smartViews, TagRepository tags, LibraryRepository library)
    {
        _smartViews = smartViews;
        _tags = tags;
        _library = library;

        // Subscribe to EditRules collection changes so we can track per-row property changes
        // and recompute the live match count.
        EditRules.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (SmartRuleRowViewModel row in e.NewItems)
                    row.PropertyChanged += OnRuleRowPropertyChanged;

            if (e.OldItems != null)
                foreach (SmartRuleRowViewModel row in e.OldItems)
                    row.PropertyChanged -= OnRuleRowPropertyChanged;

            RefreshMatchCount();
        };
    }

    // ── Views list ───────────────────────────────────────────────────────────

    public ObservableCollection<SmartViewListItemViewModel> Views { get; } = new();

    /// <summary>True while Load is in progress; used to show the skeleton overlay.
    /// Synchronous loads complete so fast this rarely stays true for a visible frame — that's fine.</summary>
    [ObservableProperty]
    private bool _isLoading;

    // ── Builder state ────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private bool _matchAll = true;

    partial void OnMatchAllChanged(bool value) => RefreshMatchCount();

    [ObservableProperty]
    private bool _editShowOnHome = true;

    [ObservableProperty]
    private long? _editingId = null;

    /// <summary>Live "Matches N videos" string shown below the builder rules list.</summary>
    [ObservableProperty]
    private string _matchCount = string.Empty;

    public ObservableCollection<SmartRuleRowViewModel> EditRules { get; } = new();

    // ── Available pickers (optional helpers for the UI) ───────────────────────

    public IReadOnlyList<string> AvailableTags => _tags.GetAllTagsAcrossLevels();

    public IReadOnlyList<string> AvailableCreators =>
        _library.GetSectionSummaries().Select(s => s.DisplayName).ToList();

    // ── Load ─────────────────────────────────────────────────────────────────

    public void Load()
    {
        IsLoading = true;
        try
        {
            var creatorNames = BuildCreatorNameMap();
            Views.Clear();
            foreach (var sv in _smartViews.GetAll())
                Views.Add(new SmartViewListItemViewModel(sv, creatorNames));
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void NewView()
    {
        EditingId = null;
        EditName = string.Empty;
        MatchAll = true;
        EditShowOnHome = true;
        EditRules.Clear();
        EditRules.Add(new SmartRuleRowViewModel());
    }

    [RelayCommand]
    private void AddRule()
    {
        EditRules.Add(new SmartRuleRowViewModel());
    }

    [RelayCommand]
    private void RemoveRule(SmartRuleRowViewModel row)
    {
        EditRules.Remove(row);
    }

    [RelayCommand]
    private void EditView(SmartViewListItemViewModel item)
    {
        EditingId = item.Id;
        EditName = item.Model.Name;
        MatchAll = item.Model.Definition.Match == "all";
        EditShowOnHome = item.Model.ShowOnHome;
        EditRules.Clear();
        foreach (var rule in item.Model.Definition.Rules)
        {
            var row = new SmartRuleRowViewModel { Value = rule.Value };
            // Set Field first so OpOptions recompute, then set Op.
            row.Field = rule.Field;
            row.Op = rule.Op;
            EditRules.Add(row);
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(EditName) || EditRules.Count == 0) return;

        var def = new SmartViewDefinition(
            MatchAll ? "all" : "any",
            EditRules.Select(r => new SmartRule(r.Field, r.Op, r.Value)).ToList());

        if (EditingId is null)
            _smartViews.Create(EditName, def, EditShowOnHome, DateTimeOffset.UtcNow);
        else
            _smartViews.Update(EditingId.Value, EditName, def, EditShowOnHome);

        Load();

        // Clear builder
        EditingId = null;
        EditName = string.Empty;
        MatchAll = true;
        EditShowOnHome = true;
        EditRules.Clear();
    }

    [RelayCommand]
    private void DeleteView(SmartViewListItemViewModel item)
    {
        _smartViews.Delete(item.Id);
        Load();
    }

    [RelayCommand]
    private void MoveUp(SmartViewListItemViewModel item)
    {
        var list = Views.ToList();
        var idx = list.IndexOf(item);
        if (idx <= 0) return;

        // Swap sort orders with the previous item.
        var prev = list[idx - 1];
        _smartViews.Reorder(item.Id, idx - 1);
        _smartViews.Reorder(prev.Id, idx);
        Load();
    }

    [RelayCommand]
    private void MoveDown(SmartViewListItemViewModel item)
    {
        var list = Views.ToList();
        var idx = list.IndexOf(item);
        if (idx < 0 || idx >= list.Count - 1) return;

        // Swap sort orders with the next item.
        var next = list[idx + 1];
        _smartViews.Reorder(item.Id, idx + 1);
        _smartViews.Reorder(next.Id, idx);
        Load();
    }

    // ── Live match count helpers ─────────────────────────────────────────────

    private void OnRuleRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RefreshMatchCount();

    /// <summary>
    /// Recomputes <see cref="MatchCount"/> from the current builder state.
    /// Runs a cheap COUNT(*) query; called on every rule change (no debounce needed).
    /// Clears to empty string when there are no rules (avoids stale "Matches 0 videos" during setup).
    /// </summary>
    private void RefreshMatchCount()
    {
        if (EditRules.Count == 0)
        {
            MatchCount = string.Empty;
            return;
        }

        try
        {
            var def = new SmartViewDefinition(
                MatchAll ? "all" : "any",
                EditRules.Select(r => new SmartRule(r.Field, r.Op, r.Value)).ToList());

            var count = _smartViews.CountMatchingVideos(def, DateTimeOffset.UtcNow);
            MatchCount = $"Matches {count} video{(count == 1 ? "" : "s")}";
        }
        catch
        {
            // Swallow (e.g. invalid value in a rule field during typing); leave previous count.
        }
    }

    /// <summary>Builds an id→display-name map from all sections in the library.</summary>
    private IReadOnlyDictionary<long, string> BuildCreatorNameMap()
    {
        var summaries = _library.GetSectionSummaries();
        var map = new Dictionary<long, string>(summaries.Count);
        foreach (var s in summaries)
            map[s.SectionId] = s.DisplayName;
        return map;
    }
}
