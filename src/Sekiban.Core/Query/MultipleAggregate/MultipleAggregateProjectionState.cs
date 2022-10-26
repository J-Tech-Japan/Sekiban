namespace Sekiban.Core.Query.MultipleAggregate;

public record MultipleAggregateProjectionState<TProjectionPayload>(
    TProjectionPayload Payload,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version) : IProjection where TProjectionPayload : IMultipleAggregateProjectionPayload
{
}
