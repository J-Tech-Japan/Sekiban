namespace Sekiban.Dcb;

/// <summary>
///     Position information for a projection head.
/// </summary>
public sealed record ProjectionPosition(
    int EventVersion,
    string? LastSortableUniqueId);

/// <summary>
///     Catch-up progress information for a projection.
/// </summary>
public sealed record ProjectionCatchUpStatus(
    bool IsInProgress,
    string? CurrentSortableUniqueId,
    string? TargetSortableUniqueId,
    int PendingStreamEventCount);

/// <summary>
///     Public executor-facing status for a multi-projection.
/// </summary>
public sealed record ProjectionHeadStatus(
    string ProjectorName,
    string ProjectorVersion,
    ProjectionPosition Current,
    ProjectionPosition Consistent,
    ProjectionCatchUpStatus CatchUp);

/// <summary>
///     Global event store head information.
/// </summary>
public sealed record EventStoreHeadStatus(
    string? LatestSortableUniqueId,
    long? TotalEventCount);
