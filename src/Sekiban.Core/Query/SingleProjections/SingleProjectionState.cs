using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

public record SingleProjectionState<TPayload>(
    TPayload Payload,
    Guid AggregateId,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version) : ISingleProjectionPayload, ISingleAggregate where TPayload : ISingleProjectionPayload
{
    public bool GetIsDeleted() => Payload is IDeletable { IsDeleted: true };
}
