namespace OpenCodeVision.Core;

public sealed record SessionInfo
{
    public required string Id { get; init; }
    public string? ParentId { get; init; }
    public string Title { get; init; } = "";
    public string Directory { get; init; } = "";
    public string? Outcome { get; init; }
    public int PendingPermissions { get; init; }
    public DateTimeOffset? Created { get; init; }
    public DateTimeOffset? Updated { get; init; }
    public DateTimeOffset? Idle { get; init; }
    public DateTimeOffset? Viewed { get; init; }
    public DateTimeOffset? LastEventAt { get; init; }
    public DateTimeOffset? PendingSince { get; init; }

    /// <summary>True while opencode has an assistant step in flight for this session.</summary>
    public bool StepOpen { get; init; }

    /// <summary>When the open step started, used to bound a stale <see cref="StepOpen"/>.</summary>
    public DateTimeOffset? StepStartedAt { get; init; }
}
