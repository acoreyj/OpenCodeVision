using System.Net;
using System.Net.Http.Headers;
using System.Text;
using OpenCodeVision.Core;
using Xunit;

namespace OpenCodeVision.Core.Tests;

public class OpenCodeClientTests
{
    private const string SessionFixture =
        "{\"data\":[{\"id\":\"ses_e82362c9affeZIBhljTUk5mC0G\",\"title\":\"Windows sticky\"," +
        "\"outcome\":\"succeeded\",\"location\":{\"directory\":\"C:\\\\work\"}," +
        "\"time\":{\"created\":1793111741293,\"updated\":1793111996605,\"idle\":1793111816310,\"viewed\":1793111816310}}]}";

    [Fact]
    public async Task SendSetsBasicAuthHeader()
    {
        var handler = new StubHandler(_ => Json("{\"data\":[]}"));
        using var client = new OpenCodeClient(new Uri("http://127.0.0.1:28894"), "pw", handler);

        await client.GetSessionsAsync(CancellationToken.None);

        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:pw"));
        Assert.Equal(expected, handler.Authorizations.Single());
    }

    [Fact]
    public async Task GetSessions_ParsesNestedTimes()
    {
        var handler = new StubHandler(_ => Json(SessionFixture));
        using var client = new OpenCodeClient(new Uri("http://127.0.0.1:28894"), "pw", handler);

        var sessions = await client.GetSessionsAsync(CancellationToken.None);

        var session = Assert.Single(sessions);
        Assert.Equal("ses_e82362c9affeZIBhljTUk5mC0G", session.Id);
        Assert.Equal("Windows sticky", session.Title);
        Assert.Equal("succeeded", session.Outcome);
        Assert.Equal(@"C:\work", session.Directory);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793111741293), session.Created);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793111996605), session.Updated);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793111816310), session.Idle);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793111816310), session.Viewed);
    }

    [Fact]
    public async Task GetSessions_TolerantOfMissingTimesAndTitle()
    {
        var handler = new StubHandler(_ => Json("{\"data\":[{\"id\":\"ses_1\"}]}"));
        using var client = new OpenCodeClient(new Uri("http://127.0.0.1:28894"), "pw", handler);

        var session = Assert.Single(await client.GetSessionsAsync(CancellationToken.None));

        Assert.Equal("ses_1", session.Id);
        Assert.Equal("", session.Title);
        Assert.Equal("", session.Directory);
        Assert.Null(session.Updated);
        Assert.Null(session.Idle);
        Assert.Null(session.Viewed);
    }

    [Fact]
    public async Task GetSessions_SkipsEntriesWithoutId()
    {
        var handler = new StubHandler(_ => Json("{\"data\":[{\"title\":\"x\"},{\"id\":\"ses_2\"}]}"));
        using var client = new OpenCodeClient(new Uri("http://127.0.0.1:28894"), "pw", handler);

        var session = Assert.Single(await client.GetSessionsAsync(CancellationToken.None));

        Assert.Equal("ses_2", session.Id);
    }

    [Fact]
    public async Task GetPendingPermissionCount_CountsDataArray()
    {
        string? requestedPath = null;
        var handler = new StubHandler(request =>
        {
            requestedPath = request.RequestUri!.AbsolutePath;
            return Json("{\"data\":[{},{}]}");
        });
        using var client = new OpenCodeClient(new Uri("http://127.0.0.1:28894"), "pw", handler);

        var count = await client.GetPendingPermissionCountAsync("ses_1", CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal("/api/session/ses_1/permission", requestedPath);
    }

    [Fact]
    public async Task NonSuccessStatus_ThrowsOpenCodeException()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"_tag\":\"UnauthorizedError\"}"),
        });
        using var client = new OpenCodeClient(new Uri("http://127.0.0.1:28894"), "wrong", handler);

        var error = await Assert.ThrowsAsync<OpenCodeException>(
            () => client.GetSessionsAsync(CancellationToken.None));

        Assert.Equal(401, error.StatusCode);
    }

    [Fact]
    public async Task Subscribe_ParsesEventTypeAndSessionId()
    {
        const string sse =
            "data: {\"type\":\"session.tool.success\",\"created\":1793111975256,\"data\":{\"sessionID\":\"ses_1\"}}\n\n" +
            "data: {\"type\":\"server.connected\"}\n\n";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
        });
        using var client = new OpenCodeClient(new Uri("http://127.0.0.1:28894"), "pw", handler);

        var events = new List<ServerEvent>();
        await foreach (var item in client.SubscribeAsync(CancellationToken.None))
        {
            events.Add(item);
        }

        Assert.Equal(2, events.Count);
        Assert.Equal("session.tool.success", events[0].Type);
        Assert.Equal("ses_1", events[0].SessionId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793111975256), events[0].Created);
        Assert.Null(events[1].SessionId);
    }

    [Fact]
    public async Task GetInfo_ParsesVersionAndPid()
    {
        var handler = new StubHandler(_ => Json("{\"version\":\"2.2.0\",\"pid\":80716}"));
        using var client = new OpenCodeClient(new Uri("http://127.0.0.1:28894"), "pw", handler);

        var info = await client.GetInfoAsync(CancellationToken.None);

        Assert.Equal("2.2.0", info.Version);
        Assert.Equal(80716, info.Pid);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<string?> Authorizations { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(_respond(request));
        }
    }
}
