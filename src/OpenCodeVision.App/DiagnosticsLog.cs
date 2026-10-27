using System.Globalization;

namespace OpenCodeVision.App;

public sealed class DiagnosticsLog
{
    private const long MaxBytes = 1_000_000;

    private readonly object _lock = new();
    private readonly string _path;

    public DiagnosticsLog(string path)
    {
        _path = path;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
            {
                File.WriteAllText(path, string.Empty);
            }
        }
        catch
        {
            // Logging must never prevent startup.
        }
    }

    public void Write(string line)
    {
        lock (_lock)
        {
            try
            {
                File.AppendAllText(
                    _path,
                    DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + line + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }
}
