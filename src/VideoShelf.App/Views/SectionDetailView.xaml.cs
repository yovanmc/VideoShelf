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
