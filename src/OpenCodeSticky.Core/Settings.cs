namespace OpenCodeSticky.Core;

public sealed class Settings
{
    public bool AlwaysOnTop { get; set; } = true;

    public bool FlashOnBlock { get; set; } = true;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public Dictionary<string, bool> ServiceEnabled { get; set; } = new();

    public bool IsServiceEnabled(string key)
        => !ServiceEnabled.TryGetValue(key, out var enabled) || enabled;

    public void SetServiceEnabled(string key, bool enabled)
        => ServiceEnabled[key] = enabled;
}
