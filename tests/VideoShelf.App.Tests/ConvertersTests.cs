using System.Globalization;
using System.Windows;
using Shouldly;
using VideoShelf.App.Converters;

namespace VideoShelf.App.Tests;

/// <summary>
/// Unit tests for the two converters added in M18-F to fix triage section visibility.
/// </summary>
public sealed class ConvertersTests
{
    // ── NotNullToVisibility ───────────────────────────────────────────────────

    [Fact]
    public void NotNullToVisibility_NullValue_ReturnsCollapsed()
    {
        var conv = new NotNullToVisibility();
        var result = conv.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.ShouldBe(Visibility.Collapsed);
    }

    [Fact]
    public void NotNullToVisibility_NonNullObject_ReturnsVisible()
    {
        var conv = new NotNullToVisibility();
        var result = conv.Convert(new object(), typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.ShouldBe(Visibility.Visible);
    }

    [Fact]
    public void NotNullToVisibility_StringValue_ReturnsVisible()
    {
        var conv = new NotNullToVisibility();
        // Even an empty string is non-null — should be Visible.
        var result = conv.Convert(string.Empty, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.ShouldBe(Visibility.Visible);
    }

    [Fact]
    public void NotNullToVisibility_ViewModelObject_ReturnsVisible()
    {
        // Simulates the real fix: binding Triage (a MissingTriageViewModel instance) → Visible.
        var conv = new NotNullToVisibility();
        // Use a plain object to stand in for any sub-VM.
        var vm = new { Name = "triage" };
        var result = conv.Convert(vm, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.ShouldBe(Visibility.Visible);
    }

    [Fact]
    public void NotNullToVisibility_ConvertBack_Throws()
    {
        var conv = new NotNullToVisibility();
        Should.Throw<NotSupportedException>(() =>
            conv.ConvertBack(Visibility.Visible, typeof(object), null, CultureInfo.InvariantCulture));
    }

    // ── CountToVisibility ─────────────────────────────────────────────────────

    [Fact]
    public void CountToVisibility_ZeroInt_ReturnsCollapsed()
    {
        var conv = new CountToVisibility();
        var result = conv.Convert(0, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.ShouldBe(Visibility.Collapsed);
    }

    [Fact]
    public void CountToVisibility_PositiveInt_ReturnsVisible()
    {
        var conv = new CountToVisibility();
        var result = conv.Convert(3, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.ShouldBe(Visibility.Visible);
    }

    [Fact]
    public void CountToVisibility_OneInt_ReturnsVisible()
    {
        var conv = new CountToVisibility();
        var result = conv.Convert(1, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.ShouldBe(Visibility.Visible);
    }

    [Fact]
    public void CountToVisibility_NullValue_ReturnsCollapsed()
    {
        // Simulates Triage being null — binding Triage.MissingVideos.Count yields UnsetValue/null.
        var conv = new CountToVisibility();
        var result = conv.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.ShouldBe(Visibility.Collapsed);
    }

    [Fact]
    public void CountToVisibility_UnsetValue_ReturnsCollapsed()
    {
        // DependencyProperty.UnsetValue is what WPF propagates when a binding chain has a null segment.
        var conv = new CountToVisibility();
        var result = conv.Convert(DependencyProperty.UnsetValue, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.ShouldBe(Visibility.Collapsed);
    }

    [Fact]
    public void CountToVisibility_NonIntValue_ReturnsCollapsed()
    {
        var conv = new CountToVisibility();
        var result = conv.Convert("five", typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.ShouldBe(Visibility.Collapsed);
    }

    [Fact]
    public void CountToVisibility_ConvertBack_Throws()
    {
        var conv = new CountToVisibility();
        Should.Throw<NotSupportedException>(() =>
            conv.ConvertBack(Visibility.Visible, typeof(object), null, CultureInfo.InvariantCulture));
    }

    // ── HalfStarSymbolConverter ───────────────────────────────────────────────

    [Fact]
    public void HalfStarSymbol_rating3point5_cell4_returns_Half()
    {
        var conv = new HalfStarSymbolConverter();
        var result = conv.Convert(3.5, typeof(object), "4", CultureInfo.InvariantCulture);
        result.ShouldBe(Wpf.Ui.Controls.SymbolRegular.StarHalf24);
    }

    [Fact]
    public void HalfStarSymbol_rating3point5_cell3_returns_Full()
    {
        var conv = new HalfStarSymbolConverter();
        var result = conv.Convert(3.5, typeof(object), "3", CultureInfo.InvariantCulture);
        result.ShouldBe(Wpf.Ui.Controls.SymbolRegular.Star24);
    }

    [Fact]
    public void HalfStarSymbol_rating3point5_cell5_returns_Empty()
    {
        var conv = new HalfStarSymbolConverter();
        var result = conv.Convert(3.5, typeof(object), "5", CultureInfo.InvariantCulture);
        result.ShouldBe(Wpf.Ui.Controls.SymbolRegular.StarOff24);
    }

    [Fact]
    public void HalfStarSymbol_rating5_cell5_returns_Full()
    {
        var conv = new HalfStarSymbolConverter();
        var result = conv.Convert(5.0, typeof(object), "5", CultureInfo.InvariantCulture);
        result.ShouldBe(Wpf.Ui.Controls.SymbolRegular.Star24);
    }

    [Fact]
    public void HalfStarSymbol_rating0_cell1_returns_Empty()
    {
        var conv = new HalfStarSymbolConverter();
        var result = conv.Convert(0.0, typeof(object), "1", CultureInfo.InvariantCulture);
        result.ShouldBe(Wpf.Ui.Controls.SymbolRegular.StarOff24);
    }

    [Fact]
    public void HalfStarSymbol_rating0point5_cell1_returns_Half()
    {
        var conv = new HalfStarSymbolConverter();
        var result = conv.Convert(0.5, typeof(object), "1", CultureInfo.InvariantCulture);
        result.ShouldBe(Wpf.Ui.Controls.SymbolRegular.StarHalf24);
    }
}
