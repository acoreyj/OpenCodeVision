using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using OpenCodeSticky.App.ViewModels;
using OpenCodeSticky.Core;

namespace OpenCodeSticky.App;

public partial class MainWindow : Window
{
    private readonly SessionMonitor _monitor;
    private readonly Settings _settings;
    private readonly SettingsStore _store;
    private readonly DiagnosticsLog _log;
    private readonly ItemsControl _itemsControl;
    private readonly ObservableCollection<object> _items = new();
    private readonly Dictionary<string, DispatcherTimer> _flashTimers = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _elapsedTimer;
    private string _signature = "";

    public MainWindow(SessionMonitor monitor, Settings settings, SettingsStore store, DiagnosticsLog log)
    {
        InitializeComponent();

        _monitor = monitor;
        _settings = settings;
        _store = store;
        _log = log;
        _itemsControl = this.FindControl<ItemsControl>("Items")!;
        _itemsControl.ItemsSource = _items;

        Opened += OnOpened;
        Closing += OnClosing;
        KeyDown += OnKeyDown;

        _monitor.Changed += (_, _) => Dispatcher.UIThread.Post(Rebuild);
        _monitor.BlockedStarted += (_, args) => Dispatcher.UIThread.Post(() => StartFlash(args));

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => UpdateElapsed();
        _elapsedTimer.Start();

        Rebuild();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var screen = Screens.Primary;
        if (screen is not null)
        {
            MaxHeight = screen.WorkingArea.Height / RenderScaling * 0.6;
        }

        Topmost = _settings.AlwaysOnTop;

        if (_settings.WindowLeft is { } left && _settings.WindowTop is { } top)
        {
            var point = new PixelPoint((int)left, (int)top);
            if (Screens.All.Any(candidate => Contains(candidate.WorkingArea, point)))
            {
                Position = point;
            }
        }
        else if (screen is not null)
        {
            var area = screen.WorkingArea;
            var width = (int)(Width * RenderScaling);
            Position = new PixelPoint(
                area.Right - width - (int)(24 * RenderScaling),
                area.Y + (int)(24 * RenderScaling));
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _settings.WindowLeft = Position.X;
        _settings.WindowTop = Position.Y;
        try
        {
            _store.Save(_settings);
        }
        catch (Exception exception)
        {
            _log.Write("save settings failed: " + exception.Message);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            WindowState = WindowState.Minimized;
            e.Handled = true;
        }
    }

    private void OnMinimize(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void Rebuild()
    {
        var rows = _monitor.Rows;
        var signature = string.Join(
            "\n",
            rows.Select(row =>
                $"{row.ServiceKey}|{row.Session.Id}|{row.State}|{row.IsOffline}|{row.Session.Title}"));

        if (signature == _signature)
        {
            UpdateElapsed();
            return;
        }

        _signature = signature;
        _items.Clear();

        string? lastService = null;
        foreach (var row in rows)
        {
            if (row.ServiceKey != lastService)
            {
                _items.Add(new ServiceHeaderViewModel(row.ServiceName));
                lastService = row.ServiceKey;
            }

            _items.Add(new RowViewModel(row));
        }

        UpdateElapsed();
    }

    private void UpdateElapsed()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var row in _items.OfType<RowViewModel>())
        {
            row.UpdateElapsed(now);
        }
    }

    private void StartFlash(BlockedSessionEventArgs args)
    {
        if (!_settings.FlashOnBlock || !IsVisible)
        {
            return;
        }

        var row = _items
            .OfType<RowViewModel>()
            .FirstOrDefault(candidate =>
                candidate.SessionId == args.SessionId && candidate.ServiceKey == args.ServiceKey);

        if (row is null)
        {
            return;
        }

        StopFlash(row.SessionId);

        var ticks = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            ticks++;
            row.FlashOpacity = row.FlashOpacity > 0.9 ? 0.45 : 1.0;
            if (ticks >= 12)
            {
                StopFlash(row.SessionId);
            }
        };

        _flashTimers[row.SessionId] = timer;
        timer.Start();
    }

    private void StopFlash(string sessionId)
    {
        if (_flashTimers.Remove(sessionId, out var timer))
        {
            timer.Stop();
        }

        var row = _items.OfType<RowViewModel>().FirstOrDefault(candidate => candidate.SessionId == sessionId);
        if (row is not null)
        {
            row.FlashOpacity = 1.0;
        }
    }

    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: RowViewModel row })
        {
            return;
        }

        _monitor.MarkViewed(row.ServiceKey, row.SessionId);
        StopFlash(row.SessionId);

        var service = _monitor.Services.FirstOrDefault(candidate => candidate.Key == row.ServiceKey);
        if (service is null)
        {
            return;
        }

        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is null)
        {
            return;
        }

        try
        {
            _ = launcher.LaunchUriAsync(new Uri(service.Url, "/session/" + row.SessionId));
        }
        catch (Exception exception)
        {
            _log.Write("open session failed: " + exception.Message);
            _ = launcher.LaunchUriAsync(service.Url);
        }

        e.Handled = true;
    }

    private static bool Contains(PixelRect rectangle, PixelPoint point)
        => point.X >= rectangle.X
            && point.Y >= rectangle.Y
            && point.X < rectangle.X + rectangle.Width
            && point.Y < rectangle.Y + rectangle.Height;
}
