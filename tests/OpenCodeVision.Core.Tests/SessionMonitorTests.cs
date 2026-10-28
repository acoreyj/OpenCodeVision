using OpenCodeVision.Core;
using OpenCodeVision.Core.Discovery;
using System.Threading.Channels;
using Xunit;

namespace OpenCodeVision.Core.Tests;

public class SessionMonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Start_BuildsRowsFromEnabledServices()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient();
        client.Sessions.Add(IdleSession("ses_idle", Now));
        client.Sessions.Add(WorkingSession("ses_work", Now));

        await using var monitor = NewMonitor(discovery, _ => client, new MutableTimeProvider(Now));
        await monitor.StartAsync(CancellationToken.None);

        Assert.Equal(2, monitor.Rows.Count);
        Assert.True(monitor.Services.Single().Online);
    }

    [Fact]
    public async Task DisabledService_HasNoRowsAndNoRequests()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient();
        client.Sessions.Add(IdleSession("ses_idle", Now));
        var settings = new Settings();
        settings.SetServiceEnabled("native", false);

        await using var monitor = NewMonitor(discovery, _ => client, new MutableTimeProvider(Now), settings);
        await monitor.StartAsync(CancellationToken.None);

        Assert.Empty(monitor.Rows);
        Assert.Equal(0, client.SessionCalls);
        Assert.Equal(0, client.InfoCalls);
    }

    [Fact]
    public async Task SetServiceEnabled_OffRemovesRowsImmediately()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient();
        client.Sessions.Add(IdleSession("ses_idle", Now));

        await using var monitor = NewMonitor(discovery, _ => client, new MutableTimeProvider(Now));
        await monitor.StartAsync(CancellationToken.None);
        Assert.Single(monitor.Rows);

        await monitor.SetServiceEnabledAsync("native", false);

        Assert.Empty(monitor.Rows);
        var calls = client.SessionCalls;
        await monitor.RefreshNowAsync(CancellationToken.None);
        Assert.Equal(calls, client.SessionCalls);
    }

    [Fact]
    public async Task Offline_KeepsLastRowsForFiveMinutesThenDrops()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient();
        client.Sessions.Add(IdleSession("ses_idle", Now));
        var time = new MutableTimeProvider(Now);

        await using var monitor = NewMonitor(discovery, _ => client, time);
        await monitor.StartAsync(CancellationToken.None);

        client.Failure = new HttpRequestException("down");
        time.Advance(TimeSpan.FromMinutes(4));
        await monitor.RefreshNowAsync(CancellationToken.None);

        Assert.True(Assert.Single(monitor.Rows).IsOffline);

        time.Advance(TimeSpan.FromMinutes(2));
        await monitor.RefreshNowAsync(CancellationToken.None);

        Assert.Empty(monitor.Rows);
    }

    [Fact]
    public async Task OfflineRows_AreNotBlocked()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient();
        client.Sessions.Add(BlueSession("ses_blue", Now));
        var time = new MutableTimeProvider(Now);

        await using var monitor = NewMonitor(discovery, _ => client, time);
        await monitor.StartAsync(CancellationToken.None);
        Assert.True(Assert.Single(monitor.Rows).IsBlocked);

        client.Failure = new HttpRequestException("down");
        time.Advance(TimeSpan.FromMinutes(1));
        await monitor.RefreshNowAsync(CancellationToken.None);

        var row = Assert.Single(monitor.Rows);
        Assert.True(row.IsOffline);
        Assert.False(row.IsBlocked);
    }

    [Fact]
    public async Task BlockedStarted_FiresOncePerTransition()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient();
        client.Sessions.Add(IdleSession("ses_idle", Now));

        await using var monitor = NewMonitor(discovery, _ => client, new MutableTimeProvider(Now));
        await monitor.StartAsync(CancellationToken.None);

        var fired = new List<BlockedSessionEventArgs>();
        monitor.BlockedStarted += (_, args) => fired.Add(args);

        client.Sessions.Clear();
        client.Sessions.Add(BlueSession("ses_blue", Now));
        await monitor.RefreshNowAsync(CancellationToken.None);

        var blocked = Assert.Single(fired);
        Assert.Equal("native", blocked.ServiceKey);
        Assert.Equal("ses_blue", blocked.SessionId);

        await monitor.RefreshNowAsync(CancellationToken.None);
        Assert.Single(fired);
    }

    [Fact]
    public async Task MarkViewed_DowngradesWaitingForYou()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient();
        client.Sessions.Add(BlueSession("ses_blue", Now));

        await using var monitor = NewMonitor(discovery, _ => client, new MutableTimeProvider(Now));
        await monitor.StartAsync(CancellationToken.None);
        Assert.Equal(SessionState.WaitingForYou, monitor.Rows.Single().State);

        await monitor.MarkViewedAsync("native", "ses_blue");

        Assert.Equal(SessionState.Idle, monitor.Rows.Single().State);
    }

    [Fact]
    public async Task Interrupt_CallsTheOwningServiceAndRefreshes()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate(key: "native", url: "http://127.0.0.1:1"));
        discovery.Candidates.Add(Candidate(key: "wsl:Ubuntu", url: "http://127.0.0.1:2"));
        var native = new FakeClient();
        native.Sessions.Add(WorkingSession("ses_native", Now));
        var ubuntu = new FakeClient();
        ubuntu.Sessions.Add(WorkingSession("ses_ubuntu", Now));

        await using var monitor = NewMonitor(
            discovery,
            descriptor => descriptor.Key == "native" ? native : ubuntu,
            new MutableTimeProvider(Now));
        await monitor.StartAsync(CancellationToken.None);

        await monitor.InterruptSessionAsync("native", "ses_native");

        Assert.Equal(new[] { "ses_native" }, native.InterruptedSessions);
        Assert.Empty(ubuntu.InterruptedSessions);
    }

    [Fact]
    public async Task Interrupt_FailureKeepsTheRow()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient { InterruptFailure = new HttpRequestException("down") };
        client.Sessions.Add(WorkingSession("ses_work", Now));

        await using var monitor = NewMonitor(discovery, _ => client, new MutableTimeProvider(Now));
        await monitor.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => monitor.InterruptSessionAsync("native", "ses_work"));

        Assert.Single(monitor.Rows);
    }

    [Fact]
    public async Task Interrupt_ReportsAlreadyIdleAsDismissal()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient { InterruptResult = false };
        client.Sessions.Add(WorkingSession("ses_work", Now));

        await using var monitor = NewMonitor(discovery, _ => client, new MutableTimeProvider(Now));
        await monitor.StartAsync(CancellationToken.None);
        Assert.Single(monitor.Rows);

        await monitor.InterruptSessionAsync("native", "ses_work");

        Assert.Empty(monitor.Rows);
    }

    [Fact]
    public async Task Dismiss_HidesRowUntilNewActivity()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient();
        client.Sessions.Add(IdleSession("ses_idle", Now));
        var time = new MutableTimeProvider(Now);

        await using var monitor = NewMonitor(discovery, _ => client, time);
        await monitor.StartAsync(CancellationToken.None);
        Assert.Single(monitor.Rows);

        await monitor.DismissSessionAsync("native", "ses_idle");
        Assert.Empty(monitor.Rows);

        time.Advance(TimeSpan.FromMinutes(1));
        var idle = time.Now.AddSeconds(-1);
        client.Sessions.Clear();
        client.Sessions.Add(Session("ses_idle", idle, idle, null, lastEventAt: idle));
        await monitor.RefreshNowAsync(CancellationToken.None);

        Assert.Single(monitor.Rows);
    }

    [Fact]
    public async Task Dismiss_OfflineRowStaysHidden()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient();
        client.Sessions.Add(IdleSession("ses_idle", Now));
        var time = new MutableTimeProvider(Now);

        await using var monitor = NewMonitor(discovery, _ => client, time);
        await monitor.StartAsync(CancellationToken.None);

        client.Failure = new HttpRequestException("down");
        time.Advance(TimeSpan.FromMinutes(1));
        await monitor.RefreshNowAsync(CancellationToken.None);
        Assert.True(Assert.Single(monitor.Rows).IsOffline);

        await monitor.DismissSessionAsync("native", "ses_idle");

        Assert.Empty(monitor.Rows);
    }

    [Fact]
    public async Task Dismiss_ReappearingBlockedSessionRefires()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient();
        client.Sessions.Add(BlueSession("ses_blue", Now));
        var time = new MutableTimeProvider(Now);

        await using var monitor = NewMonitor(discovery, _ => client, time);
        await monitor.StartAsync(CancellationToken.None);

        var fired = new List<BlockedSessionEventArgs>();
        monitor.BlockedStarted += (_, args) => fired.Add(args);

        await monitor.DismissSessionAsync("native", "ses_blue");
        Assert.Empty(monitor.Rows);

        time.Advance(TimeSpan.FromMinutes(1));
        var idle = time.Now.AddSeconds(-1);
        client.Sessions.Clear();
        client.Sessions.Add(Session("ses_blue", idle, idle, time.Now.AddMinutes(-10)));
        await monitor.RefreshNowAsync(CancellationToken.None);

        Assert.Single(monitor.Rows);
        Assert.Single(fired);
    }

    [Fact]
    public async Task Rescan_PicksUpNewServiceAndNewPassword()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate(password: "old"));
        var clients = new Dictionary<string, FakeClient>
        {
            ["old"] = new() { Failure = new OpenCodeException(401, "unauthorized") },
            ["new"] = new(),
        };
        clients["new"].Sessions.Add(WorkingSession("ses_work", Now));

        await using var monitor = NewMonitor(
            discovery,
            descriptor => clients[descriptor.Password],
            new MutableTimeProvider(Now));
        await monitor.StartAsync(CancellationToken.None);

        Assert.Empty(monitor.Rows);
        Assert.False(monitor.Services.Single().Online);

        discovery.Candidates.Clear();
        discovery.Candidates.Add(Candidate(password: "new"));
        await monitor.RescanAsync(CancellationToken.None);

        Assert.Single(monitor.Rows);
        Assert.True(monitor.Services.Single().Online);
    }

    [Fact]
    public async Task PrimaryFails_AlternateSucceeds_ServiceStaysOnline()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate(url: "http://127.0.0.1:1", alternate: new Uri("http://10.0.0.2:1")));
        var primary = new FakeClient { Failure = new HttpRequestException("no route") };
        var alternate = new FakeClient();
        alternate.Sessions.Add(WorkingSession("ses_work", Now));
        var byUrl = new Dictionary<string, FakeClient>
        {
            [new Uri("http://127.0.0.1:1").ToString()] = primary,
            [new Uri("http://10.0.0.2:1").ToString()] = alternate,
        };

        await using var monitor = NewMonitor(discovery, descriptor => byUrl[descriptor.Url.ToString()], new MutableTimeProvider(Now));
        await monitor.StartAsync(CancellationToken.None);

        Assert.Single(monitor.Rows);
        Assert.True(monitor.Services.Single().Online);
    }

    [Fact]
    public async Task Refresh_FetchesPermissionCounts()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient();
        client.Sessions.Add(WorkingSession("ses_work", Now));

        await using var monitor = NewMonitor(discovery, _ => client, new MutableTimeProvider(Now));
        await monitor.StartAsync(CancellationToken.None);
        Assert.Equal(SessionState.Working, monitor.Rows.Single().State);

        client.PermissionCount = 1;
        var before = client.PermissionCalls;
        await monitor.RefreshNowAsync(CancellationToken.None);

        Assert.True(client.PermissionCalls > before);
        Assert.Equal(SessionState.WaitingApproval, monitor.Rows.Single().State);
    }

    [Fact]
    public async Task Rows_AreSortedBlockedFirst()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate(key: "native", url: "http://127.0.0.1:1"));
        discovery.Candidates.Add(Candidate(key: "wsl:Ubuntu", url: "http://127.0.0.1:2"));
        var native = new FakeClient();
        native.Sessions.Add(WorkingSession("ses_work", Now));
        var ubuntu = new FakeClient { PermissionCount = 1 };
        ubuntu.Sessions.Add(AmberSession("ses_amber", Now));

        await using var monitor = NewMonitor(
            discovery,
            descriptor => descriptor.Key == "native" ? native : ubuntu,
            new MutableTimeProvider(Now));
        await monitor.StartAsync(CancellationToken.None);

        Assert.Equal(2, monitor.Rows.Count);
        Assert.True(monitor.Rows[0].IsBlocked);
        Assert.Equal(SessionState.WaitingApproval, monitor.Rows[0].State);
    }

    [Fact]
    public async Task NoServices_DoesNotThrow()
    {
        await using var monitor = NewMonitor(new FakeDiscovery(), _ => new FakeClient(), new MutableTimeProvider(Now));

        await monitor.StartAsync(CancellationToken.None);

        Assert.Empty(monitor.Rows);
        Assert.Empty(monitor.Services);
    }

    [Fact]
    public async Task MarkViewed_ExpiresWhenNewTurnStarts()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient();
        client.Sessions.Add(BlueSession("ses_blue", Now));
        var time = new MutableTimeProvider(Now);

        await using var monitor = NewMonitor(discovery, _ => client, time);
        await monitor.StartAsync(CancellationToken.None);
        await monitor.MarkViewedAsync("native", "ses_blue");
        Assert.Equal(SessionState.Idle, monitor.Rows.Single().State);

        time.Advance(TimeSpan.FromMinutes(1));
        var idle = time.Now.AddSeconds(-1);
        client.Sessions.Clear();
        client.Sessions.Add(Session("ses_blue", idle, idle, idle.AddMinutes(-10)));
        await monitor.RefreshNowAsync(CancellationToken.None);

        Assert.Equal(SessionState.WaitingForYou, monitor.Rows.Single().State);
    }

    [Fact]
    public async Task MarkViewed_OnAmber_DoesNotRefireBlocked()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient { PermissionCount = 1 };
        client.Sessions.Add(AmberSession("ses_amber", Now));

        await using var monitor = NewMonitor(discovery, _ => client, new MutableTimeProvider(Now));
        await monitor.StartAsync(CancellationToken.None);

        var fired = new List<BlockedSessionEventArgs>();
        monitor.BlockedStarted += (_, args) => fired.Add(args);

        await monitor.MarkViewedAsync("native", "ses_amber");
        await monitor.RefreshNowAsync(CancellationToken.None);

        Assert.Empty(fired);
        Assert.Equal(SessionState.WaitingApproval, monitor.Rows.Single().State);
    }

    [Fact]
    public async Task Start_AlreadyPending_IsAmberFromFirstRefresh()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient { PermissionCount = 1 };
        var idle = Now.AddMinutes(-10);
        client.Sessions.Add(Session("ses_pending", idle, idle, idle));

        await using var monitor = NewMonitor(discovery, _ => client, new MutableTimeProvider(Now));
        await monitor.StartAsync(CancellationToken.None);

        var row = Assert.Single(monitor.Rows);
        Assert.Equal(SessionState.WaitingApproval, row.State);
        Assert.Equal(idle, row.StateSince);
    }

    [Fact]
    public async Task RemovedService_KeepsRowsForGraceThenDrops()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var client = new FakeClient();
        client.Sessions.Add(IdleSession("ses_idle", Now));
        var time = new MutableTimeProvider(Now);

        await using var monitor = NewMonitor(discovery, _ => client, time);
        await monitor.StartAsync(CancellationToken.None);
        Assert.Single(monitor.Rows);

        client.Failure = new HttpRequestException("down");
        discovery.Candidates.Clear();
        await monitor.RescanAsync(CancellationToken.None);

        Assert.True(Assert.Single(monitor.Rows).IsOffline);
        Assert.Contains(monitor.Services, service => service.Key == "native" && !service.Online);

        time.Advance(TimeSpan.FromMinutes(6));
        await monitor.RefreshNowAsync(CancellationToken.None);

        Assert.Empty(monitor.Rows);
    }

    [Fact]
    public async Task Start_DiscoveryFailure_DoesNotThrow()
    {
        var discovery = new FakeDiscovery { Throw = new InvalidOperationException("wsl missing") };

        await using var monitor = NewMonitor(discovery, _ => new FakeClient(), new MutableTimeProvider(Now));

        await monitor.StartAsync(CancellationToken.None);

        Assert.Empty(monitor.Rows);
        Assert.Empty(monitor.Services);
    }

    [Fact]
    public async Task OpenStep_KeepsWorkingThroughALongToolRun()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var events = Channel.CreateUnbounded<ServerEvent>();
        var client = new FakeClient { Events = events };
        client.Sessions.Add(IdleSession("ses_tool", Now));
        var time = new MutableTimeProvider(Now);

        await using var monitor = NewMonitor(discovery, _ => client, time);
        await monitor.StartAsync(CancellationToken.None);
        Assert.Equal(SessionState.Idle, monitor.Rows.Single().State);

        await PublishAsync(events, new ServerEvent("session.step.started", Now, "ses_tool"));
        time.Advance(TimeSpan.FromMinutes(5));
        await monitor.RefreshNowAsync(CancellationToken.None);

        Assert.Equal(SessionState.Working, monitor.Rows.Single().State);

        await PublishAsync(events, new ServerEvent("session.step.ended", time.Now, "ses_tool"));
        time.Advance(TimeSpan.FromMinutes(1));
        await monitor.RefreshNowAsync(CancellationToken.None);

        Assert.Equal(SessionState.Idle, monitor.Rows.Single().State);
    }

    [Fact]
    public async Task Refresh_ClosesStepWhenServiceReportsNewerIdle()
    {
        var discovery = new FakeDiscovery();
        discovery.Candidates.Add(Candidate());
        var events = Channel.CreateUnbounded<ServerEvent>();
        var client = new FakeClient { Events = events };
        client.Sessions.Add(IdleSession("ses_tool", Now));
        var time = new MutableTimeProvider(Now);

        await using var monitor = NewMonitor(discovery, _ => client, time);
        await monitor.StartAsync(CancellationToken.None);
        await PublishAsync(events, new ServerEvent("session.step.started", Now, "ses_tool"));
        time.Advance(TimeSpan.FromMinutes(5));
        await monitor.RefreshNowAsync(CancellationToken.None);
        Assert.Equal(SessionState.Working, monitor.Rows.Single().State);

        // The turn ended while the stream was down: only time.idle tells us.
        var idle = time.Now;
        client.Sessions.Clear();
        client.Sessions.Add(Session("ses_tool", idle, idle, idle));
        time.Advance(TimeSpan.FromMinutes(1));
        await monitor.RefreshNowAsync(CancellationToken.None);

        Assert.Equal(SessionState.Idle, monitor.Rows.Single().State);
    }

    private static async Task PublishAsync(Channel<ServerEvent> events, ServerEvent serverEvent)
    {
        await events.Writer.WriteAsync(serverEvent);
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (events.Reader.Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        // The pump applies the frame right after reading it.
        await Task.Delay(50);
    }

    private static SessionMonitor NewMonitor(
        FakeDiscovery discovery,
        Func<ServiceDescriptor, IOpenCodeClient> factory,
        MutableTimeProvider time,
        Settings? settings = null)
        => new(discovery, factory, settings ?? new Settings(), time);

    private static ServiceCandidate Candidate(
        string key = "native",
        string url = "http://127.0.0.1:1",
        string password = "pw",
        Uri? alternate = null)
        => new(key, key == "native" ? "Windows" : key, new Uri(url), password, alternate);

    private static SessionInfo IdleSession(string id, DateTimeOffset now)
    {
        var idle = now.AddMinutes(-5);
        return Session(id, idle, idle, idle);
    }

    private static SessionInfo WorkingSession(string id, DateTimeOffset now)
        => Session(id, now.AddMinutes(-10), now.AddSeconds(-1), null, lastEventAt: now.AddSeconds(-1));

    private static SessionInfo BlueSession(string id, DateTimeOffset now)
        => Session(id, now.AddMinutes(-5), now.AddMinutes(-5), now.AddMinutes(-10));

    private static SessionInfo AmberSession(string id, DateTimeOffset now)
        => Session(id, now.AddMinutes(-5), now.AddMinutes(-5), now.AddMinutes(-5), pending: 1, pendingSince: now.AddMinutes(-5));

    private static SessionInfo Session(
        string id,
        DateTimeOffset idle,
        DateTimeOffset updated,
        DateTimeOffset? viewed,
        int pending = 0,
        DateTimeOffset? pendingSince = null,
        DateTimeOffset? lastEventAt = null)
        => new()
        {
            Id = id,
            Title = id,
            Directory = @"C:\work",
            Outcome = "succeeded",
            PendingPermissions = pending,
            PendingSince = pendingSince,
            Created = idle.AddHours(-1),
            Updated = updated,
            Idle = idle,
            Viewed = viewed,
            LastEventAt = lastEventAt,
        };
}
