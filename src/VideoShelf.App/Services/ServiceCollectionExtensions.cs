using Microsoft.Extensions.DependencyInjection;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Views;

namespace VideoShelf.App.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVideoShelf(this IServiceCollection services)
    {
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
