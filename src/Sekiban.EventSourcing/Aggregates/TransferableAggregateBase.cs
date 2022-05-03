namespace Sekiban.EventSourcing.Aggregates;

public abstract class TransferableAggregateBase<TDto> : AggregateBase,
    ISingleAggregateProjectionDtoConvertible<TDto>
    where TDto : AggregateDtoBase
{
    public TransferableAggregateBase(Guid aggregateId) : base(aggregateId) { }

    public abstract TDto ToDto();

    public void ApplySnapshot(TDto snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
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
