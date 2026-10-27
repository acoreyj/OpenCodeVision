using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using OpenCodeVision.Core;

namespace OpenCodeVision.App.ViewModels;

public sealed class RowViewModel : INotifyPropertyChanged
{
    private IBrush _stateBrush;
    private string _title;
    private string _tooltip;
    private bool _isOffline;
    private double _flashOpacity = 1.0;
    private string _elapsed = "";

    public RowViewModel(SessionRow row)
    {
        ServiceKey = row.ServiceKey;
        SessionId = row.Session.Id;
        Project = ProjectName(row.Session.Directory);
        _title = TitleFor(row.Session.Title, row.IsOffline);
        _tooltip = TooltipFor(row);
        _stateBrush = StateBrushes.For(row.State);
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
            }
        }
    }

    public double RowOpacity => IsOffline ? 0.4 : 1.0;

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

    public DateTimeOffset StateSince { get; private set; }

    public void Update(SessionRow row)
    {
        Title = TitleFor(row.Session.Title, row.IsOffline);
        Tooltip = TooltipFor(row);
        StateBrush = StateBrushes.For(row.State);
        IsOffline = row.IsOffline;
        StateSince = row.StateSince;
    }

    public void UpdateElapsed(DateTimeOffset now)
    {
        Elapsed = ElapsedFormatter.Format(now - StateSince);
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
        => $"{TitleFor(row.Session.Title, row.IsOffline)}\n{row.Session.Directory}";

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
