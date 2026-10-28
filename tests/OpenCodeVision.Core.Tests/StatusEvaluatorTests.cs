using Xunit;

namespace OpenCodeVision.Core.Tests;

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
        string title = "test session",
        string id = "ses_test")
        => new()
        {
            Id = id,
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
    public void OpenStep_IsWorkingPastTheGraceWindow()
    {
        // A tool call leaves updated and lastEventAt behind for minutes at a time.
        var stale = Now.AddMinutes(-10);
        var s = Session(idle: stale, updated: stale, viewed: stale) with
        {
            StepOpen = true,
            StepStartedAt = Now.AddMinutes(-10),
        };

        Assert.Equal(SessionState.Working, StatusEvaluator.Evaluate(s, false, Now));
    }

    [Fact]
    public void OpenStep_ExpiresAfterStepCeiling()
    {
        var stale = Now.AddHours(-2);
        var s = Session(idle: stale, updated: stale, viewed: stale) with
        {
            StepOpen = true,
            StepStartedAt = Now.AddMinutes(-31),
        };

        Assert.Equal(SessionState.Idle, StatusEvaluator.Evaluate(s, false, Now));
    }

    [Fact]
    public void OpenStep_DoesNotOverridePendingPermission()
    {
        var stale = Now.AddMinutes(-10);
        var s = Session(idle: stale, updated: stale, viewed: stale, pending: 1, pendingSince: stale) with
        {
            StepOpen = true,
            StepStartedAt = Now.AddMinutes(-10),
        };

        Assert.Equal(SessionState.WaitingApproval, StatusEvaluator.Evaluate(s, false, Now));
    }

    [Fact]
    public void ClosedStep_FallsBackToGrace()
    {
        var stale = Now.AddMinutes(-10);
        var s = Session(idle: stale, updated: stale, viewed: stale) with
        {
            StepOpen = false,
            StepStartedAt = null,
        };

        Assert.Equal(SessionState.Idle, StatusEvaluator.Evaluate(s, false, Now));
    }

    [Fact]
    public void BuildRows_KeepsRowWithOpenStepPastCutoff()
    {
        var stale = Now.AddHours(-2);
        var sessions = new[]
        {
            new ServiceSessions("native", "Windows", new[]
            {
                Session(idle: stale, updated: stale, viewed: stale) with
                {
                    StepOpen = true,
                    StepStartedAt = Now.AddMinutes(-10),
                },
            }),
        };

        var rows = StatusEvaluator.BuildRows(sessions, new HashSet<string>(), Now);

        Assert.Equal(SessionState.Working, Assert.Single(rows).State);
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

    [Fact]
    public void BuildRows_GroupsUnblockedRowsByColorThenTitle()
    {
        var grey = Session(id: "ses_grey", idle: Now.AddMinutes(-5), updated: Now.AddMinutes(-5), viewed: Now.AddMinutes(-5), title: "grey");
        var greenB = Session(id: "ses_green_b", idle: Now.AddMinutes(-5), updated: Now.AddSeconds(-1), lastEventAt: Now.AddSeconds(-1), title: "green b");
        var greenA = Session(id: "ses_green_a", idle: Now.AddMinutes(-5), updated: Now.AddSeconds(-2), lastEventAt: Now.AddSeconds(-2), title: "green a");
        var red = Session(id: "ses_red", outcome: "failed", idle: Now.AddMinutes(-5), updated: Now.AddMinutes(-5), viewed: Now.AddMinutes(-5), title: "red");

        var sessions = new[]
        {
            new ServiceSessions("native", "Windows", new[] { grey, greenB, red, greenA }),
        };

        var rows = StatusEvaluator.BuildRows(sessions, new HashSet<string>(), Now);

        Assert.Equal(new[] { "red", "green a", "green b", "grey" }, rows.Select(r => r.Session.Title));
    }

    [Fact]
    public void BuildRows_DoesNotReorderWhenActivityMovesWithinAColor()
    {
        var before = new[]
        {
            new ServiceSessions("native", "Windows", new[]
            {
                Session(id: "ses_beta", idle: Now.AddMinutes(-5), updated: Now.AddSeconds(-2), lastEventAt: Now.AddSeconds(-2), title: "beta"),
                Session(id: "ses_alpha", idle: Now.AddMinutes(-5), updated: Now.AddSeconds(-1), lastEventAt: Now.AddSeconds(-1), title: "alpha"),
            }),
        };

        var after = new[]
        {
            new ServiceSessions("native", "Windows", new[]
            {
                Session(id: "ses_beta", idle: Now.AddMinutes(-5), updated: Now.AddSeconds(-1), lastEventAt: Now.AddSeconds(-1), title: "beta"),
                Session(id: "ses_alpha", idle: Now.AddMinutes(-5), updated: Now.AddSeconds(-3), lastEventAt: Now.AddSeconds(-3), title: "alpha"),
            }),
        };

        var beforeTitles = StatusEvaluator.BuildRows(before, new HashSet<string>(), Now).Select(r => r.Session.Title);
        var afterTitles = StatusEvaluator.BuildRows(after, new HashSet<string>(), Now).Select(r => r.Session.Title);

        Assert.Equal(new[] { "alpha", "beta" }, beforeTitles);
        Assert.Equal(new[] { "alpha", "beta" }, afterTitles);
    }

    [Fact]
    public void BuildRows_UsesIdToBreakTitleTies()
    {
        var first = new[]
        {
            new ServiceSessions("native", "Windows", new[]
            {
                Session(id: "ses_b", idle: Now.AddMinutes(-5), updated: Now.AddSeconds(-1), lastEventAt: Now.AddSeconds(-1), title: "same"),
                Session(id: "ses_a", idle: Now.AddMinutes(-5), updated: Now.AddSeconds(-2), lastEventAt: Now.AddSeconds(-2), title: "same"),
            }),
        };

        var reversed = new[]
        {
            new ServiceSessions("native", "Windows", new[]
            {
                Session(id: "ses_a", idle: Now.AddMinutes(-5), updated: Now.AddSeconds(-1), lastEventAt: Now.AddSeconds(-1), title: "same"),
                Session(id: "ses_b", idle: Now.AddMinutes(-5), updated: Now.AddSeconds(-2), lastEventAt: Now.AddSeconds(-2), title: "same"),
            }),
        };

        var firstIds = StatusEvaluator.BuildRows(first, new HashSet<string>(), Now).Select(r => r.Session.Id);
        var reversedIds = StatusEvaluator.BuildRows(reversed, new HashSet<string>(), Now).Select(r => r.Session.Id);

        Assert.Equal(new[] { "ses_a", "ses_b" }, firstIds);
        Assert.Equal(new[] { "ses_a", "ses_b" }, reversedIds);
    }
}
