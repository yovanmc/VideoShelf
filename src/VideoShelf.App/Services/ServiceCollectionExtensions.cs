using Microsoft.Extensions.DependencyInjection;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Views;
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

        services.AddSingleton<SourcesViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
