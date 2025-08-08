using System;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
/// Serializable form of a multi-projection state.
/// </summary>
public record SerializableMultiProjectionState(
    byte[] Payload,
    string MultiProjectionPayloadType,
    string LastSortableUniqueId,
    Guid LastEventId,
    int Version);
