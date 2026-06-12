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
