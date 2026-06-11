using Microsoft.Extensions.DependencyInjection;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Views;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVideoShelf(this IServiceCollection services)
    {
        services.AddSingleton<AppPaths>();
        services.AddSingleton<LibraryBootstrap>();
        services.AddSingleton<VideoShelfDb>(sp =>
            sp.GetRequiredService<LibraryBootstrap>().OpenLibrary());
        services.AddSingleton<LibraryRepository>();
        services.AddSingleton<WatchRepository>();

        services.AddSingleton<IFolderPicker, FolderPicker>();

        services.AddSingleton<ScanService>();
        services.AddSingleton<IScanCoordinator, ScanCoordinator>();

        services.AddSingleton<IThumbnailSnapshotter, LibVlcThumbnailService>();
        services.AddSingleton<IThumbnailService>(sp =>
            new ThumbnailCache(
                sp.GetRequiredService<AppPaths>().ThumbnailDirectory,
                sp.GetRequiredService<IThumbnailSnapshotter>()));

        services.AddSingleton<SourcesViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
