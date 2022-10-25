using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleAggregate;

public record SingleAggregateProjectionDto<TPayload>(
    TPayload Payload,
    Guid AggregateId,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version) : ISingleAggregateProjectionPayload, ISingleAggregate where TPayload : ISingleAggregateProjectionPayload
{
    public bool GetIsDeleted()
    {
        return Payload is IDeletable { IsDeleted: true };
    }
}
