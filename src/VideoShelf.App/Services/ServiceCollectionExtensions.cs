using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VideoShelf.App.ViewModels;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.App.Views;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
#pragma warning disable CA1506

namespace VideoShelf.App.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all VideoShelf services.
    /// <paramref name="dataDirOverride"/> redirects the DB and data files to a custom directory
    /// (used by the visual-verification harness for isolation). Real users omit it.
    /// </summary>
    public static IServiceCollection AddVideoShelf(this IServiceCollection services, string? dataDirOverride = null)
    {
        if (dataDirOverride is not null)
        {
            Directory.CreateDirectory(dataDirOverride);
            services.AddSingleton(new AppPaths(dataDirOverride));
        }
        else
        {
            services.AddSingleton<AppPaths>();
        }
        services.AddSingleton<LibraryBootstrap>();
        services.AddSingleton<VideoShelfDb>(sp =>
            sp.GetRequiredService<LibraryBootstrap>().OpenLibrary());
        services.AddSingleton<LibraryRepository>();
        services.AddSingleton<WatchRepository>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<StatsRepository>();

        services.AddSingleton<IFolderPicker, FolderPicker>();
        services.AddSingleton<IImagePicker, ImagePicker>();
        services.AddSingleton<ISubtitleFilePicker, SubtitleFilePicker>();
        services.AddSingleton<CreatorArtRepository>();
        services.AddSingleton<ItemArtRepository>();
        services.AddSingleton<CreatorsViewModel>(sp => new CreatorsViewModel(
            sp.GetRequiredService<LibraryRepository>(),
            sp.GetRequiredService<CreatorArtRepository>(),
            sp.GetRequiredService<IThumbnailService>(),
            sp.GetRequiredService<SettingsRepository>()));

        services.AddSingleton<ScanService>();
        services.AddSingleton<IScanCoordinator, ScanCoordinator>();

        services.AddSingleton<IMediaProbe, LibVlcMediaProbe>();
        services.AddSingleton<MediaBackfillService>();
        services.AddSingleton<IThumbnailSnapshotter, LibVlcThumbnailService>();
        services.AddSingleton<IThumbnailService>(sp =>
            new ThumbnailCache(
                sp.GetRequiredService<AppPaths>().ThumbnailDirectory,
                sp.GetRequiredService<IThumbnailSnapshotter>()));

        services.AddSingleton<ResumePolicy>();
        services.AddSingleton<IPlaybackEngine, LibVlcPlaybackEngine>();
        services.AddSingleton<PlayerViewModel>(sp =>
        {
            var paths = sp.GetRequiredService<AppPaths>();
            var vm = new PlayerViewModel(
                sp.GetRequiredService<IPlaybackEngine>(),
                sp.GetRequiredService<LibraryRepository>(),
                sp.GetRequiredService<WatchRepository>(),
                sp.GetRequiredService<SettingsRepository>(),
                sp.GetRequiredService<ResumePolicy>(),
                sp.GetRequiredService<ISubtitleFilePicker>(),
                sp.GetRequiredService<ItemArtRepository>())
            {
                CaptureDirectory = paths.CaptureDirectory,
                SeekPreviewDirectory = paths.SeekPreviewDirectory,
                CoversDirectory = paths.CoversDirectory,
            };
            return vm;
        });

        services.AddSingleton<TagRepository>();
        services.AddSingleton<SmartViewRepository>();
        services.AddSingleton<CurationRepository>();
        services.AddSingleton<PlaylistRepository>();
        services.AddSingleton<DiscoveryRepository>();
        services.AddSingleton<PlayQueueViewModel>();
        services.AddSingleton<DiscoveryViewModel>();
        services.AddSingleton<SectionDetailViewModel>(sp => new SectionDetailViewModel(
            sp.GetRequiredService<LibraryRepository>(),
            sp.GetRequiredService<TagRepository>(),
            sp.GetRequiredService<WatchRepository>(),
            sp.GetRequiredService<IThumbnailService>(),
            sp.GetRequiredService<CreatorArtRepository>(),
            sp.GetRequiredService<IImagePicker>(),
            sp.GetRequiredService<PlayQueueViewModel>(),
            sp.GetRequiredService<CurationRepository>(),
            sp.GetRequiredService<PlaylistRepository>(),
            sp.GetRequiredService<ItemArtRepository>()));
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SourcesViewModel>();
        services.AddSingleton<LibraryViewModel>();

        services.AddSingleton<IFileSystem, RealFileSystem>();
        services.AddSingleton<RenamePlanner>();
        services.AddSingleton<RenameExecutor>();
        services.AddSingleton<RenameToolViewModel>();

        services.AddSingleton<CreatorCardFactory>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<SmartViewsViewModel>();
        services.AddSingleton<FavoritesViewModel>();
        services.AddSingleton<WatchlistViewModel>();
        services.AddSingleton<PlaylistsViewModel>();
        services.AddSingleton<HistoryRepository>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<BulkActionBarViewModel>();
        services.AddSingleton<MainViewModel>(sp =>
        {
            var lib = sp.GetRequiredService<LibraryRepository>();

            // Deferred holder: lets the palette reference mainVm before it exists.
            // The delegates capture this box; by the time they execute mainVm is set.
            MainViewModel? mainVmBox = null;

            var palette = new CommandPaletteViewModel(
                lib,
                // Action registry built lazily on first call — mainVmBox is set before palette executes anything.
                actionRegistryFactory: () => mainVmBox!.BuildActionRegistry(),
                openSection: id => mainVmBox!.OpenSectionAsync(id),
                playVideo: videoId =>
                {
                    var ep = lib.GetEpisode(videoId);
                    if (ep is not null) mainVmBox!.PlayEpisode(ep);
                });

            var mainVm = new MainViewModel(
                sp.GetRequiredService<SourcesViewModel>(),
                sp.GetRequiredService<LibraryViewModel>(),
                sp.GetRequiredService<IScanCoordinator>(),
                sp.GetRequiredService<PlayerViewModel>(),
                sp.GetRequiredService<SettingsViewModel>(),
                sp.GetRequiredService<DiscoveryViewModel>(),
                sp.GetRequiredService<SectionDetailViewModel>(),
                sp.GetRequiredService<RenameToolViewModel>(),
                sp.GetRequiredService<CreatorsViewModel>(),
                sp.GetRequiredService<SearchViewModel>(),
                sp.GetRequiredService<MediaBackfillService>(),
                sp.GetRequiredService<PlayQueueViewModel>(),
                sp.GetRequiredService<SmartViewsViewModel>(),
                sp.GetRequiredService<FavoritesViewModel>(),
                sp.GetRequiredService<WatchlistViewModel>(),
                sp.GetRequiredService<PlaylistsViewModel>(),
                sp.GetRequiredService<HistoryViewModel>(),
                lib,
                sp.GetRequiredService<BulkActionBarViewModel>(),
                palette);

            mainVmBox = mainVm;
            return mainVm;
        });
        services.AddSingleton<MainWindow>();
        return services;
    }
}
