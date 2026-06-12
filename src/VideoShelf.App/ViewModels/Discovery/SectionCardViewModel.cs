using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Discovery;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed partial class SectionCardViewModel(SectionSuggestion item) : ObservableObject
{
    public long SectionId => item.SectionId;
    public string DisplayName => item.DisplayName;
    public int SeriesCount => item.SeriesCount;
    public int UnwatchedCount => item.UnwatchedCount;
    public bool HasUnwatched => item.UnwatchedCount > 0;
    public string TagsLabel => string.Join(" · ", item.Tags);

    public event EventHandler? OpenInvoked;
    [RelayCommand] private void Open() => OpenInvoked?.Invoke(this, EventArgs.Empty);
}
