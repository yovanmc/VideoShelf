using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VideoShelf.Core.Models;

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
