using System;
using System.Threading.Tasks;
using System.Windows;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Views;

/// <summary>
/// Modal window for the hybrid creator portrait picker (candidate grid + scrub panel).
/// DataContext is set to a <see cref="CreatorFramePickerViewModel"/> by the caller.
/// </summary>
public partial class CreatorFramePickerWindow : Window
{
    private readonly CreatorFramePickerViewModel _vm;

    public CreatorFramePickerWindow(CreatorFramePickerViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;

        vm.Confirmed  += OnVmConfirmed;
        vm.Cancelled  += OnVmCancelled;

        Loaded += OnWindowLoaded;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Fire-and-forget: loads thumbnails asynchronously while the window is open.
        // Fail-safe: exceptions are swallowed inside LoadCandidatesAsync.
        try { await _vm.LoadCandidatesAsync(); }
        catch { /* fail-safe */ }
    }

    private void OnVmConfirmed(object? sender, EventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnVmCancelled(object? sender, EventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.Confirmed -= OnVmConfirmed;
        _vm.Cancelled -= OnVmCancelled;
        base.OnClosed(e);
    }
}
