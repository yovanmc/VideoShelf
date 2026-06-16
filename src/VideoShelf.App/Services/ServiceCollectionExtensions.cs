using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VideoShelf.App.Motion;
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
        services.AddSingleton<IImageLoader>(_ => new PooledBitmapLoader(maxEntries: 600));
        services.AddSingleton<IMotionPolicy, SystemMotionPolicy>();
        services.AddSingleton<IToastService>(_ => new ToastService((delay, act) =>
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = delay };
            timer.Tick += (_, _) => { timer.Stop(); act(); };
            timer.Start();
        }));
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
        services.AddSingleton<IVideoFilePicker, VideoFilePicker>();
        services.AddSingleton<IConfirmService, ConfirmService>();
        services.AddSingleton<IRecycleBinService, RecycleBinService>();
        services.AddSingleton<CreatorArtRepository>();
        services.AddSingleton<ItemArtRepository>();
        services.AddSingleton<CreatorsViewModel>(sp => new CreatorsViewModel(
            sp.GetRequiredService<LibraryRepository>(),
            sp.GetRequiredService<CreatorArtRepository>(),
            sp.GetRequiredService<IThumbnailService>(),
            sp.GetRequiredService<SettingsRepository>(),
            sp.GetRequiredService<IImageLoader>()));

        services.AddSingleton<ScanService>();
        services.AddSingleton<IScanCoordinator, ScanCoordinator>();

        services.AddSingleton<IMediaProbe, LibVlcMediaProbe>();
        services.AddSingleton<MediaBackfillService>();
        services.AddSingleton<ResolutionBackfillService>();
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
                SeekPreviewDirectory = paths.SeekPreviewDirectory,
                CoversDirectory = paths.CoversDirectory,
            };
            return vm;
        });

        services.AddSingleton<TagRepository>();
        services.AddSingleton<CurationRepository>();
        services.AddSingleton<PlaylistRepository>();
        services.AddSingleton<DiscoveryRepository>();
        services.AddSingleton<PlayQueueViewModel>();
        services.AddSingleton<DiscoveryViewModel>();
        services.AddSingleton<GroupingEditViewModel>();
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
            sp.GetRequiredService<ItemArtRepository>(),
            sp.GetRequiredService<MaintenanceRepository>(),
            sp.GetRequiredService<IRecycleBinService>(),
            sp.GetRequiredService<IConfirmService>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<GroupingEditViewModel>(),
            sp.GetRequiredService<IToastService>(),
            sp.GetRequiredService<IImageLoader>()));
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SourcesViewModel>(sp => new SourcesViewModel(
            sp.GetRequiredService<LibraryRepository>(),
            sp.GetRequiredService<IFolderPicker>(),
            sp.GetRequiredService<IConfirmService>(),
            sp.GetRequiredService<IToastService>()));
        services.AddSingleton<LibraryViewModel>(sp => new LibraryViewModel(
            sp.GetRequiredService<LibraryRepository>(),
            sp.GetRequiredService<WatchRepository>(),
            sp.GetRequiredService<IThumbnailService>(),
            sp.GetRequiredService<IImageLoader>()));

        services.AddSingleton<IFileSystem, RealFileSystem>();
        services.AddSingleton<RenamePlanner>();
        services.AddSingleton<RenameExecutor>();
        services.AddSingleton<RenameToolViewModel>(sp => new RenameToolViewModel(
            sp.GetRequiredService<LibraryRepository>(),
            sp.GetRequiredService<RenamePlanner>(),
            sp.GetRequiredService<RenameExecutor>(),
            sp.GetRequiredService<SettingsRepository>(),
            sp.GetRequiredService<AppPaths>(),
            sp.GetRequiredService<IToastService>()));
        services.AddSingleton<CreatorCardFactory>(sp => new CreatorCardFactory(
            sp.GetRequiredService<CreatorArtRepository>(),
            sp.GetRequiredService<IThumbnailService>(),
            sp.GetRequiredService<IImageLoader>()));
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<FavoritesViewModel>();
        services.AddSingleton<WatchLaterViewModel>();
        services.AddSingleton<PlaylistsViewModel>();
        services.AddSingleton<HistoryRepository>();
        services.AddSingleton<HistoryViewModel>(sp => new HistoryViewModel(
            sp.GetRequiredService<HistoryRepository>(),
            sp.GetRequiredService<LibraryRepository>(),
            sp.GetRequiredService<IThumbnailService>(),
            sp.GetRequiredService<IImageLoader>()));
        services.AddSingleton<BulkActionBarViewModel>(sp => new BulkActionBarViewModel(
            sp.GetRequiredService<WatchRepository>(),
            sp.GetRequiredService<TagRepository>(),
            sp.GetRequiredService<CurationRepository>(),
            sp.GetRequiredService<PlaylistRepository>(),
            sp.GetRequiredService<PlayQueueViewModel>(),
            sp.GetRequiredService<LibraryRepository>(),
            sp.GetRequiredService<IToastService>()));
        services.AddSingleton<MaintenanceRepository>();
        services.AddSingleton<MissingTriageViewModel>();
        services.AddSingleton<MaintenanceViewModel>();
        services.AddSingleton<InsightsViewModel>();
        services.AddSingleton<MainViewModel>(sp =>
        {
            var lib = sp.GetRequiredService<LibraryRepository>();
            return new MainViewModel(
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
                sp.GetRequiredService<FavoritesViewModel>(),
                sp.GetRequiredService<WatchLaterViewModel>(),
                sp.GetRequiredService<PlaylistsViewModel>(),
                sp.GetRequiredService<HistoryViewModel>(),
                lib,
                sp.GetRequiredService<BulkActionBarViewModel>(),
                sp.GetRequiredService<ResolutionBackfillService>(),
                sp.GetRequiredService<MaintenanceViewModel>(),
                sp.GetRequiredService<IToastService>(),
                sp.GetRequiredService<IMotionPolicy>(),
                sp.GetRequiredService<InsightsViewModel>());
        });
        services.AddSingleton<MainWindow>();
        return services;
    }
}
