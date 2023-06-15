using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

public record SingleProjectionState<TPayload>(
    TPayload Payload,
    Guid AggregateId,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version,
    string RootPartitionKey) : ISingleProjectionPayloadCommon, IAggregateStateCommon where TPayload : ISingleProjectionPayloadCommon
{
    public string PayloadTypeName => Payload.GetType().Name;


    public SingleProjectionState() : this(
        default!,
        Guid.Empty,
        Guid.Empty,
        string.Empty,
        0,
        0,
        string.Empty)
    {
    }
    public SingleProjectionState(IAggregateCommon aggregateCommon) : this(
        default!,
        aggregateCommon.AggregateId,
        aggregateCommon.LastEventId,
        aggregateCommon.LastSortableUniqueId,
        aggregateCommon.AppliedSnapshotVersion,
        aggregateCommon.Version,
        aggregateCommon.RootPartitionKey)
    {
    }

    public SingleProjectionState(IAggregateCommon aggregateCommon, TPayload payload) : this(aggregateCommon) => Payload = payload;
    public dynamic GetPayload() => Payload;
    public string GetPayloadVersionIdentifier() => Payload.GetPayloadVersionIdentifier();
    public bool GetIsDeleted() => Payload is IDeletable { IsDeleted: true };
}
