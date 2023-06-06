using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
namespace Sekiban.Core.Query.MultiProjections;

public class MultiProjection<TProjectionPayload> : IMultiProjector<TProjectionPayload>, IMultiProjectionBase
    where TProjectionPayload : IMultiProjectionPayloadCommon, new()
{
    private MultiProjectionState<TProjectionPayload> state = new(new TProjectionPayload(), Guid.Empty, string.Empty, 0, 0);
    private TProjectionPayload Payload => state.Payload;
    public Guid LastEventId => state.LastEventId;
    public string LastSortableUniqueId => state.LastSortableUniqueId;
    public int AppliedSnapshotVersion => state.AppliedSnapshotVersion;
    public int Version => state.Version;

    public void ApplyEvent(IEvent ev)
    {
        (ev, var payload) = EventHelper.GetConvertedEventAndPayloadIfConverted(ev, ev.GetPayload());
        if (payload is UnregisteredEventPayload || payload is EmptyEventPayload)
        {
            return;
        }
        state = state.ApplyEvent(ev);
    }

    public MultiProjectionState<TProjectionPayload> ToState() => state;
    public bool EventShouldBeApplied(IEvent ev) => ev.GetSortableUniqueId().LaterThanOrEqual(new SortableUniqueIdValue(LastSortableUniqueId));

    public void ApplySnapshot(MultiProjectionState<TProjectionPayload> snapshot)
    {
        state = snapshot with { AppliedSnapshotVersion = snapshot.Version };
    }

    public virtual IList<string> TargetAggregateNames()
    {
        var projectionPayload = Payload as IMultiProjectionPayload<TProjectionPayload> ??
            throw new SekibanMultiProjectionMustInheritISingleProjectionEventApplicable();
        return projectionPayload.GetTargetAggregatePayloads().GetAggregateNames();
    }
    public string GetPayloadVersionIdentifier() => Payload.GetPayloadVersionIdentifier();
}
