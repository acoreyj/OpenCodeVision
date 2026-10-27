using System.Text.Json;

namespace OpenCodeVision.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public SettingsStore(string path)
    {
        _path = path;
    }

    public Settings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new Settings();
            }

            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path), Options)
                ?? new Settings();
            settings.ServiceEnabled ??= new Dictionary<string, bool>();
            return settings;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public void Save(Settings settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings.Snapshot(), Options));
        File.Move(temporary, _path, overwrite: true);
    }
}
