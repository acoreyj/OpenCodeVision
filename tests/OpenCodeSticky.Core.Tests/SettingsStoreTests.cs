using OpenCodeSticky.Core;
using Xunit;

namespace OpenCodeSticky.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new SettingsStore(Path.Combine(NewDirectory(), "settings.json"));

        var settings = store.Load();

        Assert.True(settings.AlwaysOnTop);
        Assert.True(settings.FlashOnBlock);
        Assert.True(settings.IsServiceEnabled("never-seen"));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEverything()
    {
        var path = Path.Combine(NewDirectory(), "settings.json");
        var store = new SettingsStore(path);

        var saved = new Settings
        {
            AlwaysOnTop = false,
            FlashOnBlock = false,
            WindowLeft = 120.5,
            WindowTop = 340.25,
        };
        saved.SetServiceEnabled("wsl:Ubuntu", false);
        store.Save(saved);

        var loaded = store.Load();

        Assert.False(loaded.AlwaysOnTop);
        Assert.False(loaded.FlashOnBlock);
        Assert.Equal(120.5, loaded.WindowLeft);
        Assert.Equal(340.25, loaded.WindowTop);
        Assert.False(loaded.IsServiceEnabled("wsl:Ubuntu"));
        Assert.True(loaded.IsServiceEnabled("native"));
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        var path = Path.Combine(NewDirectory(), "settings.json");
        File.WriteAllText(path, "{ not json");

        var settings = new SettingsStore(path).Load();

        Assert.True(settings.AlwaysOnTop);
        Assert.True(settings.FlashOnBlock);
    }

    [Fact]
    public void Save_WritesAtomically()
    {
        var directory = NewDirectory();
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));

        store.Save(new Settings());

        Assert.True(File.Exists(Path.Combine(directory, "settings.json")));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void SetServiceEnabled_FalseIsRemembered()
    {
        var settings = new Settings();

        settings.SetServiceEnabled("native", false);

        Assert.False(settings.IsServiceEnabled("native"));
        Assert.True(settings.ServiceEnabled.ContainsKey("native"));
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ocs-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
