using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
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
    public string GetPayloadVersionIdentifier()
    {
        return Payload.GetPayloadVersionIdentifier();
    }
    public bool EventShouldBeApplied(IEvent ev)
    {
        return ev.GetSortableUniqueId().LaterThanOrEqual(new SortableUniqueIdValue(LastSortableUniqueId));
    }

    public void ApplyEvent(IEvent ev)
    {
        if (ev.Id == LastEventId)
        {
            return;
        }
        (ev, var payload) = EventHelper.GetConvertedEventAndPayloadIfConverted(ev, ev.GetPayload());
        if (payload is UnregisteredEventPayload || payload is EmptyEventPayload)
        {
            return;
        }
#if NET7_0_OR_GREATER
        var method = typeof(TProjectionPayload).GetMethod("ApplyEvent");
        var genericMethod = method?.MakeGenericMethod(ev.GetEventPayloadType());
        Payload = (TProjectionPayload)(genericMethod?.Invoke(typeof(TProjectionPayload), new object[] { Payload, ev }) ?? Payload);
#else
        var method = Payload.GetType().GetMethod("ApplyEventInstance");
        var genericMethod = method?.MakeGenericMethod(ev.GetEventPayloadType());
        Payload = (TProjectionPayload)(genericMethod?.Invoke(Payload, new object[] { Payload, ev }) ?? Payload);
#endif


        LastEventId = ev.Id;
        LastSortableUniqueId = ev.SortableUniqueId;
        Version++;
    }

    public bool CanApplyEvent(IEvent ev)
    {
        return true;
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

    public Type GetPayloadType()
    {
        return typeof(TProjectionPayload);
    }

    public SingleProjection<TProjectionPayload> CreateInitialAggregate(Guid aggregateId)
    {
        return new()
            { AggregateId = aggregateId };
    }

    public Type GetOriginalAggregatePayloadType()
    {
        return typeof(TProjectionPayload).GetOriginalTypeFromSingleProjectionPayload();
    }

    public bool GetIsDeleted()
    {
        return Payload is IDeletable { IsDeleted: true };
    }
}
