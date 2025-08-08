using System;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
/// In-memory/state representation of a multi-projection.
/// </summary>
public record MultiProjectionState(
    IMultiProjectionPayload Payload,
    string ProjectorName,
    string ProjectorVersion,
    string LastSortableUniqueId,
    Guid LastEventId,
    int Version);
