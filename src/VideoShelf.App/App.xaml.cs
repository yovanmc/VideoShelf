using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VideoShelf.App.Harness;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Views;
using VideoShelf.Core.Storage;

namespace VideoShelf.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = HarnessOptions.Parse(e.Args);

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services => services.AddVideoShelf(options.DataDir))
                .Build();

            _host.StartAsync().GetAwaiter().GetResult();
            var window = _host.Services.GetRequiredService<MainWindow>();
            window.Show();

            if (options.IsHarness)
            {
                var main = _host.Services.GetRequiredService<MainViewModel>();
                var runner = new HarnessRunner(
                    main,
                    options,
                    _host.Services.GetRequiredService<LibraryRepository>(),
                    _host.Services.GetRequiredService<WatchRepository>(),
                    _host.Services.GetRequiredService<TagRepository>());
                _ = Dispatcher.InvokeAsync(async () => await runner.RunAsync(), DispatcherPriority.Background);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "VideoShelf startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            try
            {
                _host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch
            {
                // Preserve the original startup failure shown to the user.
            }
            _host?.Dispose();
            _host = null;
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        finally
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}
