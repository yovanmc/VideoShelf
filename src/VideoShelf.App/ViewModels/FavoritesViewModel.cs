using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class FavoritesViewModel(CurationRepository curation, LibraryRepository library) : ObservableObject
{
    public ObservableCollection<RecencyCardViewModel> Favorites { get; } = [];

    public bool HasFavorites => Favorites.Count > 0;

    /// <summary>Raised when a card's Play is invoked; carries the resolved EpisodeView.</summary>
    public event EventHandler<EpisodeView>? PlayRequested;

    public async Task LoadAsync()
    {
        var items = await Task.Run(() => curation.GetFavorites(48));

        Favorites.Clear();
        foreach (var item in items)
        {
            var card = new RecencyCardViewModel(item);
            var capturedId = item.VideoId;
            card.PlayInvoked += (_, _) =>
            {
                var ep = library.GetEpisode(capturedId);
                if (ep is not null) PlayRequested?.Invoke(this, ep);
            };
            Favorites.Add(card);
        }
        OnPropertyChanged(nameof(HasFavorites));
    }

    /// <summary>Synchronous wrapper for use from MainViewModel RelayCommand.</summary>
    public void Load() => _ = LoadAsync();
}
