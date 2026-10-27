using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenCodeSticky.Core;

namespace OpenCodeSticky.App;

public sealed class AppTrayIcon : IDisposable
{
    private readonly MainWindow _window;
    private readonly SessionMonitor _monitor;
    private readonly TrayIcon _tray;
    private readonly TrayIcons _icons;
    private SessionState _state = SessionState.Idle;
    private bool _disposed;

    public AppTrayIcon(MainWindow window, SessionMonitor monitor, Action openOptions, Action quit)
    {
        _window = window;
        _monitor = monitor;

        _tray = new TrayIcon
        {
            ToolTipText = "opencode",
            Icon = LoadIcon(SessionState.Idle),
        };

        var menu = new NativeMenu();
        menu.Add(Item("Show / Hide", Toggle));
        menu.Add(Item("Options…", openOptions));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Quit", quit));
        _tray.Menu = menu;
        _tray.Clicked += (_, _) => Toggle();

        _icons = new TrayIcons { _tray };
        TrayIcon.SetIcons(Application.Current!, _icons);

        _monitor.Changed += OnChanged;
        Update();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitor.Changed -= OnChanged;
        TrayIcon.SetIcons(Application.Current!, null);
        _tray.Dispose();
    }

    private void OnChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        var worst = SessionState.Idle;
        var blocked = 0;

        foreach (var row in _monitor.Rows)
        {
            if (row.IsOffline)
            {
                continue;
            }

            if (row.IsBlocked)
            {
                blocked++;
            }

            if (Priority(row.State) > Priority(worst))
            {
                worst = row.State;
            }
        }

        if (worst != _state)
        {
            _state = worst;
            _tray.Icon = LoadIcon(worst);
        }

        _tray.ToolTipText = blocked > 0 ? $"opencode — {blocked} blocked" : "opencode";
    }

    private void Toggle()
    {
        if (_window.IsVisible)
        {
            _window.Hide();
        }
        else
        {
            _window.Show();
            _window.Activate();
        }
    }

    private static int Priority(SessionState state) => state switch
    {
        SessionState.WaitingApproval => 4,
        SessionState.WaitingForYou => 3,
        SessionState.Errored => 2,
        SessionState.Working => 1,
        _ => 0,
    };

    private static WindowIcon LoadIcon(SessionState state)
    {
        var name = state switch
        {
            SessionState.WaitingApproval => "approval",
            SessionState.WaitingForYou => "waiting",
            SessionState.Errored => "errored",
            SessionState.Working => "working",
            _ => "idle",
        };

        return new WindowIcon(AssetLoader.Open(new Uri($"avares://OpenCodeSticky/Assets/tray-{name}.png")));
    }

    private static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }
}
