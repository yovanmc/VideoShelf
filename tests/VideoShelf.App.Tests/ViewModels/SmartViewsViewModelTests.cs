using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests.ViewModels;

public sealed class SmartViewsViewModelTests
{
    // ── Fixture ──────────────────────────────────────────────────────────────

    private sealed record Fx(
        AppTempDb Db,
        LibraryRepository Lib,
        TagRepository Tags,
        SmartViewRepository SmartViews,
        SmartViewsViewModel Vm);

    private static Fx NewFx()
    {
        var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var tags = new TagRepository(db.Db);
        var svr = new SmartViewRepository(db.Db);
        var vm = new SmartViewsViewModel(svr, tags, lib);
        return new Fx(db, lib, tags, svr, vm);
    }

    // ── NewView resets builder ────────────────────────────────────────────────

    [Fact]
    public void NewView_resets_builder_with_one_blank_rule()
    {
        var f = NewFx(); using var _d = f.Db;

        // Dirty the builder first.
        f.Vm.EditName = "dirty";
        f.Vm.AddRuleCommand.Execute(null);
        f.Vm.AddRuleCommand.Execute(null); // 2 rules added

        f.Vm.NewViewCommand.Execute(null);

        f.Vm.EditingId.ShouldBeNull();
        f.Vm.EditName.ShouldBe(string.Empty);
        f.Vm.MatchAll.ShouldBeTrue();
        f.Vm.EditShowOnHome.ShouldBeTrue();
        f.Vm.EditRules.Count.ShouldBe(1);
    }

    // ── AddRule / RemoveRule ──────────────────────────────────────────────────

    [Fact]
    public void AddRule_increases_EditRules_count()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Vm.NewViewCommand.Execute(null); // starts with 1

        f.Vm.AddRuleCommand.Execute(null);

        f.Vm.EditRules.Count.ShouldBe(2);
    }

    [Fact]
    public void RemoveRule_decreases_EditRules_count()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Vm.NewViewCommand.Execute(null); // 1 rule
        f.Vm.AddRuleCommand.Execute(null); // 2 rules

        f.Vm.RemoveRuleCommand.Execute(f.Vm.EditRules[0]);

        f.Vm.EditRules.Count.ShouldBe(1);
    }

    // ── Field change updates OpOptions ────────────────────────────────────────

    [Fact]
    public void Changing_Field_to_watched_sets_OpOptions_to_is_only()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Vm.NewViewCommand.Execute(null);
        var row = f.Vm.EditRules[0];

        row.Field = "watched";

        row.OpOptions.ShouldBe(new[] { "is" });
        row.Op.ShouldBe("is");
    }

    [Fact]
    public void Changing_Field_to_dateAdded_sets_OpOptions_to_day_ops()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Vm.NewViewCommand.Execute(null);
        var row = f.Vm.EditRules[0];

        row.Field = "dateAdded";

        row.OpOptions.ShouldBe(new[] { "withinDays", "beforeDays" });
        row.Op.ShouldBe("withinDays");
    }

    [Fact]
    public void Changing_Field_to_duration_sets_OpOptions_gt_lt()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Vm.NewViewCommand.Execute(null);
        var row = f.Vm.EditRules[0];

        row.Field = "duration";

        row.OpOptions.ShouldBe(new[] { "gt", "lt" });
        row.Op.ShouldBe("gt");
    }

    // ── Save (create) persists ────────────────────────────────────────────────

    [Fact]
    public void Save_create_persists_smart_view_with_correct_match_and_rules()
    {
        var f = NewFx(); using var _d = f.Db;

        f.Vm.NewViewCommand.Execute(null);
        f.Vm.EditName = "Unwatched";
        f.Vm.MatchAll = true;
        var row = f.Vm.EditRules[0];
        row.Field = "watched";
        row.Op = "is";
        row.Value = "false";
        f.Vm.EditShowOnHome = false;

        f.Vm.SaveCommand.Execute(null);

        // Verify via a fresh repository.
        var fresh = new SmartViewRepository(f.Db.Db);
        var all = fresh.GetAll();
        all.Count.ShouldBe(1);
        all[0].Name.ShouldBe("Unwatched");
        all[0].Definition.Match.ShouldBe("all");
        all[0].Definition.Rules.Count.ShouldBe(1);
        all[0].Definition.Rules[0].Field.ShouldBe("watched");
        all[0].Definition.Rules[0].Op.ShouldBe("is");
        all[0].Definition.Rules[0].Value.ShouldBe("false");
        all[0].ShowOnHome.ShouldBeFalse();
    }

    [Fact]
    public void Save_create_clears_builder_and_refreshes_Views()
    {
        var f = NewFx(); using var _d = f.Db;

        f.Vm.NewViewCommand.Execute(null);
        f.Vm.EditName = "Test";
        f.Vm.EditRules[0].Field = "tag";
        f.Vm.EditRules[0].Op = "is";
        f.Vm.EditRules[0].Value = "anime";

        f.Vm.SaveCommand.Execute(null);

        // Builder cleared.
        f.Vm.EditName.ShouldBe(string.Empty);
        f.Vm.EditingId.ShouldBeNull();
        // Views refreshed.
        f.Vm.Views.Count.ShouldBe(1);
        f.Vm.Views[0].Name.ShouldBe("Test");
    }

    // ── EditView loads into builder, Save updates ─────────────────────────────

    [Fact]
    public void EditView_loads_existing_view_into_builder_and_Save_updates_it()
    {
        var f = NewFx(); using var _d = f.Db;

        // Create a view first.
        f.Vm.NewViewCommand.Execute(null);
        f.Vm.EditName = "Original";
        f.Vm.EditRules[0].Field = "tag";
        f.Vm.EditRules[0].Op = "is";
        f.Vm.EditRules[0].Value = "drama";
        f.Vm.SaveCommand.Execute(null);

        // Load it.
        f.Vm.Load();
        var item = f.Vm.Views[0];
        f.Vm.EditViewCommand.Execute(item);

        f.Vm.EditingId.ShouldNotBeNull();
        f.Vm.EditName.ShouldBe("Original");
        f.Vm.EditRules.Count.ShouldBe(1);
        f.Vm.EditRules[0].Field.ShouldBe("tag");

        // Modify and save.
        f.Vm.EditName = "Updated";
        f.Vm.EditRules[0].Value = "comedy";
        f.Vm.SaveCommand.Execute(null);

        var all = new SmartViewRepository(f.Db.Db).GetAll();
        all.Count.ShouldBe(1);
        all[0].Name.ShouldBe("Updated");
        all[0].Definition.Rules[0].Value.ShouldBe("comedy");
    }

    // ── DeleteView ────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteView_removes_it_from_the_database_and_Views()
    {
        var f = NewFx(); using var _d = f.Db;

        f.Vm.NewViewCommand.Execute(null);
        f.Vm.EditName = "ToDelete";
        f.Vm.EditRules[0].Value = "x";
        f.Vm.SaveCommand.Execute(null);
        f.Vm.Load();
        f.Vm.Views.Count.ShouldBe(1);

        f.Vm.DeleteViewCommand.Execute(f.Vm.Views[0]);

        f.Vm.Views.ShouldBeEmpty();
        new SmartViewRepository(f.Db.Db).GetAll().ShouldBeEmpty();
    }

    // ── MoveUp / MoveDown ─────────────────────────────────────────────────────

    [Fact]
    public void MoveDown_reorders_item_after_its_successor()
    {
        var f = NewFx(); using var _d = f.Db;

        // Create two views.
        f.Vm.NewViewCommand.Execute(null);
        f.Vm.EditName = "Alpha";
        f.Vm.EditRules[0].Value = "a";
        f.Vm.SaveCommand.Execute(null);

        f.Vm.NewViewCommand.Execute(null);
        f.Vm.EditName = "Beta";
        f.Vm.EditRules[0].Value = "b";
        f.Vm.SaveCommand.Execute(null);

        f.Vm.Load();
        f.Vm.Views[0].Name.ShouldBe("Alpha");

        f.Vm.MoveDownCommand.Execute(f.Vm.Views[0]); // move Alpha down

        f.Vm.Views[0].Name.ShouldBe("Beta");
        f.Vm.Views[1].Name.ShouldBe("Alpha");
    }

    [Fact]
    public void MoveUp_reorders_item_before_its_predecessor()
    {
        var f = NewFx(); using var _d = f.Db;

        f.Vm.NewViewCommand.Execute(null);
        f.Vm.EditName = "Alpha";
        f.Vm.EditRules[0].Value = "a";
        f.Vm.SaveCommand.Execute(null);

        f.Vm.NewViewCommand.Execute(null);
        f.Vm.EditName = "Beta";
        f.Vm.EditRules[0].Value = "b";
        f.Vm.SaveCommand.Execute(null);

        f.Vm.Load();

        f.Vm.MoveUpCommand.Execute(f.Vm.Views[1]); // move Beta up

        f.Vm.Views[0].Name.ShouldBe("Beta");
        f.Vm.Views[1].Name.ShouldBe("Alpha");
    }

    // ── Save skips when blank name / no rules ──────────────────────────────────

    [Fact]
    public void Save_is_no_op_when_name_is_blank()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Vm.NewViewCommand.Execute(null);
        // EditName is blank by default after NewView.
        f.Vm.EditRules[0].Value = "something";

        f.Vm.SaveCommand.Execute(null);

        new SmartViewRepository(f.Db.Db).GetAll().ShouldBeEmpty();
    }

    [Fact]
    public void Save_is_no_op_when_EditRules_is_empty()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Vm.NewViewCommand.Execute(null);
        f.Vm.EditName = "NoRules";
        f.Vm.RemoveRuleCommand.Execute(f.Vm.EditRules[0]); // remove the single blank rule

        f.Vm.SaveCommand.Execute(null);

        new SmartViewRepository(f.Db.Db).GetAll().ShouldBeEmpty();
    }
}
