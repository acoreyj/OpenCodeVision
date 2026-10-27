using Xunit;

namespace OpenCodeSticky.Core.Tests;

public class StatusEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static SessionInfo Session(
        string? parentId = null,
        string? outcome = "succeeded",
        int pending = 0,
        DateTimeOffset? created = null,
        DateTimeOffset? updated = null,
        DateTimeOffset? idle = null,
        DateTimeOffset? viewed = null,
        DateTimeOffset? lastEventAt = null,
        DateTimeOffset? pendingSince = null,
        string title = "test session")
        => new()
        {
            Id = "ses_test",
            ParentId = parentId,
            Title = title,
            Directory = @"C:\work\demo",
            Outcome = outcome,
            PendingPermissions = pending,
            Created = created ?? Now.AddHours(-1),
            Updated = updated,
            Idle = idle,
            Viewed = viewed,
            LastEventAt = lastEventAt,
            PendingSince = pendingSince,
        };

    [Fact]
    public void PendingPermission_IsWaitingApproval()
    {
        var s = Session(idle: Now.AddMinutes(-5), updated: Now.AddMinutes(-5), viewed: Now.AddMinutes(-5), pending: 1);
        Assert.Equal(SessionState.WaitingApproval, StatusEvaluator.Evaluate(s, false, Now));
    }

    [Fact]
    public void FailedAndIdle_IsErrored()
    {
        var idle = Now.AddMinutes(-5);
        var s = Session(outcome: "failed", idle: idle, updated: idle, viewed: idle);
        Assert.Equal(SessionState.Errored, StatusEvaluator.Evaluate(s, false, Now));
    }

    [Fact]
    public void IdleAndUnviewed_IsWaitingForYou()
    {
        var s = Session(idle: Now.AddMinutes(-5), updated: Now.AddMinutes(-5), viewed: Now.AddMinutes(-10));
        Assert.Equal(SessionState.WaitingForYou, StatusEvaluator.Evaluate(s, false, Now));
    }

    [Fact]
    public void IdleAndViewed_IsIdle()
    {
        var idle = Now.AddMinutes(-5);
        var s = Session(idle: idle, updated: idle, viewed: idle);
        Assert.Equal(SessionState.Idle, StatusEvaluator.Evaluate(s, false, Now));
    }

    [Fact]
    public void RecentActivity_IsWorking()
    {
        var s = Session(idle: Now.AddMinutes(-10), updated: Now.AddSeconds(-1), lastEventAt: Now.AddSeconds(-1));
        Assert.Equal(SessionState.Working, StatusEvaluator.Evaluate(s, false, Now));
    }

    [Fact]
    public void RecentActivityWithNoIdle_IsWorking()
    {
        var s = Session(idle: null, updated: Now.AddSeconds(-3));
        Assert.Equal(SessionState.Working, StatusEvaluator.Evaluate(s, false, Now));
    }

    [Fact]
    public void StaleSessionWithoutIdle_IsIdle()
    {
        var s = Session(idle: null, updated: Now.AddHours(-2));
        Assert.Equal(SessionState.Idle, StatusEvaluator.Evaluate(s, false, Now));
    }

    [Fact]
    public void LocalViewMark_OverridesBlue()
    {
        var s = Session(idle: Now.AddMinutes(-5), updated: Now.AddMinutes(-5), viewed: null);
        Assert.Equal(SessionState.Idle, StatusEvaluator.Evaluate(s, true, Now));
    }

    [Fact]
    public void BlueBlocksFor24HoursThenStops()
    {
        Assert.True(StatusEvaluator.IsBlocked(SessionState.WaitingForYou, Now.AddHours(-23), Now));
        Assert.False(StatusEvaluator.IsBlocked(SessionState.WaitingForYou, Now.AddHours(-25), Now));
    }

    [Fact]
    public void AmberBlocksAlwaysAndRedNever()
    {
        Assert.True(StatusEvaluator.IsBlocked(SessionState.WaitingApproval, Now.AddDays(-100), Now));
        Assert.False(StatusEvaluator.IsBlocked(SessionState.Errored, Now.AddMinutes(-1), Now));
        Assert.False(StatusEvaluator.IsBlocked(SessionState.Working, Now.AddMinutes(-1), Now));
        Assert.False(StatusEvaluator.IsBlocked(SessionState.Idle, Now.AddMinutes(-1), Now));
    }

    [Fact]
    public void BuildRows_ExcludesSubagentSessions()
    {
        var sessions = new[]
        {
            new ServiceSessions("native", "Windows", new[]
            {
                Session(parentId: "ses_parent", idle: Now.AddMinutes(-1), updated: Now.AddMinutes(-1)),
            }),
        };

        var rows = StatusEvaluator.BuildRows(sessions, new HashSet<string>(), Now);

        Assert.Empty(rows);
    }

    [Fact]
    public void BuildRows_KeepsOldBlockedSession()
    {
        var idle = Now.AddDays(-2);
        var sessions = new[]
        {
            new ServiceSessions("native", "Windows", new[]
            {
                Session(pending: 1, idle: idle, updated: idle, viewed: null, pendingSince: idle),
            }),
        };

        var rows = StatusEvaluator.BuildRows(sessions, new HashSet<string>(), Now);

        var row = Assert.Single(rows);
        Assert.True(row.IsBlocked);
        Assert.Equal(SessionState.WaitingApproval, row.State);
    }

    [Fact]
    public void BuildRows_DropsOldQuietSession()
    {
        var idle = Now.AddHours(-2);
        var sessions = new[]
        {
            new ServiceSessions("native", "Windows", new[]
            {
                Session(idle: idle, updated: idle, viewed: idle),
            }),
        };

        Assert.Empty(StatusEvaluator.BuildRows(sessions, new HashSet<string>(), Now));
    }

    [Fact]
    public void BuildRows_SortsBlockedLongestFirstThenRecent()
    {
        var blue = Session(idle: Now.AddHours(-1), updated: Now.AddHours(-1), viewed: null, title: "blue");
        var amber = Session(pending: 1, idle: Now.AddMinutes(-5), updated: Now.AddMinutes(-5), viewed: Now.AddMinutes(-5), pendingSince: Now.AddMinutes(-5), title: "amber");
        var working = Session(idle: Now.AddMinutes(-20), updated: Now.AddSeconds(-1), lastEventAt: Now.AddSeconds(-1), title: "working");

        var sessions = new[]
        {
            new ServiceSessions("native", "Windows", new[] { working, amber, blue }),
        };

        var rows = StatusEvaluator.BuildRows(sessions, new HashSet<string>(), Now);

        Assert.Equal(new[] { "blue", "amber", "working" }, rows.Select(r => r.Session.Title));
    }
}
