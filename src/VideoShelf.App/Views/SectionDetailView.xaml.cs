using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace VideoShelf.App.Views;

public partial class SectionDetailView : System.Windows.Controls.UserControl
{
    public SectionDetailView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Closes the rating Popup after a star half-click.
    /// The ToggleButton's IsChecked (bound to Popup.IsOpen) is set to false.
    /// </summary>
    private void RatingPopup_StarClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        // Walk up to the Popup and close it; the command has already been invoked by the Button.
        var btn = sender as System.Windows.FrameworkElement;
        var popup = FindAncestor<Popup>(btn);
        if (popup is not null)
            popup.IsOpen = false;
    }

    /// <summary>
    /// Opens the episode row's ContextMenu from the ⋯ overflow button.
    /// Walks up to the nearest StackPanel that has a ContextMenu attached.
    /// </summary>
    private void EpisodeOverflow_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement btn) return;
        var panel = FindAncestorWithContextMenu(btn);
        if (panel?.ContextMenu is null) return;
        panel.ContextMenu.PlacementTarget = btn;
        panel.ContextMenu.Placement = PlacementMode.Bottom;
        panel.ContextMenu.IsOpen = true;
    }

    /// <summary>
    /// Opens the series tile's ContextMenu from the ⋯ overflow button.
    /// Walks up to the Border that owns the series context menu.
    /// </summary>
    private void SeriesOverflow_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement btn) return;
        var border = FindAncestorWithContextMenu(btn);
        if (border?.ContextMenu is null) return;
        border.ContextMenu.PlacementTarget = btn;
        border.ContextMenu.Placement = PlacementMode.Bottom;
        border.ContextMenu.IsOpen = true;
    }

    /// <summary>Walks the visual tree upward to find the nearest element with a non-null ContextMenu.</summary>
    private static System.Windows.FrameworkElement? FindAncestorWithContextMenu(System.Windows.DependencyObject? obj)
    {
        while (obj is not null)
        {
            if (obj is System.Windows.FrameworkElement fe && fe.ContextMenu is not null)
                return fe;
            obj = System.Windows.Media.VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private static T? FindAncestor<T>(System.Windows.DependencyObject? obj) where T : System.Windows.DependencyObject
    {
        while (obj is not null)
        {
            if (obj is T t) return t;
            obj = System.Windows.Media.VisualTreeHelper.GetParent(obj);
        }
        return null;
    }
}
