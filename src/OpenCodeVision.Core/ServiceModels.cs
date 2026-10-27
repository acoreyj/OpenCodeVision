namespace OpenCodeVision.Core;

public sealed record SessionRow(
    string ServiceKey,
    string ServiceName,
    SessionInfo Session,
    SessionState State,
    DateTimeOffset StateSince,
    bool IsBlocked,
    bool IsOffline);

public sealed record ServiceSessions(string Key, string Name, IReadOnlyList<SessionInfo> Sessions);
