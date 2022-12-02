namespace Sekiban.Core.Query.MultiProjections;

public record MultiProjectionState<TProjectionPayload>(
    TProjectionPayload Payload,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version) : IProjection where TProjectionPayload : IMultiProjectionPayloadCommon
{
}
