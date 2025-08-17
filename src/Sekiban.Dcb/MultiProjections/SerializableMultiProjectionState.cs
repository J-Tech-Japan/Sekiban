namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Serializable form of a multi-projection state.
/// </summary>
public record SerializableMultiProjectionState(
    byte[] Payload,
    string MultiProjectionPayloadType,
    string ProjectorName,
    string ProjectorVersion,
    string LastSortableUniqueId,
    Guid LastEventId,
    int Version,
    bool IsCatchedUp = true,
    bool IsSafeState = true);
