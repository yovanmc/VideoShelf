using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VideoShelf.App.ViewModels;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.App.Views;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;

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

        services.AddSingleton<IFolderPicker, FolderPicker>();

        services.AddSingleton<ScanService>();
        services.AddSingleton<IScanCoordinator, ScanCoordinator>();

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
                sp.GetRequiredService<ResumePolicy>())
            {
                CaptureDirectory = paths.CaptureDirectory,
                SeekPreviewDirectory = paths.SeekPreviewDirectory,
            };
            return vm;
        });

        services.AddSingleton<TagRepository>();
        services.AddSingleton<DiscoveryRepository>();
        services.AddSingleton<DiscoveryViewModel>();
        services.AddSingleton<SectionDetailViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SourcesViewModel>();
        services.AddSingleton<LibraryViewModel>();

        services.AddSingleton<IFileSystem, RealFileSystem>();
        services.AddSingleton<RenamePlanner>();
        services.AddSingleton<RenameExecutor>();
        services.AddSingleton<RenameToolViewModel>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
