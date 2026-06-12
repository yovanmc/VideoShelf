using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.App.Services;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public partial class CreatorsViewModel : ObservableObject
{
    private readonly LibraryRepository _library;
    private readonly CreatorArtRepository _art;
    private readonly IThumbnailService _thumbnails;

    public CreatorsViewModel(LibraryRepository library, CreatorArtRepository art, IThumbnailService thumbnails)
    {
        _library = library;
        _art = art;
        _thumbnails = thumbnails;
    }

    public ObservableCollection<CreatorCardViewModel> Creators { get; } = new();

    /// <summary>Raised when a creator card is activated (forwarded to the host nav).</summary>
    public event Action<long>? OpenCreatorRequested;

    public async Task LoadAsync(CancellationToken ct)
    {
        // Heavy work off the UI thread; resume on the captured context to mutate the UI-bound collection.
        // NOTE: do NOT use ConfigureAwait(false) on this chain (the Cross-thread ObservableCollection gotcha).
        var summaries = await Task.Run(() => _library.GetSectionSummaries(), ct);

        Creators.Clear();
        foreach (var summary in summaries)
        {
            var overridePath = _art.GetArtPath(summary.SectionId);
            var card = new CreatorCardViewModel(summary, overridePath, _thumbnails);
            card.OpenRequested += id => OpenCreatorRequested?.Invoke(id);
            Creators.Add(card);
            await card.LoadImageAsync(ct);
        }
    }
}
