// src/VideoShelf.App/Motion/ScrollMemory.cs
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Motion;

public sealed class ScrollOffsetStore
{
    private readonly Dictionary<AppView, double> _offsets = new();
    public void Save(AppView view, double y) => _offsets[view] = y;
    public bool TryGet(AppView view, out double y) => _offsets.TryGetValue(view, out y);
}

/// <summary>
/// Attached behavior: saves the hosting ScrollViewer's vertical offset when the
/// owning view hides, and restores it when the view becomes visible again.
/// Keyed by the ScrollMemory.ViewKey attached property (an AppView value).
/// </summary>
public static class ScrollMemory
{
    // ── ViewKey ──────────────────────────────────────────────────────────────
    public static readonly DependencyProperty ViewKeyProperty =
        DependencyProperty.RegisterAttached("ViewKey", typeof(AppView?), typeof(ScrollMemory),
            new PropertyMetadata(null, OnViewKeyChanged));

    public static void SetViewKey(DependencyObject d, AppView? v) => d.SetValue(ViewKeyProperty, v);
    public static AppView? GetViewKey(DependencyObject d) => (AppView?)d.GetValue(ViewKeyProperty);

    // ── Shared store (singleton) ──────────────────────────────────────────────
    public static readonly ScrollOffsetStore Store = new();

    private static void OnViewKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;

        fe.IsVisibleChanged += (_, args) =>
        {
            var key = GetViewKey(fe);
            if (key is null) return;

            // Find the ScrollViewer inside this element (or the element itself).
            var sv = FindScrollViewer(fe);
            if (sv is null) return;

            if (args.NewValue is true)
            {
                // Restore on show.
                if (Store.TryGet(key.Value, out var saved))
                    sv.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Loaded,
                        new System.Action(() => sv.ScrollToVerticalOffset(saved)));
            }
            else
            {
                // Save on hide.
                Store.Save(key.Value, sv.VerticalOffset);
            }
        };
    }

    private static ScrollViewer? FindScrollViewer(FrameworkElement root)
    {
        if (root is ScrollViewer sv) return sv;
        // One level of visual-tree descent is enough for a top-level ScrollViewer child.
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (System.Windows.Media.VisualTreeHelper.GetChild(root, i) is ScrollViewer child)
                return child;
        }
        return null;
    }
}
