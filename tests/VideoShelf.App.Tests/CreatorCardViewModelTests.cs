using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using Xunit;

namespace VideoShelf.App.Tests;

public class CreatorCardViewModelTests
{
    private sealed class StubThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken cancellationToken)
            => Task.FromResult<string?>(videoPath + ".thumb.png");
    }

    private static SectionSummary Summary(long id = 1, int videos = 3, string? seed = @"C:\v\a.mp4")
        => new(SectionId: id, SourceId: 1, DisplayName: "Creator A",
               SeriesCount: 1, UnwatchedCount: 1, VideoCount: videos, ThumbnailSeedPath: seed);

    [Fact]
    public void Exposes_name_and_video_count_label()
    {
        var vm = new CreatorCardViewModel(Summary(videos: 5), overrideArtPath: null, new StubThumbs());

        vm.Name.ShouldBe("Creator A");
        vm.VideoCountLabel.ShouldBe("5 videos");
    }

    [Fact]
    public void Single_video_label_is_singular()
    {
        var vm = new CreatorCardViewModel(Summary(videos: 1), overrideArtPath: null, new StubThumbs());

        vm.VideoCountLabel.ShouldBe("1 video");
    }

    [Fact]
    public async Task Override_art_wins_over_seed_frame()
    {
        var vm = new CreatorCardViewModel(Summary(), overrideArtPath: @"C:\pics\custom.png", new StubThumbs());

        await vm.LoadImageAsync(CancellationToken.None);

        vm.ImagePath.ShouldBe(@"C:\pics\custom.png");
    }

    [Fact]
    public async Task Falls_back_to_representative_frame_when_no_override()
    {
        var vm = new CreatorCardViewModel(Summary(seed: @"C:\v\a.mp4"), overrideArtPath: null, new StubThumbs());

        await vm.LoadImageAsync(CancellationToken.None);

        vm.ImagePath.ShouldBe(@"C:\v\a.mp4.thumb.png");
    }

    [Fact]
    public async Task No_image_when_no_override_and_no_seed()
    {
        var vm = new CreatorCardViewModel(Summary(seed: null), overrideArtPath: null, new StubThumbs());

        await vm.LoadImageAsync(CancellationToken.None);

        vm.ImagePath.ShouldBeNull();
    }

    // -----------------------------------------------------------------
    // A1 — ISelectableCard / IsSelected
    // -----------------------------------------------------------------

    [Fact]
    public void IsSelected_defaults_to_false()
    {
        var vm = new CreatorCardViewModel(Summary(), overrideArtPath: null, new StubThumbs());

        vm.IsSelected.ShouldBeFalse();
    }

    [Fact]
    public void IsSelected_round_trips_true_then_false()
    {
        var vm = new CreatorCardViewModel(Summary(), overrideArtPath: null, new StubThumbs());

        vm.IsSelected = true;
        vm.IsSelected.ShouldBeTrue();

        vm.IsSelected = false;
        vm.IsSelected.ShouldBeFalse();
    }

    [Fact]
    public void IsSelected_raises_PropertyChanged()
    {
        var vm = new CreatorCardViewModel(Summary(), overrideArtPath: null, new StubThumbs());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsSelected = true;

        raised.ShouldContain(nameof(CreatorCardViewModel.IsSelected));
    }

    [Fact]
    public void CreatorCardViewModel_implements_ISelectableCard()
    {
        var vm = new CreatorCardViewModel(Summary(), overrideArtPath: null, new StubThumbs());

        // Verify the interface contract via the typed reference.
        ISelectableCard card = vm;
        card.IsSelected = true;
        card.IsSelected.ShouldBeTrue();

        card.IsSelected = false;
        card.IsSelected.ShouldBeFalse();
    }

    [Fact]
    public void SelectionViewModel_wires_via_PropertyChanged_subscription()
    {
        // Verify the CreatorsViewModel-style wiring: subscribe, mutate, observe SelectedItems.
        var selection = new SelectionViewModel<CreatorCardViewModel>();
        var vm = new CreatorCardViewModel(Summary(), overrideArtPath: null, new StubThumbs());

        // Mirror what CreatorsViewModel.OnCardPropertyChanged does.
        vm.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(CreatorCardViewModel.IsSelected) &&
                sender is CreatorCardViewModel card)
                selection.OnItemSelectionChanged(card);
        };

        vm.IsSelected = true;
        selection.SelectedItems.ShouldContain(vm);
        selection.SelectedCount.ShouldBe(1);

        vm.IsSelected = false;
        selection.SelectedItems.ShouldBeEmpty();
        selection.SelectedCount.ShouldBe(0);
    }
}
