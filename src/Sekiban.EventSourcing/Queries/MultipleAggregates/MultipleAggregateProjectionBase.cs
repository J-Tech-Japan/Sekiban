namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public abstract class MultipleAggregateProjectionBase<TContents> : IMultipleAggregateProjector<TContents>
    where TContents : IMultipleAggregateProjectionContents, new()
{
    public TContents Contents { get; protected set; } = new();
    public Guid LastEventId { get; set; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int AppliedSnapshotVersion { get; set; }
    public int Version { get; set; }
    public void ApplyEvent(IAggregateEvent ev)
    {
        var action = GetApplyEventAction(ev);
        if (action is null) { return; }
        action();
        Version++;
        LastEventId = ev.Id;
        LastSortableUniqueId = ev.SortableUniqueId;
    }
    public MultipleAggregateProjectionContentsDto<TContents> ToDto()
    {
        return new MultipleAggregateProjectionContentsDto<TContents>(Contents, LastEventId, LastSortableUniqueId, AppliedSnapshotVersion, Version);
    }
    public void ApplySnapshot(MultipleAggregateProjectionContentsDto<TContents> snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        Contents = snapshot.Contents;
    }
    public virtual IList<string> TargetAggregateNames()
    {
        return new List<string>();
    }
    protected abstract Action? GetApplyEventAction(IAggregateEvent ev);
}
