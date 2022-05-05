namespace Sekiban.EventSourcing.Aggregates;

public abstract class TransferableAggregateBase<TDto> : AggregateBase,
    ISingleAggregateProjectionDtoConvertible<TDto>
    where TDto : AggregateDtoBase
{
    public TransferableAggregateBase(Guid aggregateId) : base(aggregateId) { }

    public abstract TDto ToDto();

    public void ApplySnapshot(TDto snapshot)
    {
        Console.WriteLine(
            $"Apply snapshot - version {snapshot.Version}, {snapshot.LastEventId}, {snapshot.LastSortableUniqueId}");
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        IsDeleted = snapshot.IsDeleted;
        CopyPropertiesFromSnapshot(snapshot);
    }
    protected abstract void CopyPropertiesFromSnapshot(TDto snapshot);

    protected override void AddAndApplyEvent(AggregateEvent ev)
    {
        if (GetApplyEventAction(ev) == null)
        {
            throw new JJEventNotImplementedException();
        }

        // Add Event
        _events.Add(ev);

        // Apply Event
        ApplyEvent(ev);
        ev.SetVersion(Version);
    }
}
