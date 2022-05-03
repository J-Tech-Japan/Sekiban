namespace Sekiban.EventSourcing.Queries;

public abstract class SingleAggregateProjectionBase<T> : ISingleAggregateProjection,
    ISingleAggregateProjectionDtoConvertible<T>, ISingleAggregate, ISingleAggregateProjector<T>
    where T : ISingleAggregate, ISingleAggregateProjection
{
    public Guid LastEventId { get; set; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int AppliedSnapshotVersion { get; set; } = 0;
    public int Version { get; set; }
    public bool IsDeleted { get; set; }
    public Guid AggregateId { get; set; }
    public void ApplyEvent(AggregateEvent ev)
    {
        if (ev.IsAggregateInitialEvent == false && Version == 0)
        {
            throw new JJJnvalidEventException();
        }
        if (ev.Id == LastEventId) { return; }
        var action = GetApplyEventAction(ev);
        if (action == null) { return; }
        action();

        LastEventId = ev.Id;
        LastSortableUniqueId = ev.SortableUniqueId;
        Version++;
    }
    public abstract T ToDto();
    public abstract void ApplySnapshot(T snapshot);
    public abstract Type OriginalAggregateType();
    public abstract T CreateInitialAggregate(Guid aggregateId);
    protected abstract Action? GetApplyEventAction(AggregateEvent ev);
}
