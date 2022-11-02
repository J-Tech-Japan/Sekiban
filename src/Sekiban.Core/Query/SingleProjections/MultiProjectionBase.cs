using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
namespace Sekiban.Core.Query.SingleProjections;

public abstract class MultiProjectionBase<TAggregatePayload, TProjection, TProjectionPayload> : ISingleProjection,
    ISingleProjectionStateConvertible<SingleProjectionState<TProjectionPayload>>, IAggregateCommon,
    ISingleProjector<TProjection> where TProjection : MultiProjectionBase<TAggregatePayload, TProjection, TProjectionPayload>, new()
    where TProjectionPayload : ISingleProjectionPayload
    where TAggregatePayload : IAggregatePayload, new()
{
    public TProjectionPayload Payload { get; set; } = default!;
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
            throw new SekibanInvalidEventException();
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
    public TProjection CreateInitialAggregate(Guid aggregateId) => new()
        { AggregateId = aggregateId };

    public Type OriginalAggregateType() => typeof(TAggregatePayload);
    public bool GetIsDeleted() => Payload is IDeletable { IsDeleted: true };
    protected Action? GetApplyEventAction(IEvent ev, IEventPayload eventPayload)
    {
        var func = GetApplyEventFunc(ev, eventPayload);
        return () =>
        {
            if (func == null) { return; }
            var result = func(Payload);
            Payload = result;
        };
    }
    protected abstract Func<TProjectionPayload, TProjectionPayload>? GetApplyEventFunc(
        IEvent ev,
        IEventPayload eventPayload);
}
