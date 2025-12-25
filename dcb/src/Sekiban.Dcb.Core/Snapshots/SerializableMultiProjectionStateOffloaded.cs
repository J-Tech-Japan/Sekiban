namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Offloaded snapshot metadata (payload placed in external storage).
/// </summary>
public sealed record SerializableMultiProjectionStateOffloaded(
    string OffloadKey,
    string StorageProvider,
    string MultiProjectionPayloadType,
    string ProjectorName,
    string ProjectorVersion,
    string LastSortableUniqueId,
    Guid LastEventId,
    int Version,
    bool IsCatchedUp,
    bool IsSafeState,
    long PayloadLength);