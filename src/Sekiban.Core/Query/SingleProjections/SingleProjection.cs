using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Types;

namespace Sekiban.Core.Query.SingleProjections;

public class SingleProjection<TProjectionPayload> : ISingleProjection,
    ISingleProjectionStateConvertible<SingleProjectionState<TProjectionPayload>>, IAggregateCommon,
    ISingleProjector<SingleProjection<TProjectionPayload>>
    where TProjectionPayload : ISingleProjectionPayload, new()
{
    public TProjectionPayload Payload { get; set; } = new();
    public Guid LastEventId { get; set; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int AppliedSnapshotVersion { get; set; }
    public int Version { get; set; }
    public Guid AggregateId { get; init; }

    public void ApplyEvent(IEvent ev)
    {
        if (ev.Id == LastEventId) return;
        var action = GetApplyEventAction(ev, ev.GetPayload());
        if (action is null) return;
        action();

        LastEventId = ev.Id;
        LastSortableUniqueId = ev.SortableUniqueId;
        Version++;
    }

    public bool CanApplyEvent(IEvent ev)
    {
        return GetApplyEventAction(ev, ev.GetPayload()) is not null;
    }

    public void ApplySnapshot(SingleProjectionState<TProjectionPayload> snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        Payload = snapshot.Payload;
    }

    public SingleProjectionState<TProjectionPayload> ToState()
    {
        return new(
            Payload,
            AggregateId,
            LastEventId,
            LastSortableUniqueId,
            AppliedSnapshotVersion,
            Version);
    }

    public SingleProjection<TProjectionPayload> CreateInitialAggregate(Guid aggregateId)
    {
        return new()
            { AggregateId = aggregateId };
    }

    public Type OriginalAggregateType()
    {
        return typeof(TProjectionPayload).GetOriginalTypeFromSingleProjectionPayload();
    }

    public bool GetIsDeleted()
    {
        return Payload is IDeletable { IsDeleted: true };
    }

    protected Action? GetApplyEventAction(IEvent ev, IEventPayloadCommon eventPayload)
    {
        var payload = Payload as ISingleProjectionEventApplicable<TProjectionPayload> ??
                      throw new SekibanSingleProjectionMustInheritISingleProjectionEventApplicable();
        var func = payload.GetApplyEventFunc(ev, eventPayload);
        return () =>
        {
            if (func == null)
                throw new SekibanEventNotImplementedException(
                    $"{eventPayload.GetType().Name} Event not implemented on {GetType().Name} Projection");
            var result = func(Payload);
            Payload = result;
        };
    }
}