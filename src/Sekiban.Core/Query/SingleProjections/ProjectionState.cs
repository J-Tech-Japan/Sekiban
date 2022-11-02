using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

public record ProjectionState<TPayload>(
    TPayload Payload,
    Guid AggregateId,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version) : ISingleProjectionPayload, IAggregateIdentifier where TPayload : ISingleProjectionPayload
{
    public bool GetIsDeleted() => Payload is IDeletable { IsDeleted: true };
}
