namespace VideoShelf.App.Views;

public partial class SectionDetailView : System.Windows.Controls.UserControl
{
    public SectionDetailView()
    {
        InitializeComponent();
    }

    private void AddToPlaylist_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe && fe.ContextMenu is { } menu)
        {
            menu.PlacementTarget = fe;       // ensure PlacementTarget.Tag bindings resolve
            menu.IsOpen = true;
        }
    }
}
