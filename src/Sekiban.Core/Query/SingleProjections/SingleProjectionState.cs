using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

public record SingleProjectionState<TPayload>(
    TPayload Payload,
    Guid AggregateId,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version) : ISingleProjectionPayloadCommon, IAggregateStateCommon where TPayload : ISingleProjectionPayloadCommon
{


    public SingleProjectionState() : this(
        default!,
        Guid.Empty,
        Guid.Empty,
        string.Empty,
        0,
        0)
    {
    }
    public SingleProjectionState(IAggregateCommon aggregateCommon) : this(
        default!,
        aggregateCommon.AggregateId,
        aggregateCommon.LastEventId,
        aggregateCommon.LastSortableUniqueId,
        aggregateCommon.AppliedSnapshotVersion,
        aggregateCommon.Version)
    {
    }

    public SingleProjectionState(IAggregateCommon aggregateCommon, TPayload payload) : this(aggregateCommon) => Payload = payload;
    public string PayloadTypeName => Payload.GetType().Name;
    public dynamic GetPayload() => Payload;
    public string GetPayloadVersionIdentifier() => Payload.GetPayloadVersionIdentifier();
    public bool GetIsDeleted() => Payload is IDeletable { IsDeleted: true };
}
