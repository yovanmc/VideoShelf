using System.Collections.Generic;
using System.ComponentModel;
using Shouldly;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Tests;

/// <summary>Pure-VM tests for SelectionViewModel&lt;T&gt;. No UI, no DB.</summary>
public class SelectionViewModelTests
{
    // Minimal ISelectableCard stub for testing.
    private sealed class TestCard : ISelectableCard
    {
        private bool _isSelected;
        public string Name { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public TestCard(string name) => Name = name;
    }

    private static SelectionViewModel<TestCard> MakeVm(out TestCard[] cards)
    {
        var vm = new SelectionViewModel<TestCard>();
        cards = [new TestCard("A"), new TestCard("B"), new TestCard("C")];
        return vm;
    }

    // -----------------------------------------------------------------
    // EnterSelectionMode / ExitSelectionMode
    // -----------------------------------------------------------------

    [Fact]
    public void EnterSelectionMode_sets_IsSelectionMode_true()
    {
        var vm = new SelectionViewModel<TestCard>();

        vm.EnterSelectionModeCommand.Execute(null);

        vm.IsSelectionMode.ShouldBeTrue();
    }

    [Fact]
    public void ExitSelectionMode_clears_mode_and_selection()
    {
        var vm = MakeVm(out var cards);
        vm.IsSelectionMode = true;
        cards[0].IsSelected = true;
        vm.OnItemSelectionChanged(cards[0]);

        vm.ExitSelectionModeCommand.Execute(null);

        vm.IsSelectionMode.ShouldBeFalse();
        vm.SelectedItems.ShouldBeEmpty();
        vm.SelectedCount.ShouldBe(0);
        vm.HasSelection.ShouldBeFalse();
    }

    [Fact]
    public void Setting_IsSelectionMode_false_directly_clears_selection()
    {
        // The OnIsSelectionModeChanged partial should fire ClearSelection.
        var vm = MakeVm(out var cards);
        vm.IsSelectionMode = true;
        cards[0].IsSelected = true;
        vm.OnItemSelectionChanged(cards[0]);
        vm.SelectedItems.Count.ShouldBe(1);

        vm.IsSelectionMode = false;

        vm.SelectedItems.ShouldBeEmpty();
        cards[0].IsSelected.ShouldBeFalse();
    }

    // -----------------------------------------------------------------
    // OnItemSelectionChanged — add / remove
    // -----------------------------------------------------------------

    [Fact]
    public void Selecting_item_adds_it_to_SelectedItems()
    {
        var vm = MakeVm(out var cards);
        cards[0].IsSelected = true;

        vm.OnItemSelectionChanged(cards[0]);

        vm.SelectedItems.ShouldContain(cards[0]);
        vm.SelectedCount.ShouldBe(1);
        vm.HasSelection.ShouldBeTrue();
    }

    [Fact]
    public void Deselecting_item_removes_it_from_SelectedItems()
    {
        var vm = MakeVm(out var cards);
        cards[0].IsSelected = true;
        vm.OnItemSelectionChanged(cards[0]);

        cards[0].IsSelected = false;
        vm.OnItemSelectionChanged(cards[0]);

        vm.SelectedItems.ShouldNotContain(cards[0]);
        vm.SelectedCount.ShouldBe(0);
        vm.HasSelection.ShouldBeFalse();
    }

    [Fact]
    public void Selecting_same_item_twice_does_not_duplicate()
    {
        var vm = MakeVm(out var cards);
        cards[0].IsSelected = true;

        vm.OnItemSelectionChanged(cards[0]);
        vm.OnItemSelectionChanged(cards[0]); // duplicate call

        vm.SelectedItems.Count.ShouldBe(1);
    }

    [Fact]
    public void Multiple_items_accumulate_independently()
    {
        var vm = MakeVm(out var cards);
        cards[0].IsSelected = true;
        cards[2].IsSelected = true;

        vm.OnItemSelectionChanged(cards[0]);
        vm.OnItemSelectionChanged(cards[2]);

        vm.SelectedCount.ShouldBe(2);
        vm.SelectedItems.ShouldContain(cards[0]);
        vm.SelectedItems.ShouldContain(cards[2]);
        vm.SelectedItems.ShouldNotContain(cards[1]);
    }

    // -----------------------------------------------------------------
    // SelectAll
    // -----------------------------------------------------------------

    [Fact]
    public void SelectAll_marks_all_items_selected()
    {
        var vm = MakeVm(out var cards);

        vm.SelectAllCommand.Execute(new List<TestCard>(cards));

        foreach (var card in cards)
            card.IsSelected.ShouldBeTrue();
    }

    [Fact]
    public void SelectAll_with_null_parameter_is_a_no_op()
    {
        var vm = MakeVm(out _);

        Should.NotThrow(() => vm.SelectAllCommand.Execute(null));
    }

    // -----------------------------------------------------------------
    // ClearSelection
    // -----------------------------------------------------------------

    [Fact]
    public void ClearSelection_deselects_all_and_empties_SelectedItems()
    {
        var vm = MakeVm(out var cards);
        cards[0].IsSelected = true;
        cards[1].IsSelected = true;
        vm.OnItemSelectionChanged(cards[0]);
        vm.OnItemSelectionChanged(cards[1]);

        vm.ClearSelectionCommand.Execute(null);

        vm.SelectedItems.ShouldBeEmpty();
        cards[0].IsSelected.ShouldBeFalse();
        cards[1].IsSelected.ShouldBeFalse();
        vm.HasSelection.ShouldBeFalse();
    }

    // -----------------------------------------------------------------
    // InvertSelection
    // -----------------------------------------------------------------

    [Fact]
    public void InvertSelection_flips_each_item()
    {
        var vm = MakeVm(out var cards);
        cards[0].IsSelected = true;  // was selected → will become unselected
        cards[1].IsSelected = false; // was not → will become selected
        cards[2].IsSelected = true;

        vm.InvertSelectionCommand.Execute(new List<TestCard>(cards));

        cards[0].IsSelected.ShouldBeFalse();
        cards[1].IsSelected.ShouldBeTrue();
        cards[2].IsSelected.ShouldBeFalse();
    }

    // -----------------------------------------------------------------
    // Property-change notifications
    // -----------------------------------------------------------------

    [Fact]
    public void SelectedCount_raises_PropertyChanged_when_item_added()
    {
        var vm = MakeVm(out var cards);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        cards[0].IsSelected = true;

        vm.OnItemSelectionChanged(cards[0]);

        raised.ShouldContain(nameof(SelectionViewModel<TestCard>.SelectedCount));
        raised.ShouldContain(nameof(SelectionViewModel<TestCard>.HasSelection));
    }

    [Fact]
    public void IsSelectionMode_raises_PropertyChanged()
    {
        var vm = new SelectionViewModel<TestCard>();
        string? changedProp = null;
        vm.PropertyChanged += (_, e) => changedProp = e.PropertyName;

        vm.IsSelectionMode = true;

        changedProp.ShouldBe(nameof(SelectionViewModel<TestCard>.IsSelectionMode));
    }

    // -----------------------------------------------------------------
    // Re-entrancy regression tests (production wiring)
    // Wire cards exactly like CreatorsViewModel.OnCardPropertyChanged:
    // each card's PropertyChanged → selection.OnItemSelectionChanged(card)
    // Setting IsSelected=false re-enters and removes from SelectedItems.
    // Without the ToList() snapshot this throws InvalidOperationException.
    // -----------------------------------------------------------------

    private static SelectionViewModel<TestCard> MakeWiredVm(out TestCard[] cards)
    {
        var vm = new SelectionViewModel<TestCard>();
        cards = [new TestCard("X"), new TestCard("Y")];
        // Mirror the production subscription in CreatorsViewModel.OnCardPropertyChanged:
        foreach (var card in cards)
        {
            var captured = card;
            captured.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TestCard.IsSelected))
                    vm.OnItemSelectionChanged(captured);
            };
        }
        return vm;
    }

    [Fact]
    public void ClearSelection_with_production_wiring_does_not_throw_and_empties_selection()
    {
        // Arrange — select two items via the wired path so SelectedItems contains both.
        var vm = MakeWiredVm(out var cards);
        cards[0].IsSelected = true; // triggers OnItemSelectionChanged via PropertyChanged
        cards[1].IsSelected = true;
        vm.SelectedItems.Count.ShouldBe(2);

        // Act — without the ToList() snapshot this throws InvalidOperationException
        // because OnItemSelectionChanged→SelectedItems.Remove fires mid-iteration.
        Exception? ex = null;
        try { vm.ClearSelectionCommand.Execute(null); }
        catch (Exception e) { ex = e; }

        // Assert
        ex.ShouldBeNull("ClearSelection must not throw even when PropertyChanged removes from SelectedItems mid-iteration");
        vm.SelectedItems.ShouldBeEmpty();
        cards[0].IsSelected.ShouldBeFalse();
        cards[1].IsSelected.ShouldBeFalse();
        vm.HasSelection.ShouldBeFalse();
        vm.SelectedCount.ShouldBe(0);
    }

    [Fact]
    public void ExitSelectionMode_with_production_wiring_does_not_throw_and_empties_selection()
    {
        // Arrange
        var vm = MakeWiredVm(out var cards);
        vm.IsSelectionMode = true;
        cards[0].IsSelected = true;
        cards[1].IsSelected = true;
        vm.SelectedItems.Count.ShouldBe(2);

        // Act — ExitSelectionMode sets IsSelectionMode=false which calls ClearSelection.
        Exception? ex = null;
        try { vm.ExitSelectionModeCommand.Execute(null); }
        catch (Exception e) { ex = e; }

        // Assert
        ex.ShouldBeNull("ExitSelectionMode must not throw even with production PropertyChanged wiring");
        vm.IsSelectionMode.ShouldBeFalse();
        vm.SelectedItems.ShouldBeEmpty();
        cards[0].IsSelected.ShouldBeFalse();
        cards[1].IsSelected.ShouldBeFalse();
    }
}
