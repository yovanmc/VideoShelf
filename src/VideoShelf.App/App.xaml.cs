using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VideoShelf.App.Services;
using VideoShelf.App.Views;

namespace VideoShelf.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services => services.AddVideoShelf())
                .Build();

            _host.StartAsync().GetAwaiter().GetResult();
            var window = _host.Services.GetRequiredService<MainWindow>();
            window.Show();
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
