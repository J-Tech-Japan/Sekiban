namespace Sekiban.EventSourcing.Queries.SingleAggregates;

public abstract class SingleAggregateProjectionBase<TAggregate, TProjection, TSingleAggregateContents> : ISingleAggregateProjection,
    ISingleAggregateProjectionDtoConvertible<SingleAggregateProjectionDto<TSingleAggregateContents>>, ISingleAggregate,
    ISingleAggregateProjector<TProjection> where TProjection : SingleAggregateProjectionBase<TAggregate, TProjection, TSingleAggregateContents>
    where TSingleAggregateContents : ISingleAggregateProjectionContents
    where TAggregate : AggregateBase, new()
{
    public TSingleAggregateContents Contents { get; set; } = default!;
    public Guid LastEventId { get; set; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int AppliedSnapshotVersion { get; set; }
    public int Version { get; set; }
    public bool IsDeleted { get; set; }
    public Guid AggregateId { get; set; }
    public void ApplyEvent(IAggregateEvent ev)
    {
        // IsAggregateInitialEvent は V0 の時のみ
        // IsAggregateInitialEvent == false は V0以外
        if (ev.IsAggregateInitialEvent != (Version == 0))
        {
            throw new SekibanInvalidEventException();
        }
        if (ev.Id == LastEventId) { return; }
        var action = GetApplyEventAction(ev);
        if (action is null) { return; }
        action();

        LastEventId = ev.Id;
        LastSortableUniqueId = ev.SortableUniqueId;
        Version++;
    }
    public bool CanApplyEvent(IAggregateEvent ev)
    {
        return GetApplyEventAction(ev) is not null;
    }
    public void ApplySnapshot(SingleAggregateProjectionDto<TSingleAggregateContents> snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        IsDeleted = snapshot.IsDeleted;
        Contents = snapshot.Contents;
    }
    public SingleAggregateProjectionDto<TSingleAggregateContents> ToDto()
    {
        return new SingleAggregateProjectionDto<TSingleAggregateContents>(
            Contents,
            AggregateId,
            IsDeleted,
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
    protected abstract Action? GetApplyEventAction(IAggregateEvent ev);
}
