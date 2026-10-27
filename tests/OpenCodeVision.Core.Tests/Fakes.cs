using System.Runtime.CompilerServices;
using System.Threading.Channels;
using OpenCodeVision.Core;
using OpenCodeVision.Core.Discovery;

namespace OpenCodeVision.Core.Tests;

internal sealed class MutableTimeProvider : TimeProvider
{
    public MutableTimeProvider(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

internal sealed class FakeDiscovery : IServiceDiscovery
{
    public List<ServiceCandidate> Candidates { get; } = new();

    public Exception? Throw { get; set; }

    public int Calls { get; private set; }

    public Task<IReadOnlyList<ServiceCandidate>> DiscoverAsync(CancellationToken cancellationToken)
    {
        Calls++;
        if (Throw is not null)
        {
            return Task.FromException<IReadOnlyList<ServiceCandidate>>(Throw);
        }

        return Task.FromResult<IReadOnlyList<ServiceCandidate>>(Candidates.ToList());
    }
}

internal sealed class FakeClient : IOpenCodeClient
{
    public string Version { get; set; } = "2.2.0";

    public List<SessionInfo> Sessions { get; } = new();

    public int PermissionCount { get; set; }

    public Exception? Failure { get; set; }

    public int SessionCalls { get; private set; }

    public int PermissionCalls { get; private set; }

    public int InfoCalls { get; private set; }

    public Channel<ServerEvent>? Events { get; set; }

    public Task<ServerInfo> GetInfoAsync(CancellationToken cancellationToken)
    {
        InfoCalls++;
        return Failure is null
            ? Task.FromResult(new ServerInfo(Version, 1))
            : Task.FromException<ServerInfo>(Failure);
    }

    public Task<IReadOnlyList<SessionInfo>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        SessionCalls++;
        return Failure is null
            ? Task.FromResult<IReadOnlyList<SessionInfo>>(Sessions.ToList())
            : Task.FromException<IReadOnlyList<SessionInfo>>(Failure);
    }

    public Task<int> GetPendingPermissionCountAsync(string sessionId, CancellationToken cancellationToken)
    {
        PermissionCalls++;
        return Failure is null
            ? Task.FromResult(PermissionCount)
            : Task.FromException<int>(Failure);
    }

    public async IAsyncEnumerable<ServerEvent> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Events is null)
        {
            yield break;
        }

        await foreach (var serverEvent in Events.Reader.ReadAllAsync(cancellationToken))
        {
            yield return serverEvent;
        }
    }

    public void Dispose()
    {
    }
}
