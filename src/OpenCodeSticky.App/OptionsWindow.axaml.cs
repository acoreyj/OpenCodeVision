using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using OpenCodeSticky.Core;

namespace OpenCodeSticky.App;

public partial class OptionsWindow : Window
{
    private readonly SessionMonitor _monitor;
    private readonly Settings _settings;
    private readonly SettingsStore _store;
    private readonly Action _applyAlwaysOnTop;

    public OptionsWindow(
        SessionMonitor monitor,
        Settings settings,
        SettingsStore store,
        Action applyAlwaysOnTop)
    {
        InitializeComponent();

        _monitor = monitor;
        _settings = settings;
        _store = store;
        _applyAlwaysOnTop = applyAlwaysOnTop;

        var always = this.FindControl<CheckBox>("AlwaysOnTop")!;
        always.IsChecked = settings.AlwaysOnTop;
        always.IsCheckedChanged += (_, _) =>
        {
            settings.AlwaysOnTop = always.IsChecked == true;
            _applyAlwaysOnTop();
            Save();
        };

        var flash = this.FindControl<CheckBox>("FlashOnBlock")!;
        flash.IsChecked = settings.FlashOnBlock;
        flash.IsCheckedChanged += (_, _) =>
        {
            settings.FlashOnBlock = flash.IsChecked == true;
            Save();
        };

        ReloadStreams();
    }

    private void ReloadStreams()
    {
        var streams = this.FindControl<StackPanel>("Streams")!;
        streams.Children.Clear();

        foreach (var service in _monitor.Services)
        {
            var status = service.Online ? "online" : "offline";
            var version = service.Version ?? "unknown";
            var check = new CheckBox
            {
                IsChecked = service.Enabled,
                Content = $"{service.DisplayName}   v{version}   {status}",
            };

            var key = service.Key;
            check.IsCheckedChanged += (_, _) =>
            {
                var enabled = check.IsChecked == true;
                _monitor.SetServiceEnabled(key, enabled);
                _settings.SetServiceEnabled(key, enabled);
                Save();
            };

            streams.Children.Add(check);
        }
    }

    private async void OnRescan(object? sender, RoutedEventArgs e)
    {
        var button = this.FindControl<Button>("RescanButton")!;
        button.IsEnabled = false;
        button.Content = "Scanning…";
        try
        {
            await _monitor.RescanAsync(CancellationToken.None);
            ReloadStreams();
        }
        finally
        {
            button.Content = "Rescan";
            button.IsEnabled = true;
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void Save()
    {
        try
        {
            _store.Save(_settings);
        }
        catch
        {
            // Settings failures should not break the dialog.
        }
    }
}
