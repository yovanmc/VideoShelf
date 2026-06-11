using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoShelf.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "VideoShelf";
}
