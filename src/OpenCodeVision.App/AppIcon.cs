using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OpenCodeVision.App;

public static class AppIcon
{
    private static readonly Uri Source = new("avares://OpenCodeVision/Assets/app.png");

    public static WindowIcon Load() => new(AssetLoader.Open(Source));
}
