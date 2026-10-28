using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using OpenCodeVision.Core;

namespace OpenCodeVision.App.ViewModels;

public sealed class RowViewModel : INotifyPropertyChanged
{
    private static readonly IBrush StopIdleBrush = Brush.Parse("#F85149");
    private static readonly IBrush StopArmBrush = Brush.Parse("#B42318");

    private IBrush _stateBrush;
    private string _title;
    private string _tooltip;
    private bool _isOffline;
    private SessionState _state;
    private bool _isHovered;
    private bool _isStopConfirming;
    private bool _isStopping;
    private double _flashOpacity = 1.0;
    private string _elapsed = "";
    private string _timing = "";
    private SessionInfo _session;

    public RowViewModel(SessionRow row)
    {
        ServiceKey = row.ServiceKey;
        SessionId = row.Session.Id;
        Project = ProjectName(row.Session.Directory);
        _title = TitleFor(row.Session.Title, row.IsOffline);
        _tooltip = TooltipFor(row);
        _session = row.Session;
        _stateBrush = StateBrushes.For(row.State);
        _state = row.State;
        _isOffline = row.IsOffline;
        StateSince = row.StateSince;
        UpdateElapsed(DateTimeOffset.UtcNow);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ServiceKey { get; }

    public string SessionId { get; }

    public string Project { get; }

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    public string Tooltip
    {
        get => _tooltip;
        private set => Set(ref _tooltip, value);
    }

    public IBrush StateBrush
    {
        get => _stateBrush;
        private set => Set(ref _stateBrush, value);
    }

    public bool IsOffline
    {
        get => _isOffline;
        private set
        {
            if (Set(ref _isOffline, value))
            {
                OnPropertyChanged(nameof(RowOpacity));
                NotifyStopChanged();
            }
        }
    }

    public double RowOpacity => IsOffline ? 0.4 : 1.0;

    public bool CanStop => !IsOffline && _state is SessionState.Working or SessionState.WaitingApproval;

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (Set(ref _isHovered, value))
            {
                OnPropertyChanged(nameof(ShowStopAction));
                OnPropertyChanged(nameof(ShowElapsed));
            }
        }
    }

    public bool IsStopConfirming => _isStopConfirming;

    public bool IsStopping
    {
        get => _isStopping;
        set
        {
            if (Set(ref _isStopping, value))
            {
                OnPropertyChanged(nameof(StopLabel));
                OnPropertyChanged(nameof(StopTooltip));
                OnPropertyChanged(nameof(IsStopEnabled));
                OnPropertyChanged(nameof(ShowStopAction));
                OnPropertyChanged(nameof(ShowElapsed));
            }
        }
    }

    public bool IsStopEnabled => !IsStopping;

    public bool ShowStopAction => IsHovered || IsStopping;

    public bool ShowElapsed => !ShowStopAction;

    public double StopWidth => IsStopConfirming ? 58 : 30;

    public double StopFontSize => IsStopConfirming ? 11 : 17;

    public IBrush StopBackground => IsStopConfirming ? StopArmBrush : StopIdleBrush;

    public string StopLabel => IsStopping
        ? "…"
        : IsStopConfirming
            ? CanStop ? "Stop?" : "Hide?"
            : "✕";

    public string StopTooltip => IsStopping
        ? CanStop ? "Stopping…" : "Hiding…"
        : IsStopConfirming
            ? "Click again to confirm"
            : CanStop
                ? "Stop this session"
                : "Hide this session until it updates";

    public void ArmStop()
    {
        if (_isStopConfirming)
        {
            return;
        }

        _isStopConfirming = true;
        OnPropertyChanged(nameof(IsStopConfirming));
        OnPropertyChanged(nameof(StopLabel));
        OnPropertyChanged(nameof(StopTooltip));
        OnPropertyChanged(nameof(StopWidth));
        OnPropertyChanged(nameof(StopFontSize));
        OnPropertyChanged(nameof(StopBackground));
    }

    public void DisarmStop()
    {
        if (!_isStopConfirming)
        {
            return;
        }

        _isStopConfirming = false;
        OnPropertyChanged(nameof(IsStopConfirming));
        OnPropertyChanged(nameof(StopLabel));
        OnPropertyChanged(nameof(StopTooltip));
        OnPropertyChanged(nameof(StopWidth));
        OnPropertyChanged(nameof(StopFontSize));
        OnPropertyChanged(nameof(StopBackground));
    }

    public double FlashOpacity
    {
        get => _flashOpacity;
        set => Set(ref _flashOpacity, value);
    }

    public string Elapsed
    {
        get => _elapsed;
        private set => Set(ref _elapsed, value);
    }

    /// <summary>Session age and time since the last update, e.g. "open 2h · updated 5m ago".</summary>
    public string Timing
    {
        get => _timing;
        private set
        {
            if (Set(ref _timing, value))
            {
                OnPropertyChanged(nameof(HasTiming));
            }
        }
    }

    public bool HasTiming => _timing.Length > 0;

    public DateTimeOffset StateSince { get; private set; }

    public void Update(SessionRow row)
    {
        _session = row.Session;
        Title = TitleFor(row.Session.Title, row.IsOffline);
        Tooltip = TooltipFor(row);
        StateBrush = StateBrushes.For(row.State);
        IsOffline = row.IsOffline;
        if (_state != row.State)
        {
            _state = row.State;
            NotifyStopChanged();
        }

        StateSince = row.StateSince;
    }

    public void UpdateElapsed(DateTimeOffset now)
    {
        Elapsed = ElapsedFormatter.Format(now - StateSince);
        Timing = SessionTiming.Describe(_session, now);
    }

    private static string ProjectName(string directory)
    {
        var trimmed = directory.TrimEnd('/', '\\');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? directory : name;
    }

    private static string TitleFor(string title, bool offline)
    {
        var text = string.IsNullOrWhiteSpace(title) ? "(untitled)" : title;
        return offline ? text + " · offline" : text;
    }

    private static string TooltipFor(SessionRow row)
    {
        var lines = new List<string>
        {
            TitleFor(row.Session.Title, row.IsOffline),
            row.Session.Directory,
        };

        if (row.Session.Created is { } created)
        {
            lines.Add("opened " + created.ToLocalTime().ToString("t"));
        }

        if ((row.Session.Updated ?? row.Session.LastEventAt) is { } updated)
        {
            lines.Add("last update " + updated.ToLocalTime().ToString("t"));
        }

        return string.Join("\n", lines);
    }

    private void NotifyStopChanged()
    {
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(StopLabel));
        OnPropertyChanged(nameof(StopTooltip));
        if (!CanStop)
        {
            DisarmStop();
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal static class StateBrushes
{
    private static readonly Dictionary<SessionState, IBrush> Map = new()
    {
        [SessionState.Idle] = Brush.Parse("#8A8A8A"),
        [SessionState.Working] = Brush.Parse("#3FB950"),
        [SessionState.WaitingApproval] = Brush.Parse("#D29922"),
        [SessionState.WaitingForYou] = Brush.Parse("#388BFD"),
        [SessionState.Errored] = Brush.Parse("#F85149"),
    };

    public static IBrush For(SessionState state) => Map[state];
}
