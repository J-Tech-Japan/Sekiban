using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
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
        var action = GetApplyEventAction(ev, ev.GetPayload());
        if (action is null)
        {
            return;
        }
        action();
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
        return projectionPayload.TargetAggregateNames();
    }

    protected Action? GetApplyEventAction(IEvent ev, IEventPayloadCommon payload)
    {
        var projectionPayload = Payload as IMultiProjectionPayload<TProjectionPayload> ??
            throw new SekibanMultiProjectionMustInheritISingleProjectionEventApplicable();
        (ev, payload) = EventHelper.GetConvertedEventAndPayloadIfConverted(ev, payload);
        var func = projectionPayload.GetApplyEventFunc(ev, payload);
        return () =>
        {
            if (func == null)
            {
                return;
            }
            var result = func(Payload);
            Payload = result;
        };
    }
}
