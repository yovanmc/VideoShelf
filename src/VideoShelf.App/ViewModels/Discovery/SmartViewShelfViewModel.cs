using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed class SmartViewShelfViewModel(string name, IEnumerable<RecencyCardViewModel> items)
{
    public string Name { get; } = name;
    public ObservableCollection<RecencyCardViewModel> Items { get; } = new(items);
}
