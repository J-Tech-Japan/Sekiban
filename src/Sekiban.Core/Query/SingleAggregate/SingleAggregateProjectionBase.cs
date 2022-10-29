using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
namespace Sekiban.Core.Query.SingleAggregate;

public abstract class SingleAggregateProjectionBase<TAggregate, TProjection, TProjectionPayload> : ISingleAggregateProjection,
    ISingleAggregateProjectionStateConvertible<SingleAggregateProjectionState<TProjectionPayload>>, ISingleAggregate,
    ISingleAggregateProjector<TProjection> where TProjection : SingleAggregateProjectionBase<TAggregate, TProjection, TProjectionPayload>, new()
    where TProjectionPayload : ISingleAggregateProjectionPayload
    where TAggregate : IAggregatePayload, new()
{
    public TProjectionPayload Payload { get; set; } = default!;
    public Guid LastEventId { get; set; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int AppliedSnapshotVersion { get; set; }
    public int Version { get; set; }
    public Guid AggregateId { get; init; }
    public void ApplyEvent(IAggregateEvent ev)
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
    public bool CanApplyEvent(IAggregateEvent ev)
    {
        return GetApplyEventAction(ev, ev.GetPayload()) is not null;
    }
    public void ApplySnapshot(SingleAggregateProjectionState<TProjectionPayload> snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        Payload = snapshot.Payload;
    }
    public SingleAggregateProjectionState<TProjectionPayload> ToState()
    {
        return new SingleAggregateProjectionState<TProjectionPayload>(
            Payload,
            AggregateId,
            LastEventId,
            LastSortableUniqueId,
            AppliedSnapshotVersion,
            Version);
    }
    public TProjection CreateInitialAggregate(Guid aggregateId)
    {
        return new TProjection
            { AggregateId = aggregateId };
    }

    public Type OriginalAggregateType()
    {
        return typeof(TAggregate);
    }
    public bool GetIsDeleted()
    {
        return Payload is IDeletable { IsDeleted: true };
    }
    protected Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload eventPayload)
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
        IAggregateEvent ev,
        IEventPayload eventPayload);
}
