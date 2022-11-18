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
        // IsAggregateInitialEvent は V0 の時のみ
        // IsAggregateInitialEvent == false は V0以外
        if (ev.IsAggregateInitialEvent != (Version == 0))
        {
            throw new SekibanFirstEventShouldBeCreateEventException(ev.GetPayload().GetType());
        }
        if (ev.IsAggregateInitialEvent && Version > 0)
        {
            throw new SekibanCreateEventCanOnlyUseInFirstEvent(ev.GetPayload().GetType(), Version);
        }
        if (ev.Id == LastEventId) { return; }
        var action = GetApplyEventAction(ev, ev.GetPayload());
        if (action is null) { return; }
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
    public SingleProjection<TProjectionPayload> CreateInitialAggregate(Guid aggregateId) => new()
        { AggregateId = aggregateId };

    public Type OriginalAggregateType() => GetType().GetOriginalTypeFromSingleProjection();
    public bool GetIsDeleted() => Payload is IDeletable { IsDeleted: true };
    protected Action? GetApplyEventAction(IEvent ev, IEventPayload eventPayload)
    {
        var payload = Payload as ISingleProjectionEventApplicable<TProjectionPayload> ??
            throw new SekibanSingleProjectionMustInheritISingleProjectionEventApplicable();
        var func = payload.GetApplyEventFunc(ev, eventPayload);
        return () =>
        {
            if (func == null)
            {
                throw new SekibanEventNotImplementedException($"{eventPayload.GetType().Name} Event not implemented on {GetType().Name} Projection");
            }
            var result = func(Payload);
            Payload = result;
        };
    }
}
