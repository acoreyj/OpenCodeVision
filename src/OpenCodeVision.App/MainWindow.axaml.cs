using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCodeVision.App.ViewModels;
using OpenCodeVision.Core;

namespace OpenCodeVision.App;

public partial class MainWindow : Window
{
    private readonly SessionMonitor _monitor;
    private readonly Settings _settings;
    private readonly SettingsStore _store;
    private readonly DiagnosticsLog _log;
    private readonly ItemsControl _itemsControl;
    private readonly ObservableCollection<object> _items = new();
    private readonly Dictionary<string, RowViewModel> _rowCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DispatcherTimer> _flashTimers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stopInFlight = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _elapsedTimer;
    private string _signature = "";
    private OptionsWindow? _options;

    public MainWindow(SessionMonitor monitor, Settings settings, SettingsStore store, DiagnosticsLog log)
    {
        InitializeComponent();
        Icon = AppIcon.Load();

        _monitor = monitor;
        _settings = settings;
        _store = store;
        _log = log;
        _itemsControl = this.FindControl<ItemsControl>("Items")!;
        _itemsControl.ItemsSource = _items;

        Opened += OnOpened;
        Closing += OnClosing;
        KeyDown += OnKeyDown;
        Deactivated += (_, _) => ClearRowInteraction();

        _monitor.Changed += (_, _) => Dispatcher.UIThread.Post(Rebuild);
        _monitor.BlockedStarted += (_, args) => Dispatcher.UIThread.Post(() => StartFlash(args));

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => UpdateElapsed();
        _elapsedTimer.Start();

        Rebuild();
    }

    public bool HasTray { get; set; }

    public bool IsQuitting { get; set; }

    public void ShowOptions()
    {
        try
        {
            if (_options is { IsVisible: true })
            {
                _options.Activate();
                return;
            }

            _options = new OptionsWindow(_monitor, _settings, _store, ApplyAlwaysOnTop, _log.Write);
            _options.Closed += (_, _) => _options = null;
            _options.Show(this);
            _options.Activate();
        }
        catch (Exception exception)
        {
            _log.Write("options failed: " + exception);
        }
    }

    public void ApplyAlwaysOnTop() => Topmost = _settings.AlwaysOnTop;

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
        SavePlacement();

        if (HasTray && !IsQuitting)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void SavePlacement()
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
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (DisarmStops())
        {
            e.Handled = true;
            return;
        }

        if (HasTray)
        {
            Hide();
        }
        else
        {
            WindowState = WindowState.Minimized;
        }

        e.Handled = true;
    }

    private void OnMinimize(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnCloseWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (HasTray)
        {
            Hide();
        }
        else
        {
            Close();
        }
    }

    private void OnTitlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnOptions(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ShowOptions();

    private void Rebuild()
    {
        var rows = _monitor.Rows;

        var groups = new List<List<SessionRow>>();
        var groupIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!groupIndex.TryGetValue(row.ServiceKey, out var index))
            {
                index = groups.Count;
                groupIndex[row.ServiceKey] = index;
                groups.Add(new List<SessionRow>());
            }

            groups[index].Add(row);
        }

        var ordered = groups.SelectMany(group => group).ToList();
        var sequence = string.Join(
            "|",
            ordered.Select(row => row.ServiceKey + ":" + row.Session.Id));

        foreach (var row in ordered)
        {
            if (_rowCache.TryGetValue(row.Session.Id, out var existing))
            {
                existing.Update(row);
            }
            else
            {
                _rowCache[row.Session.Id] = new RowViewModel(row);
            }
        }

        var live = ordered.Select(row => row.Session.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _rowCache.Keys.Where(id => !live.Contains(id)).ToList())
        {
            StopFlash(id);
            _rowCache.Remove(id);
        }

        if (sequence != _signature)
        {
            _signature = sequence;
            _items.Clear();
            foreach (var group in groups)
            {
                _items.Add(new ServiceHeaderViewModel(group[0].ServiceName));
                foreach (var row in group)
                {
                    _items.Add(_rowCache[row.Session.Id]);
                }
            }
        }

        UpdateElapsed();
    }

    private void UpdateElapsed()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var row in _rowCache.Values)
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

        var row = _rowCache.GetValueOrDefault(args.SessionId);
        if (row is null || row.ServiceKey != args.ServiceKey)
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

        var row = _rowCache.GetValueOrDefault(sessionId);
        if (row is not null)
        {
            row.FlashOpacity = 1.0;
        }
    }

    private void OnRowPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: RowViewModel row })
        {
            row.IsHovered = true;
        }
    }

    private void OnRowPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is not Control { DataContext: RowViewModel row })
        {
            return;
        }

        row.IsHovered = false;
        row.DisarmStop();
    }

    private void OnStopClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not Control { DataContext: RowViewModel row } || row.IsStopping)
        {
            return;
        }

        if (!row.IsStopConfirming)
        {
            foreach (var candidate in _rowCache.Values)
            {
                if (!ReferenceEquals(candidate, row))
                {
                    candidate.DisarmStop();
                }
            }

            row.ArmStop();
            return;
        }

        row.DisarmStop();
        _ = StopOrHideAsync(row);
    }

    private async Task StopOrHideAsync(RowViewModel row)
    {
        if (!_stopInFlight.Add(row.SessionId))
        {
            return;
        }

        row.IsStopping = true;
        try
        {
            if (row.CanStop)
            {
                await _monitor.InterruptSessionAsync(row.ServiceKey, row.SessionId);
            }
            else
            {
                await _monitor.DismissSessionAsync(row.ServiceKey, row.SessionId);
            }
        }
        catch (Exception exception)
        {
            _log.Write("stop session failed: " + exception.Message);
        }
        finally
        {
            row.IsStopping = false;
            _stopInFlight.Remove(row.SessionId);
        }
    }

    private bool DisarmStops()
    {
        var armed = false;
        foreach (var row in _rowCache.Values)
        {
            if (!row.IsStopConfirming)
            {
                continue;
            }

            row.DisarmStop();
            armed = true;
        }

        return armed;
    }

    private void ClearRowInteraction()
    {
        foreach (var row in _rowCache.Values)
        {
            row.IsHovered = false;
            row.DisarmStop();
        }
    }

    private async void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        if (sender is not Control { DataContext: RowViewModel row })
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        try
        {
            await _monitor.MarkViewedAsync(row.ServiceKey, row.SessionId);
        }
        catch (Exception exception)
        {
            _log.Write("mark viewed failed: " + exception.Message);
        }

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
            _ = launcher.LaunchUriAsync(SessionLink.For(service.Url, row.SessionId));
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
