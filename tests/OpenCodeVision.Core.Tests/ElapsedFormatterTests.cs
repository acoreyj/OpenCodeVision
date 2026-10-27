using Xunit;

namespace OpenCodeVision.Core.Tests;

public class ElapsedFormatterTests
{
    [Fact]
    public void Format_BucketsSecondsMinutesHoursDays()
    {
        Assert.Equal("0s", ElapsedFormatter.Format(TimeSpan.Zero));
        Assert.Equal("42s", ElapsedFormatter.Format(TimeSpan.FromSeconds(42)));
        Assert.Equal("5m", ElapsedFormatter.Format(TimeSpan.FromMinutes(5)));
        Assert.Equal("3h", ElapsedFormatter.Format(TimeSpan.FromHours(3)));
        Assert.Equal("2d", ElapsedFormatter.Format(TimeSpan.FromDays(2)));
    }

    [Fact]
    public void Format_NegativeSpan_IsZero()
    {
        Assert.Equal("0s", ElapsedFormatter.Format(TimeSpan.FromSeconds(-5)));
    }
}
