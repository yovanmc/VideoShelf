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
using Wpf.Ui.Appearance;

namespace VideoShelf.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // M24-A: apply Ice Cyan accent to WPF-UI native controls (primary buttons,
        // slider thumb, checkbox ticks, etc.) BEFORE any window/control is created, so
        // they follow our token (not the OS accent) from first render.
        ApplicationAccentColorManager.Apply(
            System.Windows.Media.Color.FromRgb(0x4F, 0xC3, 0xF7),
            ApplicationTheme.Dark);

        var options = HarnessOptions.Parse(e.Args);

        // M25-A4: global exception net. Catch unhandled exceptions on the UI thread
        // (recoverable — keep the app alive) and from the AppDomain (terminal — log + show).
        DispatcherUnhandledException += (s, args) =>
        {
            var report = Diagnostics.CrashReporter.FormatReport("UI thread", args.Exception);
            Diagnostics.CrashReporter.WriteToDisk(ResolveDataDir(options), report);
            System.Windows.MessageBox.Show(report, "VideoShelf — unexpected error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            args.Handled = true; // keep the app alive after a non-fatal UI-thread exception
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var report = Diagnostics.CrashReporter.FormatReport("AppDomain", args.ExceptionObject as Exception);
            Diagnostics.CrashReporter.WriteToDisk(ResolveDataDir(options), report);
            // AppDomain exceptions are terminal; we can only log + show, not recover.
        };

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
                    _host.Services.GetRequiredService<TagRepository>(),
                    _host.Services.GetRequiredService<CurationRepository>(),
                    _host.Services.GetRequiredService<PlaylistRepository>(),
                    _host.Services.GetRequiredService<MaintenanceRepository>());
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

    /// <summary>
    /// Resolves the data directory used for crash logs: the harness override if set,
    /// else the default LocalApplicationData\VideoShelf path (mirrors AppPaths).
    /// </summary>
    private static string ResolveDataDir(HarnessOptions options)
    {
        if (!string.IsNullOrEmpty(options.DataDir))
        {
            return options.DataDir;
        }

        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoShelf");
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
