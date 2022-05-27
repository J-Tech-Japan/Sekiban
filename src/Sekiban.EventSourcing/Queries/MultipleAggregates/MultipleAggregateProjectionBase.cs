namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public abstract class MultipleAggregateProjectionBase<TDto> : IMultipleAggregateProjector<TDto>, IMultipleAggregateProjectionDto
    where TDto : IMultipleAggregateProjectionDto
{

    public Guid LastEventId { get; set; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int AppliedSnapshotVersion { get; set; }
    public int Version { get; set; }
    public void ApplyEvent(AggregateEvent ev)
    {
        var action = GetApplyEventAction(ev);
        if (action == null) { return; }
        action();
        Version++;
        LastEventId = ev.Id;
        LastSortableUniqueId = ev.SortableUniqueId;
    }
    public abstract TDto ToDto();
    public void ApplySnapshot(TDto snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        CopyPropertiesFromSnapshot(snapshot);
    }
    public virtual IList<string> TargetAggregateNames() =>
        new List<string>();
    protected abstract Action? GetApplyEventAction(AggregateEvent ev);
    protected abstract void CopyPropertiesFromSnapshot(TDto snapshot);
}
