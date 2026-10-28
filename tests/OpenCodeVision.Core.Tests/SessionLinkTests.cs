using Xunit;

namespace OpenCodeVision.Core.Tests;

public class SessionLinkTests
{
    [Fact]
    public void For_BuildsServerScopedWebRoute()
    {
        var link = SessionLink.For(new Uri("http://127.0.0.1:49374"), "ses_abc123");

        Assert.Equal(
            "http://127.0.0.1:49374/server/aHR0cDovLzEyNy4wLjAuMTo0OTM3NA/session/ses_abc123",
            link.ToString());
    }

    [Fact]
    public void For_TrailingSlash_ProducesTheSameKey()
    {
        var withSlash = SessionLink.For(new Uri("http://127.0.0.1:49374/"), "ses_abc123");
        var withoutSlash = SessionLink.For(new Uri("http://127.0.0.1:49374"), "ses_abc123");

        Assert.Equal(withoutSlash, withSlash);
    }

    [Fact]
    public void For_ServiceWithPath_ReplacesThePath()
    {
        var link = SessionLink.For(new Uri("http://example.test:8080/"), "ses_abc123");

        Assert.Equal("http://example.test:8080", link.GetLeftPart(UriPartial.Authority));
        Assert.EndsWith("/session/ses_abc123", link.AbsolutePath);
        Assert.DoesNotContain('=', link.AbsolutePath);
        Assert.DoesNotContain('+', link.AbsolutePath);
    }
}
