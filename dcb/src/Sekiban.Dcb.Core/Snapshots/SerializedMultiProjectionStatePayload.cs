using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Serialized multi-projection payload bytes plus the metadata needed to build
///     either an inline snapshot state or an offloaded snapshot reference.
/// </summary>
public sealed record SerializedMultiProjectionStatePayload(
    byte[] PayloadBytes,
    string MultiProjectionPayloadType,
    string ProjectorName,
    string ProjectorVersion,
    string LastSortableUniqueId,
    Guid LastEventId,
    int Version,
    bool IsCatchedUp,
    bool IsSafeState,
    long OriginalSizeBytes,
    long CompressedSizeBytes)
{
    public SerializableMultiProjectionState ToInlineState() =>
        SerializableMultiProjectionState.FromBytes(
            PayloadBytes,
            MultiProjectionPayloadType,
            ProjectorName,
            ProjectorVersion,
            LastSortableUniqueId,
            LastEventId,
            Version,
            IsCatchedUp,
            IsSafeState,
            OriginalSizeBytes,
            CompressedSizeBytes);

    public SerializableMultiProjectionStateOffloaded ToOffloadedState(
        string offloadKey,
        string storageProvider) =>
        new(
            OffloadKey: offloadKey,
            StorageProvider: storageProvider,
            MultiProjectionPayloadType: MultiProjectionPayloadType,
            ProjectorName: ProjectorName,
            ProjectorVersion: ProjectorVersion,
            LastSortableUniqueId: LastSortableUniqueId,
            LastEventId: LastEventId,
            Version: Version,
            IsCatchedUp: IsCatchedUp,
            IsSafeState: IsSafeState,
            PayloadLength: PayloadBytes.LongLength,
            OriginalSizeBytes: OriginalSizeBytes,
            CompressedSizeBytes: CompressedSizeBytes);
}
