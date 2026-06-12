using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed partial class TagChipViewModel(string tag, int sectionCount) : ObservableObject
{
    public string Tag => tag;
    public int SectionCount => sectionCount;
    public string Label => $"{tag} ({sectionCount})";
    [ObservableProperty] private bool isSelected;
}
