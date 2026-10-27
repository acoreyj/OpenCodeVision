using OpenCodeVision.Core.Discovery;

namespace OpenCodeVision.Core;

public sealed class SessionMonitor : IAsyncDisposable
{
    private static readonly TimeSpan OfflineGrace = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private readonly IServiceDiscovery _discovery;
    private readonly Func<ServiceDescriptor, IOpenCodeClient> _clientFactory;
    private readonly Settings _settings;
    private readonly TimeProvider _time;
    private readonly Action<string>? _log;
    private readonly Dictionary<string, Runtime> _runtimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _viewedAt = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private HashSet<string> _blockedKeys = new(StringComparer.Ordinal);
    private IReadOnlyList<SessionRow> _rows = Array.Empty<SessionRow>();
    private IReadOnlyList<ServiceStatus> _services = Array.Empty<ServiceStatus>();
    private ITimer? _rescanTimer;
    private ITimer? _refreshTimer;
    private bool _baselineReady;
    private bool _started;

    public SessionMonitor(
        IServiceDiscovery discovery,
        Func<ServiceDescriptor, IOpenCodeClient> clientFactory,
        Settings settings,
        TimeProvider time,
        Action<string>? log = null)
    {
        _discovery = discovery;
        _clientFactory = clientFactory;
        _settings = settings;
        _time = time;
        _log = log;
    }

    public IReadOnlyList<SessionRow> Rows => _rows;

    public IReadOnlyList<ServiceStatus> Services => _services;

    public event EventHandler? Changed;

    public event EventHandler<BlockedSessionEventArgs>? BlockedStarted;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        _started = true;

        _rescanTimer = _time.CreateTimer(
            _ => _ = SafeAsync(() => RescanAsync(_cts.Token)),
            null,
            RescanInterval,
            RescanInterval);

        _refreshTimer = _time.CreateTimer(
            _ => _ = SafeAsync(() => RefreshGuardedAsync(fetchPermissions: true, wait: false, _cts.Token)),
            null,
            RefreshInterval,
            RefreshInterval);

        try
        {
            await RescanAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log?.Invoke("initial rescan failed: " + exception.Message);
        }
    }

    public async Task RescanAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var candidates = await _discovery.DiscoverAsync(cancellationToken);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in candidates)
            {
                seen.Add(candidate.Key);
                var descriptor = new ServiceDescriptor(
                    candidate.Key,
                    candidate.DisplayName,
                    candidate.Url,
                    candidate.Password,
                    Version: null,
                    candidate.AlternateUrl);

                if (_runtimes.TryGetValue(candidate.Key, out var existing))
                {
                    existing.Missing = false;
                    if (!SameTarget(existing.Primary, descriptor))
                    {
                        existing.Primary = descriptor;
                        existing.Active = descriptor;
                        SwapClient(existing);
                    }
                    else
                    {
                        existing.Primary = existing.Primary with { DisplayName = descriptor.DisplayName };
                    }
                }
                else
                {
                    var runtime = new Runtime(descriptor, _settings.IsServiceEnabled(candidate.Key));
                    runtime.Client = _clientFactory(descriptor);
                    _runtimes[candidate.Key] = runtime;
                    if (runtime.Enabled)
                    {
                        StartPump(runtime);
                    }
                }
            }

            // A service whose file disappeared stays known and offline; its rows
            // linger through the five-minute grace period instead of vanishing.
            foreach (var runtime in _runtimes.Values.Where(runtime => !seen.Contains(runtime.Primary.Key)))
            {
                runtime.Missing = true;
                runtime.Online = false;
            }

            await RefreshCoreAsync(fetchPermissions: true, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task RefreshNowAsync(CancellationToken cancellationToken)
        => RefreshGuardedAsync(fetchPermissions: true, wait: true, cancellationToken);

    public async Task SetServiceEnabledAsync(
        string key,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_runtimes.TryGetValue(key, out var runtime) || runtime.Enabled == enabled)
            {
                return;
            }

            runtime.Enabled = enabled;
            if (enabled)
            {
                StartPump(runtime);
            }
            else
            {
                StopPump(runtime);
                runtime.Online = false;
                runtime.LastRows.Clear();
                DropBlockedFor(runtime.Primary.Key);
            }

            if (enabled)
            {
                await RefreshCoreAsync(fetchPermissions: true, cancellationToken);
            }

            RebuildRows(_time.GetUtcNow());
            UpdateServices();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkViewedAsync(
        string serviceKey,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _viewedAt[sessionId] = _time.GetUtcNow();
            RebuildRows(_time.GetUtcNow());
            _blockedKeys = CurrentBlockedKeys();
            UpdateServices();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _rescanTimer?.Dispose();
        _refreshTimer?.Dispose();

        foreach (var runtime in _runtimes.Values)
        {
            runtime.PumpCts?.Cancel();
        }

        foreach (var runtime in _runtimes.Values)
        {
            try
            {
                runtime.Client.Dispose();
            }
            catch
            {
                // Disposal is best effort.
            }
        }

        var pumps = _runtimes.Values
            .Select(runtime => runtime.PumpTask)
            .Where(task => task is not null)
            .Cast<Task>()
            .ToArray();

        try
        {
            await Task.WhenAll(pumps).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Cancellation or a broken stream during shutdown is expected.
        }

        foreach (var runtime in _runtimes.Values)
        {
            runtime.PumpCts?.Dispose();
        }

        _gate.Dispose();
        _cts.Dispose();
    }

    private async Task RefreshGuardedAsync(bool fetchPermissions, bool wait, CancellationToken cancellationToken)
    {
        if (wait)
        {
            await _gate.WaitAsync(cancellationToken);
        }
        else if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            await RefreshCoreAsync(fetchPermissions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshCoreAsync(bool fetchPermissions, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();

        foreach (var runtime in _runtimes.Values.Where(runtime => runtime.Enabled).ToList())
        {
            var previousOnline = runtime.Online;
            var before = SessionsSnapshot(runtime)
                .ToDictionary(
                    session => session.Id,
                    session => StatusEvaluator.Evaluate(session, IsLocallyViewed(session), now),
                    StringComparer.Ordinal);

            var online = await FetchAsync(runtime, now, cancellationToken);
            runtime.Online = online;

            if (online && fetchPermissions)
            {
                await FetchPermissionsAsync(runtime, now, cancellationToken);
            }

            if (!online && previousOnline && runtime.Error is not null)
            {
                _log?.Invoke($"{runtime.Primary.Key} offline: {runtime.Error}");
            }

            LogChanges(runtime, before, now);
        }

        RebuildRows(now);
        UpdateServices();
        Changed?.Invoke(this, EventArgs.Empty);
        RaiseBlockedStarted();
    }

    private async Task<bool> FetchAsync(Runtime runtime, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var candidates = new List<ServiceDescriptor> { runtime.Active };

        ServiceDescriptor? other;
        if (runtime.Active.Url == runtime.Primary.Url)
        {
            other = runtime.Primary.AlternateUrl is { } alternate
                ? runtime.Primary with { Url = alternate, AlternateUrl = null }
                : null;
        }
        else
        {
            other = runtime.Primary;
        }

        if (other is not null && other.Url != runtime.Active.Url)
        {
            candidates.Add(other);
        }

        foreach (var candidate in candidates)
        {
            if (candidate.Url != runtime.Active.Url)
            {
                runtime.Active = candidate;
                SwapClient(runtime);
            }

            if (await TryFetchAsync(runtime, now, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> TryFetchAsync(Runtime runtime, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var info = await runtime.Client.GetInfoAsync(cancellationToken);
            var sessions = await runtime.Client.GetSessionsAsync(cancellationToken);
            runtime.Version = info.Version;
            MergeSessions(runtime, sessions);
            runtime.LastOnlineAt = now;
            runtime.Error = null;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            runtime.Error = exception.Message;
            return false;
        }
    }

    private async Task FetchPermissionsAsync(Runtime runtime, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var snapshot = SessionsSnapshot(runtime).Where(session => session.ParentId is null).ToList();

        foreach (var session in snapshot)
        {
            try
            {
                var count = await runtime.Client.GetPendingPermissionCountAsync(session.Id, cancellationToken);
                lock (runtime.Sync)
                {
                    var index = runtime.Sessions.FindIndex(candidate => candidate.Id == session.Id);
                    if (index < 0)
                    {
                        continue;
                    }

                    var current = runtime.Sessions[index];
                    var earliest = current.Idle ?? current.Updated ?? now;
                    runtime.Sessions[index] = current with
                    {
                        PendingPermissions = count,
                        PendingSince = count > 0 ? current.PendingSince ?? earliest : null,
                    };
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A single failed permission probe must not hide the session.
            }
        }
    }

    private static void MergeSessions(Runtime runtime, IReadOnlyList<SessionInfo> sessions)
    {
        lock (runtime.Sync)
        {
            var previous = runtime.Sessions.ToDictionary(session => session.Id, StringComparer.Ordinal);
            runtime.Sessions = sessions
                .Select(session => previous.TryGetValue(session.Id, out var old)
                    ? session with
                    {
                        LastEventAt = old.LastEventAt,
                        PendingSince = old.PendingSince,
                        PendingPermissions = old.PendingPermissions,
                    }
                    : session)
                .ToList();
        }
    }

    private static List<SessionInfo> SessionsSnapshot(Runtime runtime)
    {
        lock (runtime.Sync)
        {
            return runtime.Sessions.ToList();
        }
    }

    private void RebuildRows(DateTimeOffset now)
    {
        var online = _runtimes.Values.Where(runtime => runtime.Enabled && runtime.Online).ToList();
        var serviceSessions = online
            .Select(runtime => new ServiceSessions(
                runtime.Primary.Key,
                runtime.Primary.DisplayName,
                SessionsSnapshot(runtime)))
            .ToList();

        var rows = StatusEvaluator.BuildRows(serviceSessions, LocallyViewedIds(now), now).ToList();

        foreach (var runtime in online)
        {
            runtime.LastRows = rows.Where(row => row.ServiceKey == runtime.Primary.Key).ToList();
        }

        foreach (var runtime in _runtimes.Values.Where(runtime => runtime.Enabled && !runtime.Online).ToList())
        {
            if (runtime.LastOnlineAt is { } lastOnline && now - lastOnline <= OfflineGrace)
            {
                rows.AddRange(runtime.LastRows.Select(row => row with { IsOffline = true, IsBlocked = false }));
            }
            else
            {
                runtime.LastRows.Clear();
            }
        }

        _rows = rows;
    }

    private HashSet<string> LocallyViewedIds(DateTimeOffset now)
    {
        // BuildRows receives a set of session ids, so resolve the turn-scoped
        // marks against the current snapshots before calling it.
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var runtime in _runtimes.Values.Where(runtime => runtime.Enabled && runtime.Online))
        {
            foreach (var session in SessionsSnapshot(runtime))
            {
                if (IsLocallyViewed(session))
                {
                    ids.Add(session.Id);
                }
            }
        }

        return ids;
    }

    private bool IsLocallyViewed(SessionInfo session)
    {
        if (!_viewedAt.TryGetValue(session.Id, out var markedAt))
        {
            return false;
        }

        var reference = session.Idle ?? session.Updated;
        return reference is null || reference <= markedAt;
    }

    private void UpdateServices()
    {
        _services = _runtimes.Values
            .Select(runtime => new ServiceStatus(
                runtime.Primary.Key,
                runtime.Primary.DisplayName,
                runtime.Active.Url,
                runtime.Version,
                runtime.Enabled,
                runtime.Online))
            .ToList();
    }

    private void RaiseBlockedStarted()
    {
        var blocked = CurrentBlockedKeys();

        if (!_baselineReady)
        {
            _baselineReady = true;
            _blockedKeys = blocked;
            return;
        }

        foreach (var key in blocked.Except(_blockedKeys))
        {
            var separator = key.IndexOf('\0');
            if (separator <= 0)
            {
                continue;
            }

            BlockedStarted?.Invoke(this, new BlockedSessionEventArgs(key[..separator], key[(separator + 1)..]));
        }

        _blockedKeys = blocked;
    }

    private HashSet<string> CurrentBlockedKeys()
        => _rows
            .Where(row => row.IsBlocked && !row.IsOffline)
            .Select(row => BlockedKey(row.ServiceKey, row.Session.Id))
            .ToHashSet(StringComparer.Ordinal);

    private void DropBlockedFor(string serviceKey)
    {
        var prefix = serviceKey + "\0";
        _blockedKeys.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string BlockedKey(string serviceKey, string sessionId) => serviceKey + "\0" + sessionId;

    private void LogChanges(Runtime runtime, IReadOnlyDictionary<string, SessionState> before, DateTimeOffset now)
    {
        if (_log is null)
        {
            return;
        }

        foreach (var session in SessionsSnapshot(runtime))
        {
            var state = StatusEvaluator.Evaluate(session, IsLocallyViewed(session), now);
            if (before.TryGetValue(session.Id, out var old) && old != state)
            {
                _log($"{runtime.Primary.Key} {session.Id} {old} -> {state}");
            }
        }
    }

    private void StartPump(Runtime runtime)
    {
        if (runtime.PumpCts is not null)
        {
            return;
        }

        runtime.PumpCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        runtime.PumpTask = PumpAsync(runtime, runtime.PumpCts.Token);
    }

    private void StopPump(Runtime runtime)
    {
        runtime.PumpCts?.Cancel();
        runtime.PumpCts = null;
        runtime.PumpTask = null;
    }

    private async Task PumpAsync(Runtime runtime, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var serverEvent in runtime.Client.SubscribeAsync(cancellationToken))
                {
                    ApplyEvent(runtime, serverEvent);
                    ScheduleRefresh(runtime, serverEvent.Type.Contains("permission", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Reconnect after a broken stream.
            }

            try
            {
                await Task.Delay(ReconnectDelay, _time, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static void ApplyEvent(Runtime runtime, ServerEvent serverEvent)
    {
        if (serverEvent.SessionId is null)
        {
            return;
        }

        lock (runtime.Sync)
        {
            var index = runtime.Sessions.FindIndex(session => session.Id == serverEvent.SessionId);
            if (index >= 0)
            {
                runtime.Sessions[index] = runtime.Sessions[index] with { LastEventAt = serverEvent.Created };
            }
        }
    }

    private void ScheduleRefresh(Runtime runtime, bool permissions)
    {
        lock (runtime)
        {
            runtime.RefreshPermissions |= permissions;
            runtime.RefreshVersion++;
            var version = runtime.RefreshVersion;
            _ = DebouncedRefreshAsync(runtime, version);
        }
    }

    private async Task DebouncedRefreshAsync(Runtime runtime, int version)
    {
        try
        {
            await Task.Delay(Debounce, _time, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        bool permissions;
        lock (runtime)
        {
            if (runtime.RefreshVersion != version)
            {
                return;
            }

            permissions = runtime.RefreshPermissions;
            runtime.RefreshPermissions = false;
        }

        await SafeAsync(() => RefreshGuardedAsync(permissions, wait: false, _cts.Token));
    }

    private void SwapClient(Runtime runtime)
    {
        StopPump(runtime);
        try
        {
            runtime.Client.Dispose();
        }
        catch
        {
            // Disposal is best effort.
        }

        runtime.Client = _clientFactory(runtime.Active);
        runtime.Version = null;

        if (runtime.Enabled)
        {
            StartPump(runtime);
        }
    }

    private static bool SameTarget(ServiceDescriptor left, ServiceDescriptor right)
        => left.Url == right.Url
            && left.Password == right.Password
            && left.AlternateUrl == right.AlternateUrl;

    private static async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            // Background refresh and rescan failures are surfaced through service status.
        }
    }

    private sealed class Runtime
    {
        public Runtime(ServiceDescriptor primary, bool enabled)
        {
            Primary = primary;
            Active = primary;
            Enabled = enabled;
        }

        public ServiceDescriptor Primary { get; set; }

        public ServiceDescriptor Active { get; set; }

        public IOpenCodeClient Client { get; set; } = null!;

        public List<SessionInfo> Sessions { get; set; } = new();

        public object Sync { get; } = new();

        public List<SessionRow> LastRows { get; set; } = new();

        public DateTimeOffset? LastOnlineAt { get; set; }

        public string? Version { get; set; }

        public string? Error { get; set; }

        public bool Online { get; set; }

        public bool Enabled { get; set; }

        public bool Missing { get; set; }

        public CancellationTokenSource? PumpCts { get; set; }

        public Task? PumpTask { get; set; }

        public int RefreshVersion { get; set; }

        public bool RefreshPermissions { get; set; }
    }
}
