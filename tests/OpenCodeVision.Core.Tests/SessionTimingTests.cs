using Xunit;

namespace OpenCodeVision.Core.Tests;

public class SessionTimingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Describe_WithCreatedAndUpdated_ShowsBothAges()
    {
        var session = new SessionInfo
        {
            Id = "s1",
            Created = Now.AddHours(-2),
            Updated = Now.AddMinutes(-5),
        };

        Assert.Equal("open 2h · updated 5m ago", SessionTiming.Describe(session, Now));
    }

    [Fact]
    public void Describe_WithoutCreated_ShowsOnlyUpdateAge()
    {
        var session = new SessionInfo { Id = "s1", Updated = Now.AddSeconds(-42) };

        Assert.Equal("updated 42s ago", SessionTiming.Describe(session, Now));
    }

    [Fact]
    public void Describe_WithoutUpdated_FallsBackToLastEvent()
    {
        var session = new SessionInfo { Id = "s1", LastEventAt = Now.AddMinutes(-3) };

        Assert.Equal("updated 3m ago", SessionTiming.Describe(session, Now));
    }

    [Fact]
    public void Describe_WithoutTimestamps_IsEmpty()
    {
        Assert.Equal("", SessionTiming.Describe(new SessionInfo { Id = "s1" }, Now));
    }

    [Fact]
    public void Describe_FutureTimestamps_ClampToZero()
    {
        var session = new SessionInfo
        {
            Id = "s1",
            Created = Now.AddMinutes(5),
            Updated = Now.AddMinutes(5),
        };

        Assert.Equal("open 0s · updated 0s ago", SessionTiming.Describe(session, Now));
    }
}
