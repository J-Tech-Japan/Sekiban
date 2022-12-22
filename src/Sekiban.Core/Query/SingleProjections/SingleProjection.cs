using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
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
    public string GetPayloadVersionIdentifier() => Payload.GetPayloadVersionIdentifier();
    public bool EventShouldBeApplied(IEvent ev) => ev.GetSortableUniqueId().LaterThan(new SortableUniqueIdValue(LastSortableUniqueId));

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

    public SingleProjectionState<TProjectionPayload> ToState() => new(
        Payload,
        AggregateId,
        LastEventId,
        LastSortableUniqueId,
        AppliedSnapshotVersion,
        Version);

    public Type GetPayloadType() => typeof(TProjectionPayload);

    public SingleProjection<TProjectionPayload> CreateInitialAggregate(Guid aggregateId) => new()
        { AggregateId = aggregateId };

    public Type GetOriginalAggregatePayloadType() => typeof(TProjectionPayload).GetOriginalTypeFromSingleProjectionPayload();

    public bool GetIsDeleted() => Payload is IDeletable { IsDeleted: true };

    protected Action? GetApplyEventAction(IEvent ev, IEventPayloadCommon eventPayload)
    {
        var payload = Payload as ISingleProjectionEventApplicable<TProjectionPayload> ??
            throw new SekibanSingleProjectionMustInheritISingleProjectionEventApplicable();
        (ev, eventPayload) = EventHelper.GetConvertedEventAndPayloadIfConverted(ev, eventPayload);
        var func = payload.GetApplyEventFunc(ev, eventPayload);
        return () =>
        {
            if (func == null) { return; }
            var result = func(Payload);
            Payload = result;
        };
    }
}
