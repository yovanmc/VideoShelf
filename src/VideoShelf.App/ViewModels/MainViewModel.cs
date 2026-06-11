using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;

namespace VideoShelf.App.ViewModels;

public sealed partial class MainViewModel(
    SourcesViewModel sources,
    LibraryViewModel library,
    IScanCoordinator scanCoordinator) : ObservableObject
{
    public string Title => "VideoShelf";

    public SourcesViewModel Sources => sources;
    public LibraryViewModel Library => library;

    [ObservableProperty]
    private bool _isScanning;

    /// <summary>Loads sources + library once at startup.</summary>
    public async Task InitializeAsync()
    {
        Sources.Load();
        await Library.LoadSectionsAsync();
    }

    [RelayCommand]
    private async Task ScanAndReload()
    {
        IsScanning = true;
        try
        {
            await scanCoordinator.ScanAllAsync(CancellationToken.None);
            Sources.Load();
            await Library.LoadSectionsAsync();
        }
        finally
        {
            IsScanning = false;
        }
    }
}
