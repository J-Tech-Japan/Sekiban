using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Types;
namespace Sekiban.Core.Query.MultiProjections;

public class MultiProjection<TProjectionPayload> : IMultiProjector<TProjectionPayload>, IMultiProjectionBase
    where TProjectionPayload : IMultiProjectionPayloadCommon, new()
{
    private TProjectionPayload Payload { get; set; } = new();
    public Guid LastEventId { get; set; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int AppliedSnapshotVersion { get; set; }
    public int Version { get; set; }

    public void ApplyEvent(IEvent ev)
    {

        (ev, _) = EventHelper.GetConvertedEventAndPayloadIfConverted(ev, ev.GetPayload());

#if NET7_0_OR_GREATER
        var method = typeof(TProjectionPayload).GetMethod("ApplyEvent");
        var genericMethod = method?.MakeGenericMethod(ev.GetEventPayloadType());
        Payload = (TProjectionPayload)(genericMethod?.Invoke(typeof(TProjectionPayload), new object[] { Payload, ev }) ?? Payload);
#else
        var method = Payload.GetType().GetMethod("ApplyEventInstance");
        var genericMethod = method?.MakeGenericMethod(ev.GetEventPayloadType());
        Payload = (TProjectionPayload)(genericMethod?.Invoke(Payload, new object[] { Payload, ev }) ?? Payload);
#endif
        Version++;
        LastEventId = ev.Id;
        LastSortableUniqueId = ev.SortableUniqueId;
    }

    public MultiProjectionState<TProjectionPayload> ToState() => new(
        Payload,
        LastEventId,
        LastSortableUniqueId,
        AppliedSnapshotVersion,
        Version);
    public bool EventShouldBeApplied(IEvent ev) => ev.GetSortableUniqueId().LaterThan(new SortableUniqueIdValue(LastSortableUniqueId));

    public void ApplySnapshot(MultiProjectionState<TProjectionPayload> snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        Payload = snapshot.Payload;
    }

    public virtual IList<string> TargetAggregateNames()
    {
        var projectionPayload = Payload as IMultiProjectionPayload<TProjectionPayload> ??
            throw new SekibanMultiProjectionMustInheritISingleProjectionEventApplicable();
        return projectionPayload.GetTargetAggregatePayloads().GetAggregateNames();
    }
}
