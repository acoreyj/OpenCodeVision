namespace OpenCodeSticky.Core;

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
}
