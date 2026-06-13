using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    public SmartViewListItemViewModel(SmartView model)
    {
        Model = model;
        Id = model.Id;
        Name = model.Name;
        ShowOnHome = model.ShowOnHome;
        RuleSummary = BuildSummary(model.Definition);
    }

    private static string BuildSummary(SmartViewDefinition def)
    {
        if (def.Rules.Count == 0) return "(no rules)";
        var match = def.Match == "all" ? "all of" : "any of";
        var parts = def.Rules.Select(r => $"{r.Field} {r.Op} {r.Value}");
        return $"{match}: {string.Join(", ", parts)}";
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
    }

    // ── Views list ───────────────────────────────────────────────────────────

    public ObservableCollection<SmartViewListItemViewModel> Views { get; } = new();

    // ── Builder state ────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private bool _matchAll = true;

    [ObservableProperty]
    private bool _editShowOnHome = true;

    [ObservableProperty]
    private long? _editingId = null;

    public ObservableCollection<SmartRuleRowViewModel> EditRules { get; } = new();

    // ── Available pickers (optional helpers for the UI) ───────────────────────

    public IReadOnlyList<string> AvailableTags => _tags.GetAllTagsAcrossLevels();

    public IReadOnlyList<string> AvailableCreators =>
        _library.GetSectionSummaries().Select(s => s.DisplayName).ToList();

    // ── Load ─────────────────────────────────────────────────────────────────

    public void Load()
    {
        Views.Clear();
        foreach (var sv in _smartViews.GetAll())
            Views.Add(new SmartViewListItemViewModel(sv));
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
}
