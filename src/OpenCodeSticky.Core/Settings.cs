namespace OpenCodeSticky.Core;

public sealed class Settings
{
    private readonly object _lock = new();

    public bool AlwaysOnTop { get; set; } = true;

    public bool FlashOnBlock { get; set; } = true;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public Dictionary<string, bool> ServiceEnabled { get; set; } = new();

    public bool IsServiceEnabled(string key)
    {
        lock (_lock)
        {
            return !ServiceEnabled.TryGetValue(key, out var enabled) || enabled;
        }
    }

    public void SetServiceEnabled(string key, bool enabled)
    {
        lock (_lock)
        {
            ServiceEnabled[key] = enabled;
        }
    }

    public Settings Snapshot()
    {
        lock (_lock)
        {
            return new Settings
            {
                AlwaysOnTop = AlwaysOnTop,
                FlashOnBlock = FlashOnBlock,
                WindowLeft = WindowLeft,
                WindowTop = WindowTop,
                ServiceEnabled = new Dictionary<string, bool>(ServiceEnabled),
            };
        }
    }
}
