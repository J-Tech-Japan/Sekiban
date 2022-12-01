using Sekiban.Core.Aggregate;

namespace Sekiban.Core.Query.SingleProjections;

public record SingleProjectionState<TPayload>(
    TPayload Payload,
    Guid AggregateId,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version) : ISingleProjectionPayload, IAggregateCommon where TPayload : ISingleProjectionPayload
{
    public bool GetIsDeleted()
    {
        return Payload is IDeletable { IsDeleted: true };
    }
}
