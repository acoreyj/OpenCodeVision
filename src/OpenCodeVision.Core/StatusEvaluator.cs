namespace OpenCodeVision.Core;

public static class StatusEvaluator
{
    public const int RecentMinutes = 30;
    public static readonly TimeSpan WorkingGrace = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan IdleTolerance = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan BlueLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan StepCeiling = TimeSpan.FromMinutes(30);

    public static SessionState Evaluate(SessionInfo session, bool viewedLocally, DateTimeOffset now)
    {
        if (session.PendingPermissions > 0)
        {
            return SessionState.WaitingApproval;
        }

        var lastActivity = Max(session.Updated, session.LastEventAt);
        var quiet = session.Idle is { } idle
            && (session.Updated is null || session.Updated <= idle + IdleTolerance);

        // A long tool run emits nothing and leaves updated behind, so an open
        // step outranks both the quiet flag and the grace window.
        if (HasOpenStep(session, now)
            || (lastActivity is { } activity && now - activity <= WorkingGrace && !quiet))
        {
            return SessionState.Working;
        }

        if (string.Equals(session.Outcome, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return SessionState.Errored;
        }

        if (session.Viewed is { } viewed && session.Idle is { } idleAt && viewed >= idleAt)
        {
            return SessionState.Idle;
        }

        if (viewedLocally)
        {
            return SessionState.Idle;
        }

        return session.Idle is not null ? SessionState.WaitingForYou : SessionState.Idle;
    }

    public static DateTimeOffset StateSince(SessionInfo session, SessionState state, DateTimeOffset now)
    {
        return state switch
        {
            SessionState.WaitingApproval => session.PendingSince ?? session.Idle ?? session.Updated ?? now,
            SessionState.Working => (session.Idle + IdleTolerance) ?? session.Created ?? session.Updated ?? now,
            _ => session.Idle ?? session.Updated ?? session.Created ?? now,
        };
    }

    public static bool IsBlocked(SessionState state, DateTimeOffset since, DateTimeOffset now)
    {
        return state switch
        {
            SessionState.WaitingApproval => true,
            SessionState.WaitingForYou => now - since < BlueLifetime,
            _ => false,
        };
    }

    /// <summary>
    /// True while opencode reported an assistant step that has not ended yet.
    /// The ceiling bounds a step whose <c>session.step.ended</c> frame was lost.
    /// </summary>
    public static bool HasOpenStep(SessionInfo session, DateTimeOffset now)
        => session.StepOpen
            && session.StepStartedAt is { } started
            && now - started <= StepCeiling;

    public static IReadOnlyList<SessionRow> BuildRows(
        IEnumerable<ServiceSessions> services,
        IReadOnlySet<string> viewedLocally,
        DateTimeOffset now)
    {
        var cutoff = now.AddMinutes(-RecentMinutes);
        var rows = new List<SessionRow>();

        foreach (var service in services)
        {
            foreach (var session in service.Sessions)
            {
                if (session.ParentId is not null)
                {
                    continue;
                }

                var state = Evaluate(session, viewedLocally.Contains(session.Id), now);
                var since = StateSince(session, state, now);
                var blocked = IsBlocked(state, since, now);
                var lastActivity = session.Updated ?? session.LastEventAt ?? session.Created;

                if (!blocked
                    && !HasOpenStep(session, now)
                    && (lastActivity is null || lastActivity < cutoff))
                {
                    continue;
                }

                rows.Add(new SessionRow(
                    service.Key,
                    service.Name,
                    session,
                    state,
                    since,
                    blocked,
                    IsOffline: false));
            }
        }

        var blockedRows = rows
            .Where(r => r.IsBlocked)
            .OrderBy(r => r.StateSince)
            .ThenBy(r => r.Session.Title, StringComparer.Ordinal)
            .ThenBy(r => r.Session.Id, StringComparer.Ordinal);

        // Color is the only thing that moves a row once it is on screen. Within a
        // color the order is title-based so message updates cannot shuffle it.
        var unblockedRows = rows
            .Where(r => !r.IsBlocked)
            .OrderByDescending(r => Priority(r.State))
            .ThenBy(r => r.Session.Title, StringComparer.Ordinal)
            .ThenBy(r => r.Session.Id, StringComparer.Ordinal);

        return blockedRows.Concat(unblockedRows).ToList();
    }

    public static int Priority(SessionState state) => state switch
    {
        SessionState.WaitingApproval => 4,
        SessionState.WaitingForYou => 3,
        SessionState.Errored => 2,
        SessionState.Working => 1,
        _ => 0,
    };

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        return a >= b ? a : b;
    }
}
