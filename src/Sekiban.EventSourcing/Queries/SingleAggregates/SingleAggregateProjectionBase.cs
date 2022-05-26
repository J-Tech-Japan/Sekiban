namespace Sekiban.EventSourcing.Queries.SingleAggregates;

public abstract class SingleAggregateProjectionBase<T> : ISingleAggregateProjection, ISingleAggregateProjectionDtoConvertible<T>, ISingleAggregate,
    ISingleAggregateProjector<T> where T : ISingleAggregate, ISingleAggregateProjection
{
    public Guid LastEventId { get; set; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int AppliedSnapshotVersion { get; set; }
    public int Version { get; set; }
    public bool IsDeleted { get; set; }
    public Guid AggregateId { get; set; }
    public void ApplyEvent(AggregateEvent ev)
    {
        // IsAggregateInitialEvent は V0 の時のみ
        // IsAggregateInitialEvent == false は V0以外
        if (ev.IsAggregateInitialEvent != (Version == 0))
        {
            throw new SekibanInvalidEventException();
        }
        if (ev.Id == LastEventId) { return; }
        var action = GetApplyEventAction(ev);
        if (action == null) { return; }
        action();

        LastEventId = ev.Id;
        LastSortableUniqueId = ev.SortableUniqueId;
        Version++;
    }
    public bool CanApplyEvent(AggregateEvent ev) =>
        GetApplyEventAction(ev) != null;
    public abstract T ToDto();
    public void ApplySnapshot(T snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        IsDeleted = snapshot.IsDeleted;
        CopyPropertiesFromSnapshot(snapshot);
    }

    public abstract Type OriginalAggregateType();
    public abstract T CreateInitialAggregate(Guid aggregateId);
    protected abstract void CopyPropertiesFromSnapshot(T snapshot);
    protected abstract Action? GetApplyEventAction(AggregateEvent ev);
}
