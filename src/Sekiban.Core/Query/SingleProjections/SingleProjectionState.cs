using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

public record SingleProjectionState<TPayload>(
    TPayload Payload,
    Guid AggregateId,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version) : ISingleProjectionPayloadCommon, IAggregateCommon where TPayload : ISingleProjectionPayloadCommon
{
    public string PayloadTypeName => Payload.GetType().Name;
    public string GetPayloadVersionIdentifier() => Payload.GetPayloadVersionIdentifier();
    public bool GetIsDeleted() => Payload is IDeletable { IsDeleted: true };
}
