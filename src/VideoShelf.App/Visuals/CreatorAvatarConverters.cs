using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace VideoShelf.App.Visuals;

/// <summary>Converts a creator name string to its 2-letter initials fallback text.</summary>
public sealed class StringToInitialsConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => CreatorAvatar.Initials(value as string);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>Converts a creator name string to a <see cref="SolidColorBrush"/> derived from
/// its deterministic hue. Fixed S=0.45, L=0.45 — tuned for the dark theme.</summary>
public sealed class StringToAvatarBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var hue = CreatorAvatar.HueDegrees(value as string);
        var color = HslToRgb(hue, 0.45, 0.45);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();

    private static Color HslToRgb(int hueDegrees, double s, double l)
    {
        var h = hueDegrees / 360.0;
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p2 = 2 * l - q;
        var r = HueToRgbChannel(p2, q, h + 1.0 / 3);
        var g = HueToRgbChannel(p2, q, h);
        var b = HueToRgbChannel(p2, q, h - 1.0 / 3);
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static double HueToRgbChannel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}

/// <summary>Returns <see cref="Visibility.Visible"/> when the bound value IS null;
/// <see cref="Visibility.Collapsed"/> otherwise. Used to show the avatar placeholder
/// only when no <c>Cover</c> image is available.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}
