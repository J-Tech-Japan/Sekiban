using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Types;
namespace Sekiban.Core.Query.SingleProjections;

public class SingleProjection<TProjectionPayload> : ISingleProjection,
    ISingleProjectionStateConvertible<SingleProjectionState<TProjectionPayload>>, IAggregateCommon,
    ISingleProjector<SingleProjection<TProjectionPayload>>
    where TProjectionPayload : ISingleProjectionPayloadCommon, new()
{
    public TProjectionPayload Payload { get; set; } = new();
    public Guid LastEventId { get; set; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int AppliedSnapshotVersion { get; set; }
    public int Version { get; set; }
    public Guid AggregateId { get; init; }

    public void ApplyEvent(IEvent ev)
    {
        if (ev.Id == LastEventId)
        {
            return;
        }
        var action = GetApplyEventAction(ev, ev.GetPayload());
        if (action is null)
        {
            return;
        }
        action();

        LastEventId = ev.Id;
        LastSortableUniqueId = ev.SortableUniqueId;
        Version++;
    }

    public bool CanApplyEvent(IEvent ev) => GetApplyEventAction(ev, ev.GetPayload()) is not null;

    public void ApplySnapshot(SingleProjectionState<TProjectionPayload> snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        Payload = snapshot.Payload;
    }

    public SingleProjectionState<TProjectionPayload> ToState() => new SingleProjectionState<TProjectionPayload>(
        Payload,
        AggregateId,
        LastEventId,
        LastSortableUniqueId,
        AppliedSnapshotVersion,
        Version);

    public SingleProjection<TProjectionPayload> CreateInitialAggregate(Guid aggregateId) => new SingleProjection<TProjectionPayload>
        { AggregateId = aggregateId };

    public Type OriginalAggregateType() => typeof(TProjectionPayload).GetOriginalTypeFromSingleProjectionPayload();

    public bool GetIsDeleted() => Payload is IDeletable { IsDeleted: true };

    protected Action? GetApplyEventAction(IEvent ev, IEventPayloadCommon eventPayload)
    {
        var payload = Payload as ISingleProjectionEventApplicable<TProjectionPayload> ??
            throw new SekibanSingleProjectionMustInheritISingleProjectionEventApplicable();
        var func = payload.GetApplyEventFunc(ev, eventPayload);
        return () =>
        {
            if (func == null) { return; }
            var result = func(Payload);
            Payload = result;
        };
    }
}
