using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
namespace Sekiban.Core.Query.SingleAggregate;

public abstract class SingleAggregateProjectionBase<TAggregate, TProjection, TSingleAggregateContents> : ISingleAggregateProjection,
    ISingleAggregateProjectionDtoConvertible<SingleAggregateProjectionDto<TSingleAggregateContents>>, ISingleAggregate,
    ISingleAggregateProjector<TProjection> where TProjection : SingleAggregateProjectionBase<TAggregate, TProjection, TSingleAggregateContents>
    where TSingleAggregateContents : ISingleAggregateProjectionPayload
    where TAggregate : AggregateCommonBase, new()
{
    public TSingleAggregateContents Payload { get; set; } = default!;
    public Guid LastEventId { get; set; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int AppliedSnapshotVersion { get; set; }
    public int Version { get; set; }
    public Guid AggregateId { get; set; }
    public bool GetIsDeleted()
    {
        return Payload is IDeletableAggregate { IsDeleted: true };
    }
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
    public void ApplySnapshot(SingleAggregateProjectionDto<TSingleAggregateContents> snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        Payload = snapshot.Payload;
    }
    public SingleAggregateProjectionDto<TSingleAggregateContents> ToState()
    {
        return new SingleAggregateProjectionDto<TSingleAggregateContents>(
            Payload,
            AggregateId,
            LastEventId,
            LastSortableUniqueId,
            AppliedSnapshotVersion,
            Version);
    }
    public abstract TProjection CreateInitialAggregate(Guid aggregateId);

    public Type OriginalAggregateType()
    {
        return typeof(TAggregate);
    }
    protected Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload)
    {
        var func = GetApplyEventFunc(ev, payload);
        return () =>
        {
            if (func == null) { return; }
            var result = func(Payload);
            Payload = result;
        };
    }
    protected abstract Func<TSingleAggregateContents, TSingleAggregateContents>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload);
}
