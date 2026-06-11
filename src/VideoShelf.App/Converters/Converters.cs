using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VideoShelf.Core.Models;

namespace VideoShelf.App.Converters;

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
