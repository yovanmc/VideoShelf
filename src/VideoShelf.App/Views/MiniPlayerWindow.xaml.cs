using System;
using System.Windows;
using LibVLCSharp.Shared;

namespace VideoShelf.App.Views;

/// <summary>Detachable always-on-top mini-player. Re-hosts the shared MediaPlayer while PiP is on.</summary>
public partial class MiniPlayerWindow : Window
{
    /// <summary>Raised when the user clicks "Back to window" (asks the shell to leave PiP).</summary>
    public event EventHandler? ReturnRequested;

    public MiniPlayerWindow(MediaPlayer player)
    {
        InitializeComponent();
        MiniSurface.MediaPlayer = player;
    }

    /// <summary>Detaches the MediaPlayer before closing so the inline VideoView can re-host it.</summary>
    public void DetachPlayer()
    {
        MiniSurface.MediaPlayer = null;
    }

    private void OnReturnClick(object sender, RoutedEventArgs e)
        => ReturnRequested?.Invoke(this, EventArgs.Empty);
}
