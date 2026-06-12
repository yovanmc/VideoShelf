using System.Threading;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>
/// Builds a <see cref="CreatorCardViewModel"/> from a <see cref="SectionSummary"/>, applying
/// the user's art override (if any) and starting the background thumbnail load.
/// Used by Home and Search rails so both produce identical creator cards.
/// </summary>
public sealed class CreatorCardFactory(CreatorArtRepository art, IThumbnailService thumbnails)
{
    public CreatorCardViewModel Create(SectionSummary summary)
    {
        var overridePath = art.GetArtPath(summary.SectionId);
        var card = new CreatorCardViewModel(summary, overridePath, thumbnails);
        _ = card.LoadImageAsync(CancellationToken.None);
        return card;
    }
}
