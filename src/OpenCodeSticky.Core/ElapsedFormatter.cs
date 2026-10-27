namespace OpenCodeSticky.Core;

public static class ElapsedFormatter
{
    public static string Format(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalSeconds < 60)
        {
            return $"{(int)span.TotalSeconds}s";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{(int)span.TotalMinutes}m";
        }

        if (span.TotalHours < 24)
        {
            return $"{(int)span.TotalHours}h";
        }

        return $"{(int)span.TotalDays}d";
    }
}
