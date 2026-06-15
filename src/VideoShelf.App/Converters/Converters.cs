using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using Wpf.Ui.Controls;

namespace VideoShelf.App.Converters;

/// <summary>Returns Visible when the bound enum value's ToString() matches the converter parameter string; else Collapsed.</summary>
public sealed class EnumToVisibility : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value?.ToString() == p?.ToString() ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>Visible when the bound enum's name is in the comma-separated ConverterParameter set
/// (e.g. "Browse,SectionDetail,RenameTool"); used for active top-nav highlighting.</summary>
public sealed class EnumSetToVisibility : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var cur = value?.ToString();
        var set = (p?.ToString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return System.Array.IndexOf(set, cur) >= 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}

public sealed class BoolToVisibility : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}

public sealed class MissingToOpacity : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? 0.45 : 1.0;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}

public sealed class SortModeToIndex : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is BrowseSort s ? (int)s : 0;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => value is int i ? (BrowseSort)i : BrowseSort.Name;
}

/// <summary>Inverts a boolean value.</summary>
public sealed class InvertBool : IValueConverter
{
    public static readonly InvertBool Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is not true;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => value is not true;
}

/// <summary>
/// Returns Star24 when the bound rating (int or double) >= the ConverterParameter position (1..5),
/// otherwise StarOff24. Used in the 5-star episode-row rating control.
/// </summary>
public sealed class StarSymbolConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var rating = value is double d ? d : value is int i ? (double)i : 0.0;
        var pos = p is int pi ? pi : (p is string s && int.TryParse(s, out var parsed) ? parsed : 0);
        return rating >= pos ? SymbolRegular.Star24 : SymbolRegular.StarOff24;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Half-star symbol converter. Takes a double Rating as value and an int cell index (1-based) as parameter.
/// Returns Star24 when rating >= cellIndex, StarHalf24 when rating is in (cellIndex-1, cellIndex),
/// and StarOff24 otherwise. Used in the half-star rating popup.
/// </summary>
public sealed class HalfStarSymbolConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var rating = value is double d ? d : value is int i ? (double)i : 0.0;
        var pos = p is int pi ? pi : (p is string s && int.TryParse(s, out var parsed) ? parsed : 0);
        if (rating >= pos) return SymbolRegular.Star24;
        if (rating > pos - 1) return SymbolRegular.StarHalf24;
        return SymbolRegular.StarOff24;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Returns Collapsed when the bound string is null or empty; Visible otherwise.</summary>
public sealed class NotNullOrEmptyToVisibility : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Returns Visible when the bound value is not null; Collapsed otherwise.
/// Use when binding to an object (e.g. a sub-VM) rather than a string.</summary>
public sealed class NotNullToVisibility : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is not null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Returns Visible when the bound value is an int greater than zero; Collapsed otherwise.
/// Treats <see cref="DependencyProperty.UnsetValue"/>, null, and non-int values as Collapsed,
/// so it is safe to use with <c>{Binding Triage.SomeList.Count}</c> when Triage may be null.</summary>
public sealed class CountToVisibility : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Converts a string like "Home24" to the corresponding <see cref="SymbolRegular"/> enum value.
/// Falls back to SymbolRegular.Circle24 when the string is unknown.
/// Used by the command palette to show dynamic icons for each action/result row.
/// </summary>
public sealed class StringToSymbolConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        if (value is string s &&
            Enum.TryParse<SymbolRegular>(s, out var sym))
            return sym;
        return SymbolRegular.Circle24;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Maps a <see cref="BrowseDensity"/> value to a card width (double).
/// Compact = 160, Normal = 200, Spacious = 240.
/// ConverterParameter is ignored.
/// </summary>
public sealed class DensityToCardWidth : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is BrowseDensity d ? d switch
        {
            BrowseDensity.Compact  => 160.0,
            BrowseDensity.Spacious => 240.0,
            _                      => 200.0,
        } : 200.0;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Maps a <see cref="BrowseDensity"/> value to a card thumbnail height (double).
/// Compact = 90, Normal = 112, Spacious = 135.
/// </summary>
public sealed class DensityToCardThumbHeight : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is BrowseDensity d ? d switch
        {
            BrowseDensity.Compact  => 90.0,
            BrowseDensity.Spacious => 135.0,
            _                      => 112.0,
        } : 112.0;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Maps <see cref="BrowseViewMode"/> to a Visibility.
/// Pass ConverterParameter="Grid" to show only in grid mode; "List" for list mode.
/// </summary>
public sealed class ViewModeToVisibility : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var mode = value?.ToString();
        var param = p?.ToString();
        return mode == param ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Maps a 0..1 fraction to a pixel width = fraction * ConverterParameter (a track width in DIPs).
/// Used to render a continue-watching progress fill inside a fixed-width card.</summary>
public sealed class FractionToWidth : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var f = value is double d ? d : 0;
        var w = 0.0;
        if (p is string s) double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out w);
        return Math.Max(0, Math.Min(1, f)) * w;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
