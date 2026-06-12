using System;
using System.Windows;
using LibVLCSharp.Shared;

namespace VideoShelf.App.Views;

/// <summary>Detachable always-on-top mini-player. Re-hosts the shared MediaPlayer while PiP is on.</summary>
public partial class MiniPlayerWindow : Window
{
    /// <summary>Raised when the user clicks "Back to window" (asks the shell to leave PiP).</summary>
    public event EventHandler? ReturnRequested;

    private readonly MediaPlayer _player;

    public MiniPlayerWindow(MediaPlayer player)
    {
        InitializeComponent();
        _player = player;
        // Assign the shared MediaPlayer once the window (and the VideoView's native
        // surface HWND) actually exists. Assigning in the constructor — before Show()/
        // Loaded — points libVLC at a not-yet-realized surface, so the mini-player renders
        // black. PlayerView attaches in its Loaded handler for the same reason.
        Loaded += (_, _) => MiniSurface.MediaPlayer = _player;
    }

    /// <summary>Detaches the MediaPlayer before closing so the inline VideoView can re-host it.</summary>
    public void DetachPlayer()
    {
        MiniSurface.MediaPlayer = null;
    }

    private void OnReturnClick(object sender, RoutedEventArgs e)
        => ReturnRequested?.Invoke(this, EventArgs.Empty);
}
