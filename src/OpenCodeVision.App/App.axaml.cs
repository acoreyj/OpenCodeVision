using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OpenCodeVision.Core;
using OpenCodeVision.Core.Discovery;

namespace OpenCodeVision.App;

public partial class App : Application
{
    private SessionMonitor? _monitor;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenCodeVision");
            Directory.CreateDirectory(root);

            var migratedFrom = TryMigrateSettings(root);

            var settingsStore = new SettingsStore(Path.Combine(root, "settings.json"));
            var settings = settingsStore.Load();
            var log = new DiagnosticsLog(Path.Combine(root, "sticky.log"));
            if (migratedFrom is not null)
            {
                log.Write("migrated settings from " + migratedFrom);
            }

            var nativeStatePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "state",
                "opencode",
                "service.json");

            IWslGateway? wsl = OperatingSystem.IsWindows() ? new WslGateway() : null;
            var discovery = new ServiceDiscovery(wsl, nativeStatePath);

            _monitor = new SessionMonitor(
                discovery,
                descriptor => new OpenCodeClient(descriptor.Url, descriptor.Password),
                settings,
                TimeProvider.System,
                log.Write);

            desktop.MainWindow = new MainWindow(_monitor, settings, settingsStore, log);

            var window = (MainWindow)desktop.MainWindow;
            window.HasTray = !OperatingSystem.IsLinux();
            AppTrayIcon? tray = null;
            try
            {
                tray = new AppTrayIcon(window, _monitor, window.ShowOptions, async () =>
                {
                    window.IsQuitting = true;
                    tray?.Dispose();
                    try
                    {
                        await _monitor.DisposeAsync();
                    }
                    catch
                    {
                        // Shutdown must continue even if disposal reports an error.
                    }

                    desktop.Shutdown();
                });
            }
            catch (Exception exception)
            {
                window.HasTray = false;
                log.Write("tray unavailable: " + exception.Message);
            }

            _ = StartMonitorAsync(log);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string? TryMigrateSettings(string root)
    {
        var target = Path.Combine(root, "settings.json");
        if (File.Exists(target))
        {
            return null;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var name in new[] { "OpencodeVision", "OpenCodeSticky" })
        {
            var source = Path.Combine(appData, name, "settings.json");
            if (!File.Exists(source))
            {
                continue;
            }

            try
            {
                File.Copy(source, target);
                return name;
            }
            catch
            {
                // Try the next legacy folder.
            }
        }

        return null;
    }

    private async Task StartMonitorAsync(DiagnosticsLog log)
    {
        try
        {
            log.Write("starting monitor");
            await _monitor!.StartAsync(CancellationToken.None);
            foreach (var service in _monitor.Services)
            {
                log.Write(
                    $"service {service.Key} {service.Url} v{service.Version ?? "unknown"} " +
                    $"enabled={service.Enabled} online={service.Online}");
            }

            log.Write($"rows={_monitor.Rows.Count}");
        }
        catch (Exception exception)
        {
            log.Write("startup failed: " + exception);
        }
    }
}
