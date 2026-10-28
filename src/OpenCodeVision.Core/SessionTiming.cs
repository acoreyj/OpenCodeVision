namespace OpenCodeVision.Core;

/// <summary>
/// The two ages a row shows: how long the session has been open, and how long
/// ago opencode last touched it. Both are approximate, built from opencode's
/// own timestamps and clamped by <see cref="ElapsedFormatter"/>.
/// </summary>
public static class SessionTiming
{
    public static string Describe(SessionInfo session, DateTimeOffset now)
    {
        var open = session.Created is { } created
            ? "open " + ElapsedFormatter.Format(now - created)
            : "";

        var updatedAt = session.Updated ?? session.LastEventAt;
        var updated = updatedAt is { } last
            ? "updated " + ElapsedFormatter.Format(now - last) + " ago"
            : "";

        if (open.Length == 0)
        {
            return updated;
        }

        return updated.Length == 0 ? open : open + " · " + updated;
    }
}
