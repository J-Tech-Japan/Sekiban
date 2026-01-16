namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     In-memory/state representation of a multi-projection.
/// </summary>
public record MultiProjectionState(
    IMultiProjectionPayload Payload,
    string ProjectorName,
    string ProjectorVersion,
    string LastSortableUniqueId,
    Guid LastEventId,
    int Version,
    bool IsCatchedUp = true,
    bool IsSafeState = true,
    /// <summary>Whether catch-up from event store is currently in progress.</summary>
    bool IsCatchUpInProgress = false,
    /// <summary>Current position in catch-up process (SortableUniqueId value).</summary>
    string? CatchUpCurrentPosition = null,
    /// <summary>Target position for catch-up completion (SortableUniqueId value).</summary>
    string? CatchUpTargetPosition = null,
    /// <summary>Estimated catch-up progress percentage (0-100), null if not calculable.</summary>
    double? CatchUpProgressPercent = null);
